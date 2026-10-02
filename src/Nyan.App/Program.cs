using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nyan.Core;

namespace Nyan.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var startup=System.Diagnostics.Stopwatch.StartNew();
        if(!OperatingSystem.IsWindows())return 1;
        if(args.Length==1&&args[0]=="--acceptance-startup-exit")return 0;
        if(args.Length==2&&args[0]=="--acceptance-service-host")return ServiceFixture.Run(args[1]);
        if(args.Length==2&&args[0]=="--acceptance-child")return Acceptance.Child(args[1]);
        if(args.Length==2&&args[0]=="--elevated-action")return Elevated(args[1]);
        bool uiChecks=args.Length==2&&args[0]=="--ui-checks";
        bool capture=args.Length==2&&args[0]=="--capture-native";
        bool acceptance=args.Length==2&&args[0]=="--acceptance-session";
        string? resource=args.Length==2&&new[]{"environment","system-environment","startup","process-port","cleanup","service"}.Any(mode=>args[0]=="--acceptance-"+mode)?args[0][13..]:null;
        if(capture){Directory.CreateDirectory(Path.GetFullPath(args[1]));File.AppendAllText(Path.Combine(Path.GetFullPath(args[1]),"launch.txt"),$"pid={Environment.ProcessId}; elevated={NativeSecurity.IsAdministrator}\n");}
        if(args.Length>0&&!uiChecks&&!capture&&!acceptance&&resource==null){MessageBox.Show("Tham số mở ứng dụng không được hỗ trợ.","Nyan Control Center");return 2;}
        if(!uiChecks&&NativeSecurity.IsAdministrator)
        {
            try{UserLauncher.Start(Environment.ProcessPath!,args);return 0;}
            catch(Exception error){if(capture)File.WriteAllText(Path.Combine(Path.GetFullPath(args[1]),"result.txt"),"FAIL launcher: "+Privacy.Error(error));else MessageBox.Show("Không mở được app bằng quyền user thường. Hãy mở từ File Explorer.\n"+Privacy.Error(error),"Nyan Control Center");return 3;}
        }
        using var single=new Mutex(true,"Local\\NyanControlCenter-"+(uiChecks||capture||acceptance||resource!=null?Privacy.Hash(Path.GetFullPath(args[1])):Privacy.Hash(System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value)),out var firstInstance);
        if(!firstInstance){MessageBox.Show("Nyan Control Center đang mở. Hãy dùng cửa sổ hiện tại.","Nyan Control Center");return 0;}
        var application=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
        application.DispatcherUnhandledException+=(_,e)=>{MessageBox.Show(Privacy.Error(e.Exception),"Nyan Control Center",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;};
        IControlCenter? center=null;
        try
        {
            string? evidence=uiChecks||capture||acceptance||resource!=null?Path.GetFullPath(args[1]):null;
            if(evidence!=null){Directory.CreateDirectory(evidence);NativeSecurity.CheckLocalPath(evidence);}
            if(acceptance||resource!=null){Acceptance.Validate(evidence!);Acceptance.Context(evidence!,resource??"session");}
            var store=evidence!=null?new AppStore(Path.Combine(evidence!,"owned-store")):new AppStore();
            center=new ControlCenter(uiChecks?new FixtureReader():null,store,uiChecks?new MutationFixture(evidence!,@"Software\NyanControlCenter.Tests\ui-owned"):null);
            var window=new MainWindow(center,uiChecks);
            if(acceptance){Acceptance.Write(evidence!,"session-open-"+Guid.NewGuid().ToString("N")+".json",new{at=DateTimeOffset.Now,dark=store.Settings.Dark,pid=Environment.ProcessId});window.Title+=" · NGHIỆM THU — DỮ LIỆU RIÊNG";window.Closed+=(_,_)=>Acceptance.Write(evidence!,"session-closed.json",new{at=DateTimeOffset.Now,dark=store.Settings.Dark});}
            if(resource!=null)window.ContentRendered+=async(_,_)=>{try{await Acceptance.ResourceAsync(evidence!,resource);}catch(Exception error){MessageBox.Show(Privacy.Error(error),"Nyan nghiệm thu");}finally{window.Close();}};
            if(uiChecks||capture)
            {
                window.ContentRendered+=async(_,_)=>
                {
                    var firstPaintMs=startup.ElapsedMilliseconds;
                    string stage=uiChecks?"WPF fixture":"Ownership manifest";
                    try
                    {
                        var notes=new List<string>();
                        if(uiChecks)notes.AddRange(await UiChecks.RunAsync(window,evidence!));
                        else
                        {
                            if(File.Exists(Path.Combine(evidence!,"ownership.json"))){Acceptance.Validate(evidence!);stage="Read context";Acceptance.Context(evidence!,"read");stage="Isolated persistence";Acceptance.Persistence(evidence!);}
                            notes.Add("Windows native x64; elevated="+NativeSecurity.IsAdministrator+"; packaged="+NativeSecurity.HasPackageIdentity+"; redirectedAppData="+NativeSecurity.HasAppDataRedirection);
                            // Measure stable idle separately from screenshot allocation/GC.
                            stage="Native Settings / performance";await window.NavigateForTestAsync(Module.Settings);await Task.Delay(5000);
                            var process=System.Diagnostics.Process.GetCurrentProcess();var cpuStart=process.TotalProcessorTime;var idleWatch=System.Diagnostics.Stopwatch.StartNew();await Task.Delay(10000);process.Refresh();var idleCpu=(process.TotalProcessorTime-cpuStart).TotalMilliseconds/idleWatch.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100;
                            File.WriteAllText(Path.Combine(evidence!,"release-performance.json"),System.Text.Json.JsonSerializer.Serialize(new{firstPaintMs,idleCpuPercent=idleCpu,workingSetMiB=process.WorkingSet64/1048576.0,privateMiB=process.PrivateMemorySize64/1048576.0,dpiScale=VisualTreeHelper.GetDpi(window).DpiScaleX,packaged=NativeSecurity.HasPackageIdentity,condition="Self-contained x64; first ContentRendered event; Settings idle settles for 5 seconds then CPU sampled over 10 seconds, before allocating capture images; CPU normalized by logical processor count",budgetFirstPaintMs=2500,budgetIdleCpuPercent=1.0,budgetWorkingSetMiB=350},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                            foreach(var module in new[]{Module.Dashboard,Module.Applications,Module.Startup,Module.Processes,Module.Storage,Module.Services,Module.DevTools,Module.Ports,Module.Environment,Module.Network,Module.Snapshots,Module.Cleanup,Module.History,Module.Settings})
                            {
                                stage="Native "+module;
                                // Read-only captures: no system mutation, no cleanup execution.
                                await window.NavigateForTestAsync(module);await Task.Delay(150);
                                SaveImage(window,Path.Combine(evidence!,module+"-light.png"));var shown=window.ResultForTest;
                                notes.Add("CAPTURE "+module+" state="+shown?.State+" rows="+shown?.Rows.Count);
                                if(shown==null||shown.State is ResultState.Error or ResultState.Cancelled||module is Module.Ports or Module.Services&&shown.State is ResultState.Denied or ResultState.Empty)notes.Add("FAIL native read "+module+" state="+shown?.State);
                            }
                            await window.ToggleThemeForTestAsync();await window.NavigateForTestAsync(Module.Dashboard);await Task.Delay(200);SaveImage(window,Path.Combine(evidence!,"Dashboard-dark.png"));
                            await window.ToggleThemeForTestAsync();
                        }
                        bool failed=notes.Any(n=>n.StartsWith("FAIL",StringComparison.Ordinal));File.WriteAllLines(Path.Combine(evidence!,"result.txt"),notes.Prepend(failed?"FAIL":"PASS"));if(failed)Environment.ExitCode=1;
                    }
                    catch(Exception error){File.WriteAllText(Path.Combine(evidence!,"result.txt"),"FAIL\nStage: "+stage+"\n"+Privacy.Error(error));Environment.ExitCode=1;}
                    finally{window.Close();}
                };
            }
            application.Run(window);return Environment.ExitCode;
        }
        catch(Exception error){MessageBox.Show(Privacy.Error(error)+"\nDữ liệu gốc được giữ lại. Xem docs/OPERATIONS.md để khôi phục.","Nyan Control Center",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
        finally{center?.Dispose();}
    }
    public static void SaveImage(Window window,string path)
    {
        window.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(window);var bitmap=new RenderTargetBitmap((int)(window.ActualWidth*dpi.DpiScaleX),(int)(window.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
    static int Elevated(string id)
    {
        ActionOutcome outcome;
        try
        {
            var plan=Elevation.Consume(id);
            if(plan.Preview.Kind==ActionKind.Undo)
            {
                var store=new AppStore();var backup=store.GetBackup(plan.Fields["backupId"]);if(backup.GetValueOrDefault("type")!="environment"||backup.GetValueOrDefault("scope")!="Machine")throw new AppException("scope","Helper từ chối backup ngoài System environment.");Mutations.ValidateEnvironment("Machine",backup["name"],backup["value"]);backup["backupId"]=plan.Fields["backupId"];plan=plan with{Fields=backup};
            }
            var details=plan.Preview.Kind is ActionKind.StartService or ActionKind.StopService or ActionKind.RestartService
                ? $"Service: {plan.Fields["name"]}\nThao tác: {plan.Preview.Kind}\nKhông điều khiển dependency dây chuyền."
                : $"System: {plan.Fields["name"]}\nGiá trị sau thao tác: {Privacy.MaskPath(plan.Preview.Kind==ActionKind.Undo?plan.Fields["value"]:plan.Fields["newValue"])}\nCó thể ảnh hưởng process mở sau này.";
            if(MessageBox.Show(details+"\n\nXác nhận thao tác cần quyền cao này?","Nyan — xác nhận action hẹp",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)outcome=new("cancelled","Người dùng hủy tại helper; không thay đổi hệ thống.");
            else {Elevation.CheckActive(id,plan);outcome=new Mutations(new AppStore()).ExecutePlanAsync(plan,CancellationToken.None).GetAwaiter().GetResult();}
        }
        catch(Exception error){outcome=new("failed",Privacy.Error(error));}
        try{Elevation.WriteResult(id,outcome);}catch{}return outcome.Status=="success"?0:1;
    }
}
