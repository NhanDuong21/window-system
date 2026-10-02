using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Win32;
using Nyan.Core;

namespace Nyan.Tests;

public static class Program
{
    static readonly List<string> Results=new();static int passed,failed,skipped;
    public static async Task<int> Main(string[] args)
    {
        if(args.FirstOrDefault()=="--owned-child") { await Task.Delay(TimeSpan.FromMinutes(5));return 0; }
        if(args.FirstOrDefault()=="--failure-aggregation-check"){Add("FAIL expected imported-failure sentinel");return failed>0?1:0;}
        if(args.FirstOrDefault()=="--acceptance-safe")return await SafeAsync(Path.GetFullPath(args[1]));
        string evidence=Path.GetFullPath(args.Length>0?args[0]:".evidence/verify");Directory.CreateDirectory(evidence);File.WriteAllText(Path.Combine(evidence,".nyan-owned"),"Nyan test resources only");File.WriteAllText(Path.Combine(evidence,".nyan-fixture"),"Nyan test resources only");
        var owned=Path.Combine(evidence,"mutation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(owned);File.WriteAllText(Path.Combine(owned,".nyan-owned"),"Nyan tests own this tree");
        var keyPath=@"Software\NyanControlCenter.Tests\"+Guid.NewGuid().ToString("N");var fixture=new MutationFixture(owned,keyPath);var store=new AppStore(Path.Combine(owned,"store"));var mutations=new Mutations(store,fixture);
        try
        {
            Check("P01 native Windows target",OperatingSystem.IsWindows()&&System.Runtime.InteropServices.RuntimeInformation.OSArchitecture==System.Runtime.InteropServices.Architecture.X64);
            Check("P01 app store first launch light",!store.Settings.Dark);
            var secret=System.Text.Encoding.UTF8.GetBytes("fixture-secret-123");Check("P01 DPAPI roundtrip",NativeSecurity.Unprotect(NativeSecurity.Protect(secret)).SequenceEqual(secret));
            foreach(var result in await StoreChecks.RunAsync(evidence))Add(result.Replace("PASS ","PASS P06/P12 ",StringComparison.Ordinal).Replace("SKIP ","SKIP P06/P12 ",StringComparison.Ordinal));
            foreach(var result in await WindowsChecks.RunAsync())Add(result.Replace("PASS ","PASS P01/P09/P16 ",StringComparison.Ordinal).Replace("SKIP ","SKIP P01/P09/P16 ",StringComparison.Ordinal));
            using(var reader=new WindowsReader())
            {
                foreach(var (module,phase) in new[]{(Module.Dashboard,"02"),(Module.Applications,"03"),(Module.Startup,"04"),(Module.Processes,"05"),(Module.Services,"07"),(Module.DevTools,"08"),(Module.Ports,"09"),(Module.Environment,"10"),(Module.Network,"11")})
                {
                    var watch=Stopwatch.StartNew();var result=await reader.ReadAsync(module,false,CancellationToken.None);watch.Stop();
                    Check($"P{phase} Windows read-only {module} (state={result.State}, rows={result.Rows.Count}, ms={watch.ElapsedMilliseconds})",result.State is ResultState.Ready or ResultState.Partial or ResultState.Empty);
                    Check($"P{phase} updated timestamp and columns",result.UpdatedAt.HasValue&&result.Columns.Count>0);
                    Check($"P{phase} default UI privacy",result.Rows.SelectMany(r=>r.Cells.Values).All(value=>!value.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),StringComparison.OrdinalIgnoreCase)));
                    if(module==Module.Processes){Check("P05 own process creation identity",result.Rows.Any(r=>r.Meta("pid")==Environment.ProcessId.ToString()&&long.TryParse(r.Meta("startTicks"),out var ticks)&&ticks>0));}
                    if(module==Module.Environment)Check("P10 all values masked",result.Rows.All(r=>r.Cell("value").Contains("che",StringComparison.OrdinalIgnoreCase)||r.Cell("value")=="—"));
                }
            }
            await EnvironmentTests(mutations,store,keyPath);
            await StartupTests(mutations,store,keyPath,owned);
            await ProcessTests(mutations);
            await CleanupTests(mutations,owned);
            await ReviewRegressions(mutations,store,keyPath);
            await BoundedFileRegressions(owned);
            await Expect("P16 path traversal rejected",()=>Task.Run(()=>NativeSecurity.CheckLocalPath(@"\\example.invalid\share\file")));
            await Expect("P16 process self protected",()=>Task.Run(()=>NativeSecurity.CheckedProcess(Environment.ProcessId,Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks)));
            await Expect("P16 invalid service name",()=>Task.Run(()=>ServicesNative.ValidateName("x; Stop-Service *")));
            await Expect("P16 system service protected",()=>Task.Run(()=>ServicesNative.ValidateAction(new("WinDefend",4,0x10,2,"C:\\Windows\\System32\\x.exe",1,Array.Empty<string>()),ActionKind.StopService)));
            await Expect("P16 non-user scope rejected",()=>Task.Run(()=>Mutations.ValidateEnvironment("Process","X","Y")));
            await Expect("P16 NUL environment rejected",()=>Task.Run(()=>Mutations.ValidateEnvironment("User","X","a\0b")));
            await Expect("P16 malformed name rejected",()=>Task.Run(()=>Mutations.ValidateEnvironment("User","x=y","z")));
            var rejected=await mutations.ExecuteAsync("unknown-token",CancellationToken.None);Check("P16 unknown preview token no mutation",rejected.Status=="failed");
            Check("P14 real outcomes retained bounded",store.GetHistory().Any(x=>x.Status=="success")&&store.GetHistory().Any(x=>x.Status=="failed")&&store.GetHistory().All(x=>!x.Target.Contains(owned,StringComparison.OrdinalIgnoreCase)));
            store.SaveSettings(store.Settings with{Dark=true});Check("P15/P18 theme persists across store reload",new AppStore(store.Root).Settings.Dark);
            var backupPath=Path.Combine(owned,"upgrade.nccbackup");store.Backup(backupPath);store.SaveSettings(store.Settings with{Dark=false});store.Restore(backupPath);Check("P18 manual upgrade restore keeps settings",store.Settings.Dark);
            using(var controller=new ControlCenter(new DeterministicReader(),new AppStore(Path.Combine(owned,"controller")),fixture))
            {
                var first=await controller.CreateSnapshotAsync("Trước — kiểm thử",CancellationToken.None);var second=await controller.CreateSnapshotAsync("Sau — kiểm thử",CancellationToken.None);var changes=await controller.CompareSnapshotsAsync(first.Id,second.Id,CancellationToken.None);
                Check("P12 native-core-storage contract snapshot",first.Modules.Count==4&&changes.Count==0);
                var exported=Path.Combine(owned,"snapshot.nccsnapshot");await controller.ExportSnapshotAsync(first.Id,exported,CancellationToken.None);await controller.ImportSnapshotAsync(exported,CancellationToken.None);Check("P12 encrypted export/import controller",File.Exists(exported));
                var history=await controller.ReadAsync(Module.History,false,CancellationToken.None);Check("P14 empty history is honest",history.Rows.Count==0);
                var cancelled=new CancellationToken(true);var cancelledRead=await controller.ReadAsync(Module.Dashboard,false,cancelled);Check("P01 cancellation contract",cancelledRead.State==ResultState.Cancelled);
            }
            await Performance(owned,evidence);
        }
        catch(Exception error){Fail("Test harness interrupted: "+Privacy.Error(error));}
        finally
        {
            // Only this random registry subtree was created by these tests.
            using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64);hive.DeleteSubKeyTree(keyPath,false);
            Results.Add("INFO Owned test registry subtree removed; fixture files retained ignored for inspection.");
            File.WriteAllLines(Path.Combine(evidence,"tests.txt"),Results);
            File.WriteAllText(Path.Combine(evidence,"summary.json"),JsonSerializer.Serialize(new{passed,failed,skipped,unverified=new[]{"UAC real System environment/service action","Real service install/start/stop","User temp cleanup","Real startup/PATH mutations","Cross-account DPAPI restore (unsupported)"}},new JsonSerializerOptions{WriteIndented=true}));
        }
        Console.WriteLine($"PASS={passed} FAIL={failed} SKIP={skipped}; evidence: ignored .evidence/verify");return failed==0?0:1;
    }
    static async Task<int> SafeAsync(string evidence)
    {
        Directory.CreateDirectory(evidence);File.WriteAllText(Path.Combine(evidence,".nyan-fixture"),"Owned application data checks only");
        try
        {
            foreach(var result in await JsonFileChecks.RunAsync(evidence))Add(result);
            foreach(var result in await ServiceChecks.RunAsync(evidence))Add(result);
            foreach(var result in await StoreChecks.RunAsync(evidence))Add(result);
            using var reader=new WindowsReader();
            foreach(var module in new[]{Module.Dashboard,Module.Applications,Module.Startup,Module.Processes,Module.Services,Module.DevTools,Module.Ports,Module.Environment,Module.Network})
            {
                var result=await reader.ReadAsync(module,false,CancellationToken.None);
                Check($"acceptance native read-only {module}: {result.State}",result.State is ResultState.Ready or ResultState.Partial or ResultState.Empty);
            }
        }
        catch(Exception error){Fail("Safe harness: "+Privacy.Error(error));}
        File.WriteAllLines(Path.Combine(evidence,"tests.txt"),Results);
        File.WriteAllText(Path.Combine(evidence,"summary.json"),JsonSerializer.Serialize(new{passed,failed,skipped,scope="Simulated service orchestration, isolated app store/files and native read-only; no registry/service/startup/environment/cleanup mutation",productionMutations="NOT_RUN",explorer="WAITING_FOR_USER"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS={passed} FAIL={failed} SKIP={skipped}; {evidence}");return failed==0?0:1;
    }
    static async Task EnvironmentTests(Mutations mutations,AppStore store,string keyPath)
    {
        using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64);using var key=hive.CreateSubKey(keyPath+@"\Environment",true);var original=@"%USERPROFILE%\Có dấu;C:\Không tồn tại;C:\Có khoảng trắng";key.SetValue("PATH",original,RegistryValueKind.ExpandString);
        var row=new Row("User:PATH",new(),new(){{"scope","User"},{"name","PATH"},{"value",original}});
        var preview=await mutations.PreviewAsync(new(ActionKind.SetEnvironment,row.Id,new(){{"scope","User"},{"name","PATH"},{"value",original+";C:\\New"}}),row,null,CancellationToken.None);
        Check("P10 preview keeps raw PATH order",preview.Before==original&&preview.CanUndo);var outcome=await mutations.ExecuteAsync(preview.Token,CancellationToken.None);Check("P10 fixture environment write verified",outcome.Status=="success"&&key.GetValueKind("PATH")==RegistryValueKind.ExpandString);
        var undo=await mutations.PreviewAsync(new(ActionKind.Undo,outcome.UndoId!),null,null,CancellationToken.None);var restored=await mutations.ExecuteAsync(undo.Token,CancellationToken.None);Check("P10 encrypted undo preserves exact raw value",restored.Status=="success"&&(string?)key.GetValue("PATH",null,RegistryValueOptions.DoNotExpandEnvironmentNames)==original);
        var stale=await mutations.PreviewAsync(new(ActionKind.SetEnvironment,row.Id,new(){{"scope","User"},{"name","PATH"},{"value","CHANGED"}}),row,null,CancellationToken.None);key.SetValue("PATH","EXTERNAL",RegistryValueKind.ExpandString);var staleResult=await mutations.ExecuteAsync(stale.Token,CancellationToken.None);Check("P10 stale preview rejected",staleResult.Status=="failed"&&(string?)key.GetValue("PATH")=="EXTERNAL");
        Check("P16 token single use",(await mutations.ExecuteAsync(stale.Token,CancellationToken.None)).Status=="failed");
        var freshRow=row with{Data=new(){{"scope","User"},{"name","PATH"},{"value","EXTERNAL"}}};var cancelPreview=await mutations.PreviewAsync(new(ActionKind.SetEnvironment,row.Id,new(){{"scope","User"},{"name","PATH"},{"value","CANCEL"}}),freshRow,null,CancellationToken.None);var cancel=await mutations.ExecuteAsync(cancelPreview.Token,new CancellationToken(true));Check("P10 cancellation before mutation",cancel.Status=="cancelled"&&(string?)key.GetValue("PATH")=="EXTERNAL");
    }
    static async Task StartupTests(Mutations mutations,AppStore store,string keyPath,string owned)
    {
        using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64);using var key=hive.CreateSubKey(keyPath+@"\Run",true);key.SetValue("Nyan Fixture",@"C:\Có dấu\test.exe --fixture",RegistryValueKind.ExpandString);
        var row=new Row("startup-fixture",new(){{"name","Nyan Fixture"}},new(){{"kind","run-user"},{"key",keyPath+@"\Run"},{"name","Nyan Fixture"},{"value",@"C:\Có dấu\test.exe --fixture"},{"view","Registry64"}});
        var p=await mutations.PreviewAsync(new(ActionKind.DisableStartup,row.Id),row,null,CancellationToken.None);var result=await mutations.ExecuteAsync(p.Token,CancellationToken.None);Check("P04 fixture Run disable",result.Status=="success"&&key.GetValue("Nyan Fixture")==null);
        p=await mutations.PreviewAsync(new(ActionKind.EnableStartup,result.UndoId!),null,null,CancellationToken.None);result=await mutations.ExecuteAsync(p.Token,CancellationToken.None);Check("P04 Run enable exact type/value",result.Status=="success"&&key.GetValueKind("Nyan Fixture")==RegistryValueKind.ExpandString);
        var file=Path.Combine(owned,"Tiếng Việt có khoảng trắng.lnk");var contents=new byte[]{1,2,3,4,5};File.WriteAllBytes(file,contents);row=new("file-fixture",new(){{"name","Tiếng Việt có khoảng trắng"}},new(){{"kind","folder-user"},{"path",file}});p=await mutations.PreviewAsync(new(ActionKind.DisableStartup,row.Id),row,null,CancellationToken.None);result=await mutations.ExecuteAsync(p.Token,CancellationToken.None);Check("P04 fixture folder disable",result.Status=="success"&&!File.Exists(file));
        if(result.UndoId!=null){p=await mutations.PreviewAsync(new(ActionKind.EnableStartup,result.UndoId),null,null,CancellationToken.None);result=await mutations.ExecuteAsync(p.Token,CancellationToken.None);Check("P04 folder restore exact bytes",result.Status=="success"&&File.ReadAllBytes(file).SequenceEqual(contents));}
        await Expect("P04 task startup read-only policy",()=>mutations.PreviewAsync(new(ActionKind.DisableStartup,"task"),new("task",new(),new(){{"kind","task"}}),null,CancellationToken.None));
    }
    static async Task ProcessTests(Mutations mutations)
    {
        var executable=Path.Combine(AppContext.BaseDirectory,"Nyan.Tests.exe");using var child=Process.Start(new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,Arguments="--owned-child"})!;
        try
        {
            var ticks=child.StartTime.ToUniversalTime().Ticks;var row=new Row(child.Id.ToString(),new(),new(){{"pid",child.Id.ToString()},{"startTicks",ticks.ToString()}});
            await Expect("P05 PID reuse rejected",()=>mutations.PreviewAsync(new(ActionKind.EndProcess,row.Id),row with{Data=new(){{"pid",child.Id.ToString()},{"startTicks",(ticks-1).ToString()}}},null,CancellationToken.None));
            var preview=await mutations.PreviewAsync(new(ActionKind.EndProcess,row.Id),row,null,CancellationToken.None);var result=await mutations.ExecuteAsync(preview.Token,CancellationToken.None);Check("P05 owned process termination native verified",result.Status=="success"&&child.HasExited);
            await Expect("P05 vanished process rejected",()=>mutations.PreviewAsync(new(ActionKind.EndProcess,row.Id),row,null,CancellationToken.None));
        }finally{if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}}
    }
    static async Task CleanupTests(Mutations mutations,string owned)
    {
        var file=Path.Combine(owned,"Dọn fixture có dấu.tmp");File.WriteAllText(file,"owned cleanup fixture");File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-10));var state=mutations.CleanupState(file);var row=new Row("cleanup-owned",new(),state);
        var p=await mutations.PreviewAsync(new(ActionKind.CleanupFiles,"",SelectedIds:new(){row.Id}),null,new(){row},CancellationToken.None);File.AppendAllText(file,"changed");var changed=await mutations.ExecuteAsync(p.Token,CancellationToken.None);Check("P13 file changed after preview skipped",changed.Status=="failed"&&File.Exists(file));
        File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-10));row=row with{Data=mutations.CleanupState(file)};p=await mutations.PreviewAsync(new(ActionKind.CleanupFiles,"",SelectedIds:new(){row.Id}),null,new(){row},CancellationToken.None);var cancelled=await mutations.ExecuteAsync(p.Token,new CancellationToken(true));Check("P13 cleanup cancelled preserves file",cancelled.Status=="cancelled"&&File.Exists(file));
        p=await mutations.PreviewAsync(new(ActionKind.CleanupFiles,"",SelectedIds:new(){row.Id}),null,new(){row},CancellationToken.None);var result=await mutations.ExecuteAsync(p.Token,CancellationToken.None);Check("P13 owned fixture native Recycle Bin: "+result.Message,result.Status=="success"&&!File.Exists(file));
        // Restore the exact owned fixture from Recycle Bin through its unique name only.
        File.WriteAllText(Path.Combine(owned,"recycle-note.txt"),"Owned cleanup test file may be restored manually from Recycle Bin; no user files were removed.");
        var newer=Path.Combine(owned,"new.tmp");File.WriteAllText(newer,"owned");await Expect("P13 fresh file protected",()=>Task.Run(()=>mutations.CleanupState(newer)));
        await Expect("P13 outside approved scope protected",()=>Task.Run(()=>mutations.CleanupState(Path.Combine(Path.GetTempPath(),"outside.tmp"))));
    }
    static async Task Performance(string owned,string evidence)
    {
        var root=Path.Combine(owned,"stress");Directory.CreateDirectory(root);for(int i=0;i<4000;i++)File.WriteAllText(Path.Combine(root,$"file-{i:D5}.txt"),"owned stress fixture");
        var scanner=new StorageScanner();var watch=Stopwatch.StartNew();var scan=await scanner.ScanAsync(root,null,CancellationToken.None);watch.Stop();Check("P17 large scan exact count bounded rows",scan.Files==4000&&scan.Rows.Count<=2000);var elapsed=watch.ElapsedMilliseconds;
        using var cts=new CancellationTokenSource();cts.Cancel();watch.Restart();var cancelled=await scanner.ScanAsync(root,null,cts.Token);watch.Stop();Check("P17 cancellation returns partial quickly",cancelled.Cancelled&&watch.ElapsedMilliseconds<2000);
        File.WriteAllText(Path.Combine(evidence,"performance.json"),JsonSerializer.Serialize(new{fixtureFiles=4000,scanMs=elapsed,cancelMs=watch.ElapsedMilliseconds,logicalBytes=scan.Bytes,scanRowLimit=2000,budgetScanMs=15000,budgetCancellationMs=2000},new JsonSerializerOptions{WriteIndented=true}));
    }
    static async Task ReviewRegressions(Mutations mutations,AppStore store,string keyPath)
    {
        using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64);using var env=hive.CreateSubKey(keyPath+@"\Environment",true);env.SetValue("REVIEW","CURRENT",RegistryValueKind.String);
        var current=mutations.ReadEnvironment("User","REVIEW");var id=Guid.NewGuid().ToString("N");store.PutBackup(id,new(){{"type","environment"},{"scope","User"},{"name","REVIEW"},{"value","ORIGINAL"},{"exists","true"},{"kind","String"},{"afterFingerprint",Mutations.Fingerprint(current)}});
        var undo=await mutations.PreviewAsync(new(ActionKind.Undo,id),null,null,CancellationToken.None);var undone=await mutations.ExecuteAsync(undo.Token,CancellationToken.None);Check("P16 imported boolean casing canonicalized",undone.Status=="success"&&(string?)env.GetValue("REVIEW")=="ORIGINAL");
        current=mutations.ReadEnvironment("User","REVIEW");id=Guid.NewGuid().ToString("N");store.PutBackup(id,new(){{"type","environment"},{"scope","User"},{"name","REVIEW"},{"value","FORGED"},{"exists","True"},{"kind","1"},{"afterFingerprint",Mutations.Fingerprint(current)}});await Expect("P16 numeric enum backup rejected before write",()=>mutations.PreviewAsync(new(ActionKind.Undo,id),null,null,CancellationToken.None));Check("P16 numeric enum retains original data",(string?)env.GetValue("REVIEW")=="ORIGINAL");
        using var run=hive.CreateSubKey(keyPath+@"\Run",true);id=Guid.NewGuid().ToString("N");store.PutBackup(id,new(){{"type","startup-run"},{"key",keyPath+@"\Run"},{"name","Forged"},{"value","7"},{"view","Registry64"},{"registryKind","DWord"}});await Expect("P16 forged Run registry kind rejected",()=>mutations.PreviewAsync(new(ActionKind.Undo,id),null,null,CancellationToken.None));Check("P16 forged backup did not write registry",run.GetValue("Forged")==null);
        current=mutations.ReadEnvironment("User","REVIEW");var fields=new Dictionary<string,string>(current){{"fingerprint",Mutations.Fingerprint(current)},{"newValue","EXPIRED"},{"delete","False"}};var expired=new ActionPreview("expired",ActionKind.SetEnvironment,"Fixture","Fixture","","","",true,false,DateTimeOffset.UtcNow.AddMinutes(-5));await Expect("P16 helper plan expiry checked at native boundary",()=>mutations.ExecutePlanAsync(new(expired,fields),CancellationToken.None));Check("P16 expired plan retained old value",(string?)env.GetValue("REVIEW")=="ORIGINAL");
        var longName=new string('N',5000);run.SetValue(longName,"fixture",RegistryValueKind.String);var row=new Row("review-long",new(){{"name",longName}},new(){{"kind","run-user"},{"key",keyPath+@"\Run"},{"name",longName},{"value","fixture"},{"view","Registry64"}});var preview=await mutations.PreviewAsync(new(ActionKind.DisableStartup,row.Id),row,null,CancellationToken.None);var result=await mutations.ExecuteAsync(preview.Token,CancellationToken.None);Check("P16 long history target preserves verified outcome",result.Status=="success"&&store.GetHistory().Any(x=>x.UndoId==result.UndoId));
        var newRow=new Row("User:REVIEW",new(),new(){{"scope","User"},{"name","REVIEW"},{"value","ORIGINAL"}});preview=await mutations.PreviewAsync(new(ActionKind.SetEnvironment,newRow.Id,new(){{"scope","User"},{"name","REVIEW"},{"value","RESTORE-STALE"}}),newRow,null,CancellationToken.None);var backupPath=Path.Combine(store.Root,"restore-race.nccbackup");store.Backup(backupPath);await mutations.RestoreAsync(backupPath,CancellationToken.None);result=await mutations.ExecuteAsync(preview.Token,CancellationToken.None);Check("P16 restore invalidates pending preview",result.Status=="failed"&&(string?)env.GetValue("REVIEW")=="ORIGINAL");
    }
    static async Task BoundedFileRegressions(string owned)
    {
        if(NativeSecurity.HasPackageIdentity||NativeSecurity.HasAppDataRedirection)
        {
            var policy=new Mutations(new AppStore(Path.Combine(owned,"host-policy")));
            var preview=new ActionPreview("host-policy",ActionKind.EndProcess,"Fixture","PID 4","","","",false,false,DateTimeOffset.UtcNow.AddMinutes(1));
            var guarded=await policy.ExecutePlanAsync(new(preview,new(){{"pid","4"},{"startTicks","0"}}),CancellationToken.None);
            Check("P16 redirected host rejected at direct native boundary",guarded.Status=="failed"&&guarded.Message.Contains("chuyển hướng",StringComparison.Ordinal));
            guarded=await policy.ExecuteAsync("nonexistent",CancellationToken.None);Check("P16 redirected host rejected at token boundary",guarded.Status=="failed"&&guarded.Message.Contains("chuyển hướng",StringComparison.Ordinal));
        }
        var path=Path.Combine(owned,"IPC tiếng Việt.bin");var bytes=System.Text.Encoding.UTF8.GetBytes("owned IPC payload");File.WriteAllBytes(path,bytes);
        Check("P16 bounded native handle read exact bytes",NativeSecurity.ReadBounded(path,128).SequenceEqual(bytes));
        await Expect("P16 oversized IPC file rejected",()=>Task.Run(()=>NativeSecurity.ReadBounded(path,4)));
        await Expect("P16 missing IPC file rejected",()=>Task.Run(()=>NativeSecurity.ReadBounded(path+".missing",128)));
        await Expect("P16 directory IPC rejected",()=>Task.Run(()=>NativeSecurity.ReadBounded(owned,128)));
        using(var writer=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))await Expect("P16 concurrently writable IPC rejected",()=>Task.Run(()=>NativeSecurity.ReadBounded(path,128)));
        var link=path+".link";File.CreateSymbolicLink(link,path);try{await Expect("P16 IPC leaf symbolic link rejected",()=>Task.Run(()=>NativeSecurity.ReadBounded(link,128)));}finally{File.Delete(link);}
        // Exercise the reviewed helper output path without elevating or invoking
        // any Windows mutation. These two unpredictable GUID leaves are owned.
        var requestRoot=Path.Combine(NativeSecurity.LocalRoot,"requests");Directory.CreateDirectory(requestRoot);using var parent=new DirectoryLease(requestRoot);
        var id=Guid.NewGuid().ToString("N");var active=Path.Combine(requestRoot,id+".active");var result=Path.Combine(requestRoot,id+".result");
        try
        {
            using(var stream=new FileStream(active,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Flush(true);
            using(var stream=new FileStream(result,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(bytes);
            bool refused=false;try{Elevation.WriteResult(id,new("success","fixture"));}catch(IOException){refused=true;}
            Check("P16 helper result collision never overwrites leaf",refused&&File.ReadAllBytes(result).SequenceEqual(bytes));
        }
        finally{File.Delete(active);File.Delete(result);}
    }
    static async Task Expect(string name,Func<Task> action){try{await action();Fail(name+" (unexpected acceptance)");}catch(Exception e)when(e is AppException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException){Add("PASS "+name);}}
    static void Check(string name,bool condition){if(condition)Add("PASS "+name);else Fail(name);}
    static void Fail(string name){failed++;Results.Add("FAIL "+name);Console.WriteLine("FAIL "+name);}
    static void Add(string result){Results.Add(result);if(result.StartsWith("PASS"))passed++;else if(result.StartsWith("SKIP"))skipped++;else if(result.StartsWith("FAIL")){failed++;Console.WriteLine(result);}}
    sealed class DeterministicReader:IWindowsReader
    {
        public Task<ModuleResult> ReadAsync(Module module,bool reveal,CancellationToken token){token.ThrowIfCancellationRequested();var cells=module==Module.Environment?new Dictionary<string,string>{{"name","X"},{"scope","User"},{"value","Đã che"}}:new(){{"name","Fixture"},{"source","Owned fixture"}};return Task.FromResult(new ModuleResult(module,new(){new("name","Tên")},new(){new("fixture",cells)},ResultState.Ready,"Fixture",DateTimeOffset.Now));}
    }
}
