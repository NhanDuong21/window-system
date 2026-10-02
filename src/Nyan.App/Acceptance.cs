using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Nyan.Core;

namespace Nyan.App;

// Human-started acceptance only. Production Mutations has no fixture override here.
internal static class Acceptance
{
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    internal static string Validate(string root)
    {
        root=Path.GetFullPath(root);NativeSecurity.CheckLocalPath(root);
        var manifest=JsonSerializer.Deserialize<Dictionary<string,string>>(NativeSecurity.ReadBounded(Path.Combine(root,"ownership.json"),64*1024))!;
        if(manifest.GetValueOrDefault("product")!="Nyan acceptance"||!Guid.TryParseExact(manifest.GetValueOrDefault("id"),"N",out _)||manifest.GetValueOrDefault("root")!=root)
            throw new AppException("ownership","Thư mục nghiệm thu không có ownership manifest hợp lệ.");
        return root;
    }
    static string Id(string root)=>JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"ownership.json")))!["id"];
    internal static void Context(string root,string mode)
    {
        var leaf=mode+"-context.json";if(File.Exists(Path.Combine(root,leaf)))leaf=mode+"-context-"+Guid.NewGuid().ToString("N")+".json";
        Write(root,leaf,new{at=DateTimeOffset.Now,mode,pid=Environment.ProcessId,elevated=NativeSecurity.IsAdministrator,packaged=NativeSecurity.HasPackageIdentity,redirectedAppData=NativeSecurity.HasAppDataRedirection,sidHash=Privacy.Hash(System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value),source=typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute),false).Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion,exeSha256=Hash(Environment.ProcessPath!),explorer="Human declaration required; process context alone is not Explorer proof"});
    }
    internal static void Persistence(string root)
    {
        // This test root never opens the normal live user state.
        var store=new AppStore(Path.Combine(root,"persistence-store"));
        var firstLight=!store.Settings.Dark;store.SaveSettings(store.Settings with{Dark=true});
        var reloaded=new AppStore(store.Root);var reload=reloaded.Settings.Dark;
        var backup=Path.Combine(root,"persistence.nccbackup");reloaded.Backup(backup);reloaded.SaveSettings(reloaded.Settings with{Dark=false});reloaded.Restore(backup);
        var restored=new AppStore(store.Root).Settings.Dark;
        Write(root,"persistence.json",new{at=DateTimeOffset.Now,firstLight,reload,restored,result=firstLight&&reload&&restored?"PASS":"FAIL",scope="New isolated app store, same Windows user; no live-state restore or cross-machine guarantee"});
        if(!firstLight||!reload||!restored)throw new AppException("persistence","Kiểm tra persistence/backup vùng riêng thất bại.");
    }
    internal static async Task ResourceAsync(string root,string kind)
    {
        Validate(root);if(File.Exists(Path.Combine(root,kind+"-result.json")))throw new AppException("evidence","Bài thử đã có kết quả; mở phiên GUID mới, không ghi đè evidence.");var id=Id(root);var name="NYAN_ACCEPTANCE_"+id;
        var records=new List<object>();var leftovers=new List<string>();
        if(NativeSecurity.IsAdministrator||NativeSecurity.HasPackageIdentity||NativeSecurity.HasAppDataRedirection)
        {
            Write(root,kind+"-result.json",new{status="WAITING_FOR_USER",reason="Cần app user thường không có package/AppData redirection. Giữ guard; mở launcher từ Explorer."});
            MessageBox.Show("Ngữ cảnh chưa phù hợp. Hãy mở Nghiem-Thu-Nyan.cmd từ File Explorer. Không thay đổi Windows.","Nyan nghiệm thu");return;
        }
        using var reader=new WindowsReader();var store=new AppStore(Path.Combine(root,kind+"-store"));var mutation=new Mutations(store,reader:reader);
        async Task<ActionOutcome> Execute(ActionRequest request,Row? row=null,List<Row>? selected=null,CancellationToken token=default)
        {
            if(request.Kind is ActionKind.StartService or ActionKind.StopService or ActionKind.RestartService)
            {
                var ownership=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(NativeSecurity.ReadBounded(Path.Combine(root,"service-ownership.json"),64*1024))!;
                var native=ServicesNative.Read(name);var folder=ownership["folder"].GetString()!;
                if(native.Binary!=ownership["binary"].GetString()||native.Type!=0x10||native.StartType!=3||native.Dependents.Length!=0||Hash(Path.Combine(folder,"NyanControlCenter.exe"))!=ownership["exeSha256"].GetString())throw new AppException("identity","Service đã đổi ownership; không gửi lệnh.");
            }
            var preview=await mutation.PreviewAsync(request,row,selected,CancellationToken.None);
            var outcome=await mutation.ExecuteAsync(preview.Token,token);records.Add(new{action=request.Kind,preview,outcome});Write(root,kind+"-progress.json",records);return outcome;
        }
        void Require(ActionOutcome outcome){if(outcome.Status!="success")throw new AppException("acceptance",outcome.Message);}
        bool Confirm(string details)=>MessageBox.Show(details+"\n\nCho phép đúng chuỗi thử và dọn đối tượng sở hữu này?","Nyan — xác nhận riêng "+kind,MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
        void Own(object detail)=>Write(root,kind+"-ownership.json",new{product="Nyan acceptance",id,at=DateTimeOffset.Now,detail});
        string status="WAITING_FOR_USER";string? error=null;
        try
        {
            if(kind is "environment" or "system-environment")
            {
                var scope=kind=="environment"?"User":"Machine";var before=mutation.ReadEnvironment(scope,name);
                if(before["exists"]!="False")throw new AppException("collision","Biến thử đã tồn tại; không sửa.");
                Own(new{scope,name,before="absent",values=new[]{"Nyan A có dấu","Nyan B có dấu"}});
                if(!Confirm($"Biến {scope}: {name}\nTạo giá trị Nyan A có dấu → đổi Nyan B có dấu → undo về A → xóa → đối chiếu native. Không dùng biến đang có. System sẽ hỏi UAC từng thao tác; bạn tự xác nhận hoặc huỷ."))return;
                bool OwnedValue(){var current=mutation.ReadEnvironment(scope,name);return current["exists"]=="True"&&current["kind"]=="String"&&current["value"] is "Nyan A có dấu" or "Nyan B có dấu";}
                bool created=false;
                async Task<ActionOutcome> Set(string value)
                {
                    var current=mutation.ReadEnvironment(scope,name);if(!created&&current["exists"]!="False"||created&&!OwnedValue())throw new AppException("identity","Biến đã đổi ownership; không ghi.");
                    return await Execute(new(ActionKind.SetEnvironment,name,new(){{"scope",scope},{"name",name},{"value",value}}),new Row(name,new(),current));
                }
                try
                {
                    if(mutation.ReadEnvironment(scope,name)["exists"]!="False")throw new AppException("collision","Biến xuất hiện sau xác nhận; không tạo.");
                    Require(await Set("Nyan A có dấu"));created=true;if(!OwnedValue())throw new AppException("identity","Giá trị native không khớp.");
                    var changed=await Set("Nyan B có dấu");Require(changed);if(mutation.ReadEnvironment(scope,name)["value"]!="Nyan B có dấu")throw new AppException("verify","Update không khớp native.");
                    // Machine undo currently uses the helper's normal live store; do not import isolated state into it.
                    if(scope=="User"){Require(await Execute(new(ActionKind.Undo,changed.UndoId!)));if(mutation.ReadEnvironment(scope,name)["value"]!="Nyan A có dấu")throw new AppException("verify","Undo không khớp native.");}
                    status="PASS";
                }
                finally
                {
                    var current=mutation.ReadEnvironment(scope,name);
                    if(current["exists"]=="True")
                    {
                        if(created&&OwnedValue()) {var removed=await Execute(new(ActionKind.DeleteEnvironment,name,new(){{"scope",scope},{"name",name}}),new Row(name,new(),current));if(removed.Status!="success")leftovers.Add(scope+": "+name+" — "+removed.Message);}
                        else leftovers.Add(scope+": "+name+" đã thay đổi danh tính; không xóa.");
                    }
                    if(mutation.ReadEnvironment(scope,name)["exists"]!="False"&&leftovers.Count==0)leftovers.Add(scope+": "+name);
                }
            }
            else if(kind=="startup")
            {
                const string keyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
                using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64);
                using var key=hive.OpenSubKey(keyPath,true)??throw new AppException("startup","Run User chưa có hoặc không writable; không tự tạo key.");
                var command="\""+Environment.ProcessPath+"\" --acceptance-startup-exit";
                if(key.GetValue(name)!=null)throw new AppException("collision","Mục thử đã tồn tại.");
                Own(new{hive="HKCU",key=keyPath,name,command,kind="String",exeSha256=Hash(Environment.ProcessPath!)});
                if(!Confirm($"HKCU\\{keyPath}\\{name}\nGiá trị String: {command}\nTarget thoát ngay, không mở UI hoặc network. Tạo Run User → production disable → enable từ backup → xóa đúng giá trị sau test."))return;
                bool IsOwned()=>key.GetValueKind(name)==RegistryValueKind.String&&(string?)key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames)==command;
                bool created=false;
                try
                {
                    // Check absence again immediately before writing.
                    if(key.GetValue(name)!=null)throw new AppException("collision","Run User đã xuất hiện sau xác nhận.");key.SetValue(name,command,RegistryValueKind.String);created=true;
                    var row=new Row(name,new(){{"name",name}},new(){{"kind","run-user"},{"key",keyPath},{"name",name},{"value",command},{"view","Registry64"}});
                    var disabled=await Execute(new(ActionKind.DisableStartup,name),row);Require(disabled);if(key.GetValue(name)!=null)throw new AppException("verify","Disable chưa gỡ mục native.");
                    Require(await Execute(new(ActionKind.EnableStartup,disabled.UndoId!)));if(!IsOwned())throw new AppException("identity","Enable không khớp type/value.");status="PASS";
                }
                finally{if(created&&key.GetValue(name)!=null){if(IsOwned()){key.DeleteValue(name);if(key.GetValue(name)!=null)leftovers.Add("HKCU Run: "+name);}else leftovers.Add("HKCU Run: "+name+" đã đổi; không xóa.");}}
            }
            else if(kind=="process-port")
            {
                var childRoot=Path.Combine(root,"child-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(childRoot);File.Copy(Path.Combine(root,"ownership.json"),Path.Combine(childRoot,"parent-ownership.json"));
                var childExe=Path.Combine(AppContext.BaseDirectory,name+".exe");if(File.Exists(childExe)||Directory.Exists(childExe))throw new AppException("collision","Child image GUID đã tồn tại.");
                Own(new{childRoot,exe=childExe,exeSha256=Hash(Environment.ProcessPath!),listener="127.0.0.1: ephemeral TCP",timeoutMinutes=10});
                if(!Confirm($"Tạo bản sao apphost sở hữu: {childExe}\nProcess tên {name}; listener TCP 127.0.0.1 cổng ephemeral, không network ngoài máy. Đọc production Ports → preview → kết thúc đúng PID+creation time. Process tự thoát tối đa 10 phút; cleanup chỉ PID và image cùng identity."))return;
                File.Copy(Environment.ProcessPath!,childExe,false);string imageId;using(var image=new FileLease(childExe))imageId=image.Identity;
                Own(new{childRoot,exe=childExe,imageId,exeSha256=Hash(Environment.ProcessPath!),status="IMAGE_CREATED_CHILD_NOT_STARTED"});
                using var child=Process.Start(new ProcessStartInfo(childExe){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"--acceptance-child",childRoot}})!;
                var ticks=child.StartTime.ToUniversalTime().Ticks;Own(new{childRoot,pid=child.Id,startTicks=ticks,exe=childExe,imageId,exeSha256=Hash(Environment.ProcessPath!)});
                try
                {
                    for(int i=0;i<100&&!File.Exists(Path.Combine(childRoot,"ready.json"))&&!child.HasExited;i++)await Task.Delay(100);
                    if(!File.Exists(Path.Combine(childRoot,"ready.json")))throw new AppException("listener","Child chưa báo ready.");
                    var ports=await reader.ReadAsync(Module.Ports,false,CancellationToken.None);
                    var row=ports.Rows.FirstOrDefault(r=>r.Meta("pid")==child.Id.ToString()&&r.Meta("startTicks")==ticks.ToString()&&r.Meta("protocol").StartsWith("TCP",StringComparison.OrdinalIgnoreCase))??throw new AppException("port","Chưa thấy endpoint có identity đúng.");
                    Require(await Execute(new(ActionKind.EndProcess,row.Id),row));if(!child.HasExited)throw new AppException("verify","Child chưa kết thúc.");status="PASS";
                }
                finally
                {
                    if(!child.HasExited){try{using var checkedChild=NativeSecurity.CheckedProcess(child.Id,ticks);if(checkedChild.MainModule?.FileName!=childExe)throw new AppException("identity","Executable đã đổi.");checkedChild.Kill(false);await checkedChild.WaitForExitAsync();}catch(Exception ex){leftovers.Add($"PID {child.Id}, startTicks {ticks}: {Privacy.Error(ex)}");}}
                    if(child.HasExited){try{using var image=new FileLease(childExe,true);if(image.Identity!=imageId)throw new AppException("identity","Image đã đổi; không xóa.");image.Delete();}catch(Exception ex){leftovers.Add(childExe+": "+Privacy.Error(ex));}}
                }
            }
            else if(kind=="cleanup")
            {
                var path=Path.Combine(Path.GetTempPath(),name+"-có-dấu.tmp");var content="Nyan owned cleanup "+id;
                if(File.Exists(path)||Directory.Exists(path))throw new AppException("collision","File thử đã tồn tại.");
                Own(new{path,ageDays=10,content});
                if(!Confirm($"File thử: {path}\nTạo nội dung sở hữu, đặt mtime 10 ngày trước; policy Temp User giữ nguyên. Thử cancel và stale recheck, sau đó production recycle. Giữ receipt đúng item trong Recycle Bin; không purge bin. Nếu policy từ chối thì báo NOT_RUN."))return;
                using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var bytes=System.Text.Encoding.UTF8.GetBytes(content);stream.Write(bytes);File.SetLastWriteTimeUtc(stream.SafeFileHandle,DateTime.UtcNow.AddDays(-10));}
                var data=mutation.CleanupState(path);Own(new{path,identity=data["identity"],length=data["length"],lastWriteTicks=data["lastWriteTicks"]});
                var row=new Row(name,new(){{"name",name}},data);
                var cancelled=await Execute(new(ActionKind.CleanupFiles,name,SelectedIds:[name]),selected:[row],token:new CancellationToken(true));
                if(cancelled.Status!="cancelled"||!File.Exists(path))throw new AppException("verify","Cancel không giữ file thử.");
                var preview=await mutation.PreviewAsync(new(ActionKind.CleanupFiles,name,SelectedIds:[name]),null,[row],CancellationToken.None);
                // Pin the verified name while opening a metadata writer; no SHARE_DELETE allows replacement.
                using(var lease=new FileLease(path)){if(lease.Identity!=data["identity"])throw new AppException("identity","File đã đổi.");}
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read)){using var identity=new FileLease(path);if(identity.Identity!=data["identity"])throw new AppException("identity","File đã đổi trước metadata write.");File.SetLastWriteTimeUtc(stream.SafeFileHandle,DateTime.UtcNow.AddDays(-9));}
                var stale=await mutation.ExecuteAsync(preview.Token,CancellationToken.None);records.Add(new{action="stale recheck",outcome=stale});
                if(stale.Status!="failed"||!File.Exists(path))throw new AppException("verify","Stale recheck không giữ file.");
                row=row with{Data=mutation.CleanupState(path)};var recycled=await Execute(new(ActionKind.CleanupFiles,name,SelectedIds:[name]),selected:[row]);Require(recycled);
                if(File.Exists(path)||recycled.Recycled?.Count!=1)throw new AppException("verify","Thiếu receipt Recycle Bin.");
                using(var item=new FileLease(recycled.Recycled[0].RecyclePath)){if(item.Identity!=data["identity"])throw new AppException("identity","Item recycle không khớp.");}
                Own(new{path,identity=data["identity"],receipt=recycled.Recycled[0]});leftovers.Add("Owned Recycle Bin item retained: "+recycled.Recycled[0].RecyclePath+"; restore thủ công đúng tên GUID, không purge.");status="PASS";
            }
            else if(kind=="service")
            {
                var manifest=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(NativeSecurity.ReadBounded(Path.Combine(root,"service-ownership.json"),64*1024))!;
                var service=ServicesNative.Read(name);var expected=manifest["binary"].GetString()!;
                var serviceExe=Path.Combine(manifest["folder"].GetString()!,"NyanControlCenter.exe");
                if(manifest["name"].GetString()!=name||Hash(serviceExe)!=manifest["exeSha256"].GetString()||service.Binary!=expected||service.Type!=0x10||service.StartType!=3||service.Dependents.Length!=0)throw new AppException("identity","Service không khớp ownership/demand-start/no dependency.");
                Own(new{name,binary=expected,sequence="Start (UAC cancel first), Start, Restart, Stop"});
                if(!Confirm($"Service thử đã đăng ký riêng: {name}\nBinary: {expected}\nHãy HUỶ UAC đầu tiên để thử cancel. Sau đó Start → Restart → Stop, bạn xác nhận UAC và helper từng lần. Nếu một bước lỗi/partial, dừng chuỗi; không tự chữa hoặc unregister."))return;
                Row Row()=>new(name,new(),new(){{"name",name}});
                var cancel=await Execute(new(ActionKind.StartService,name),Row());
                if(cancel.Status!="cancelled"||ServicesNative.Read(name).State!=1)throw new AppException("verify","UAC cancel chưa được xác minh; dừng chuỗi.");
                foreach(var action in new[]{ActionKind.StartService,ActionKind.RestartService,ActionKind.StopService}){Require(await Execute(new(action,name),Row()));var state=ServicesNative.Read(name).State;if(state!=(action==ActionKind.StopService?1u:4u))throw new AppException("verify","Native trạng thái không khớp.");}
                status="PASS";leftovers.Add("Service thử còn đăng ký (Stopped): "+name+"; gỡ bằng service-fixture.ps1 -Action Remove với manifest này, xác nhận riêng.");
            }
            else throw new AppException("mode","Bài nghiệm thu không hỗ trợ.");
        }
        catch(Exception ex){status="PARTIAL";error=Privacy.Error(ex);leftovers.Add("Kiểm tra đúng "+kind+"-ownership.json; không dọn đối tượng không khớp manifest.");}
        finally
        {
            if(kind=="service"&&status!="PASS"&&File.Exists(Path.Combine(root,"service-ownership.json")))
            {
                try{var native=ServicesNative.Read(name);leftovers.Add($"Service thử còn đăng ký: {name}; trạng thái native {native.State} (1=Stopped, 4=Running). Stop qua preview của app rồi Remove bằng script, xác nhận riêng.");}
                catch{leftovers.Add("Service thử: "+name+"; trạng thái chưa xác định, chỉ đọc lại trước dọn.");}
            }
            if(kind=="process-port"&&status!="PASS")
            {
                var image=Path.Combine(AppContext.BaseDirectory,name+".exe");if(File.Exists(image))leftovers.Add("Image thử còn: "+image+"; đối chiếu File ID/hash trong process-port-ownership.json, chỉ dọn sau khi PID thử đã thoát.");
            }
            if(kind=="cleanup"&&status!="PASS")
            {
                var path=Path.Combine(Path.GetTempPath(),name+"-có-dấu.tmp");if(File.Exists(path))leftovers.Add("File thử còn nguyên: "+path+"; đối chiếu File ID trong cleanup-ownership.json trước dọn.");
                foreach(var backup in store.GetBackups().Where(b=>b.GetValueOrDefault("type")=="quarantine"))leftovers.Add("Quarantine sở hữu: "+backup.GetValueOrDefault("quarantine")+"; giữ file và bản ghi DPAPI, không purge.");
            }
            if(leftovers.Any(x=>!x.StartsWith("Owned Recycle Bin item retained")&&!x.StartsWith("Service thử còn đăng ký"))&&status=="PASS")status="PARTIAL";
            Write(root,kind+"-result.json",new{at=DateTimeOffset.Now,status,error,records,leftovers,productionPath="Mutations with fixture=null; guard unchanged"});
        }
        MessageBox.Show(status+"\n"+error+"\n"+string.Join("\n",leftovers)+"\nEvidence: "+root,"Nyan nghiệm thu");
    }
    internal static int Child(string root)
    {
        NativeSecurity.CheckLocalPath(root);var leaf=Path.GetFileName(root);if(!leaf.StartsWith("child-",StringComparison.Ordinal)||!Guid.TryParseExact(leaf[6..],"N",out _)||!File.Exists(Path.Combine(root,"parent-ownership.json")))return 2;
        Validate(Path.GetDirectoryName(root)!);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try{Write(root,"ready.json",new{pid=Environment.ProcessId,startTicks=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,port=((IPEndPoint)listener.LocalEndpoint).Port});Thread.Sleep(TimeSpan.FromMinutes(10));return 0;}finally{listener.Stop();}
    }
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    internal static void Write(string root,string leaf,object value)=>File.WriteAllText(Path.Combine(root,leaf),JsonSerializer.Serialize(value,Json));
}
