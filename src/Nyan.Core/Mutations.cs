using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Nyan.Core;

public sealed record MutationFixture(string Root,string RegistryKey)
{
    public void Validate(){if(!File.Exists(Path.Combine(Root,".nyan-owned"))||!RegistryKey.StartsWith(@"Software\NyanControlCenter.Tests\",StringComparison.Ordinal))throw new AppException("fixture","Fixture không có marker/phạm vi riêng.");NativeSecurity.CheckLocalPath(Root);}
}
public sealed record MutationPlan(ActionPreview Preview,Dictionary<string,string> Fields,List<Dictionary<string,string>>? Files=null);
public sealed class Mutations
{
    readonly AppStore store; readonly MutationFixture? fixture; readonly IWindowsReader? reader; readonly SemaphoreSlim serial=new(1,1);
    readonly Dictionary<string,MutationPlan> plans=new();
    public Mutations(AppStore store,MutationFixture? fixture=null,IWindowsReader? reader=null){this.store=store;this.fixture=fixture;this.reader=reader;fixture?.Validate();}
    public static void ValidateEnvironment(string scope,string name,string? value)
    {
        if(scope is not ("User" or "Machine"))throw new AppException("scope","Process scope chỉ đọc; chỉ sửa User hoặc System.");
        if(string.IsNullOrWhiteSpace(name)||name.Length>255||name.Contains('=')||name.Any(char.IsControl))throw new AppException("name","Tên biến không được rỗng, chứa dấu = hoặc ký tự điều khiển.");
        if(value is not null && (value.Length>32760||value.Contains('\0')||value.Contains('\r')||value.Contains('\n')))throw new AppException("value","Giá trị vượt giới hạn hoặc có ký tự xuống dòng/NUL.");
    }
    string EnvKey(string scope)=>fixture?.RegistryKey+@"\Environment" ?? (scope=="User"?"Environment":@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
    RegistryHive Hive(string scope)=>fixture!=null||scope=="User"?RegistryHive.CurrentUser:RegistryHive.LocalMachine;
    public Dictionary<string,string> ReadEnvironment(string scope,string name)
    {
        ValidateEnvironment(scope,name,null);
        using var hive=RegistryKey.OpenBaseKey(Hive(scope),RegistryView.Registry64);using var key=hive.OpenSubKey(EnvKey(scope));
        var value=key?.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
        if(value is not null && value is not string)throw new AppException("kind","Biến có kiểu dữ liệu registry không được hỗ trợ.");
        return new(){{"scope",scope},{"name",name},{"exists",(value!=null).ToString()},{"value",value as string??""},{"kind",value==null?"String":key!.GetValueKind(name).ToString()}};
    }
    public static string Fingerprint(Dictionary<string,string> fields)=>Privacy.Hash(string.Join('\0',fields.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Key+"="+x.Value)));
    public async Task<ActionPreview> PreviewAsync(ActionRequest request,Row? row,List<Row>? selected,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();await serial.WaitAsync(ct);
        try
        {
            foreach(var expired in plans.Where(x=>x.Value.Preview.ExpiresAt<DateTimeOffset.UtcNow).Select(x=>x.Key).ToArray())plans.Remove(expired);
            if(plans.Count>=50)throw new AppException("limit","Quá nhiều preview đang chờ; hãy thử lại sau hai phút.");
            var fields=new Dictionary<string,string>(); List<Dictionary<string,string>>? files=null;
            string title="",target="",before="",after="",warning="",token=Guid.NewGuid().ToString("N");bool undo=false,elevation=false;
            switch(request.Kind)
            {
                case ActionKind.EndProcess:
                    if(row==null||!int.TryParse(row.Meta("pid"),out var pid)||!long.TryParse(row.Meta("startTicks"),out var ticks))throw new AppException("identity","Tải lại danh sách process/port để xác minh danh tính.");
                    using(var process=NativeSecurity.CheckedProcess(pid,ticks)){target=$"{process.ProcessName} (PID {pid})";fields["pid"]=pid.ToString();fields["startTicks"]=ticks.ToString();}
                    if(row.Meta("protocol").Length>0){fields["endpointId"]=row.Id;await ValidateEndpointAsync(fields,ct);}
                    title="Kết thúc process";before="Đang chạy";after="Yêu cầu kết thúc ngay process đã chọn";warning="Dữ liệu chưa lưu có thể mất. Không thể hoàn tác; không kết thúc process con.";break;
                case ActionKind.DisableStartup:
                    if(row==null)throw new AppException("selection","Chọn mục khởi động.");
                    fields=StartupState(row);fields["fingerprint"]=Fingerprint(fields);target=row.Cell("name");
                    title="Gỡ đăng ký khởi động cùng Windows";before="Đã đăng ký; Windows Startup Apps có thể áp dụng chính sách riêng";after="Gỡ riêng mục đã chọn; lưu bản sao mã hóa";undo=true;
                    warning="Chỉ hỗ trợ registry Run của User và file trong Startup Folder của User. Không sửa scheduled task/service/nguồn System.";break;
                case ActionKind.EnableStartup:
                case ActionKind.Undo:
                    var backup=store.GetBackup(request.TargetId);fields=new(backup);fields["backupId"]=request.TargetId;
                    ValidateUndo(fields);title="Khôi phục thay đổi đã lưu";target=fields.GetValueOrDefault("name",fields.GetValueOrDefault("target","Mục đã chọn"));before="Trạng thái sau thao tác trước";after="Khôi phục giá trị/file gốc";warning="Bỏ qua nếu dữ liệu hiện tại xung đột. Không khôi phục toàn Windows.";
                    elevation=fields.GetValueOrDefault("scope")=="Machine"&&fixture==null;break;
                case ActionKind.SetEnvironment:
                case ActionKind.DeleteEnvironment:
                    var values=request.Values??throw new AppException("input","Thiếu tên/phạm vi/giá trị biến.");var scope=values.GetValueOrDefault("scope",row?.Meta("scope")??"User");var name=values.GetValueOrDefault("name",row?.Meta("name")??"");
                    var newValue=request.Kind==ActionKind.DeleteEnvironment?null:values.GetValueOrDefault("value","");ValidateEnvironment(scope,name,newValue);
                    fields=ReadEnvironment(scope,name);
                    if(row!=null&&(row.Meta("value")!=fields["value"]||row.Meta("scope")!=scope||row.Meta("name")!=name))throw new AppException("stale","Biến đã thay đổi từ lần tải danh sách; hãy tải lại.");
                    fields["fingerprint"]=Fingerprint(fields);fields["newValue"]=newValue??"";fields["delete"]=(newValue==null).ToString();
                    title=request.Kind==ActionKind.DeleteEnvironment?"Xóa biến môi trường":"Sửa biến môi trường";target=$"{scope}: {name}";before=Privacy.MaskPath(fields["value"]);after=newValue==null?"Xóa biến":Privacy.MaskPath(newValue);undo=true;elevation=scope=="Machine"&&fixture==null;
                    warning="Giá trị này có thể chứa thông tin nhạy cảm. Các process đang mở có thể chưa nhận thay đổi. PATH giữ nguyên thứ tự và entry bạn không sửa.";break;
                case ActionKind.StartService:
                case ActionKind.StopService:
                case ActionKind.RestartService:
                    if(row==null)throw new AppException("selection","Chọn service.");var service=ServicesNative.Read(row.Meta("name"));ServicesNative.ValidateAction(service,request.Kind);
                    fields["name"]=service.Name;fields["fingerprint"]=service.Fingerprint;title="Điều khiển service";target=service.Name;before=$"Trạng thái native {service.State}";after=request.Kind switch{ActionKind.StartService=>"Start",ActionKind.StopService=>"Stop",_=>"Restart"};elevation=!NativeSecurity.IsAdministrator;
                    warning="Ứng dụng phụ thuộc có thể gián đoạn hoặc mất dữ liệu. Không dừng service phụ thuộc dây chuyền; không hứa hoàn tác.";break;
                case ActionKind.CleanupFiles:
                    if(selected==null||selected.Count==0||selected.Count>500)throw new AppException("selection","Chọn từ 1 đến 500 file temp từ danh sách quét.");
                    files=new();foreach(var file in selected){ct.ThrowIfCancellationRequested();var state=CleanupState(file.Meta("path"));if(file.Meta("identity")!=state["identity"]||file.Meta("length")!=state["length"]||file.Meta("lastWriteTicks")!=state["lastWriteTicks"])throw new AppException("stale","File temp đã thay đổi; quét lại trước khi dọn.");files.Add(state);}
                    title="Dọn file temp đã chọn";target=$"{files.Count} file — {files.Sum(x=>long.Parse(x["length"])):N0} byte";before=string.Join(Environment.NewLine,files.Take(30).Select(x=>Privacy.MaskPath(x["path"])));after="Chuyển đúng các file đã chọn vào Recycle Bin";
                    warning="Không mặc định chọn file. Chỉ temp local cũ hơn 7 ngày. File liên kết/cloud/locked/thay đổi sẽ bị bỏ qua. Có thể khôi phục thủ công từ Recycle Bin khi Windows giữ lại; app không hứa undo.";break;
                default:throw new AppException("action","Thao tác không được hỗ trợ.");
            }
            var preview=new ActionPreview(token,request.Kind,title,Privacy.MaskPath(target),before,after,warning,undo,elevation,DateTimeOffset.UtcNow.AddMinutes(2));
            plans[token]=new(preview,fields,files);return preview;
        }finally{serial.Release();}
    }
    public async Task<ActionOutcome> ExecuteAsync(string token,CancellationToken ct)
    {
        try{await serial.WaitAsync(ct);}catch(OperationCanceledException){return new("cancelled","Đã hủy trước khi thực thi; không thay đổi đối tượng.");}MutationPlan? plan=null;
        try
        {
            if(!plans.Remove(token,out plan)||plan.Preview.ExpiresAt<DateTimeOffset.UtcNow)throw new AppException("expired","Preview đã hết hạn hoặc đã dùng; tạo preview mới.");
            ct.ThrowIfCancellationRequested();ActionOutcome result;
            if(plan.Preview.RequiresElevation)result=await Elevation.RunAsync(plan,ct);
            else result=await ExecutePlanAsync(plan,ct);
            return RecordOutcome(plan,result);
        }
        catch(Exception error)
        {
            var outcome=new ActionOutcome(error is OperationCanceledException?"cancelled":"failed",Privacy.Error(error));
            return plan==null?outcome:RecordOutcome(plan,outcome);
        }finally{serial.Release();}
    }
    public async Task<ActionOutcome> ExecutePlanAsync(MutationPlan plan,CancellationToken ct)
    {
        if(plan.Preview.ExpiresAt<DateTimeOffset.UtcNow)throw new AppException("expired","Yêu cầu đã hết hạn; không thay đổi hệ thống.");
        var fields=plan.Fields;
        switch(plan.Preview.Kind)
        {
            case ActionKind.EndProcess:
                if(fields.ContainsKey("endpointId"))await ValidateEndpointAsync(fields,ct);
                using(var p=NativeSecurity.CheckedProcess(int.Parse(fields["pid"]),long.Parse(fields["startTicks"]))){ct.ThrowIfCancellationRequested();p.Kill(false);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(5000);await p.WaitForExitAsync(timeout.Token);if(!p.HasExited)throw new AppException("process","Process chưa kết thúc.");}return new("success","Đã xác nhận process kết thúc.",1);
            case ActionKind.SetEnvironment:
            case ActionKind.DeleteEnvironment:return ApplyEnvironment(fields,ct);
            case ActionKind.DisableStartup:return DisableStartup(fields,ct);
            case ActionKind.EnableStartup:
            case ActionKind.Undo:return Undo(fields,ct);
            case ActionKind.StartService:
            case ActionKind.StopService:
            case ActionKind.RestartService:await ServicesNative.PerformAsync(fields["name"],plan.Preview.Kind,fields["fingerprint"],ct);return new("success","Đã kiểm tra service đạt trạng thái yêu cầu.",1);
            case ActionKind.CleanupFiles:return await CleanupAsync(plan.Files??new(),ct);
            default:throw new AppException("action","Thao tác không được hỗ trợ.");
        }
    }
    ActionOutcome RecordOutcome(MutationPlan plan,ActionOutcome outcome)
    {
        try{store.AddHistory(new(Guid.NewGuid().ToString("N"),DateTimeOffset.Now,SafeHistory(plan.Preview.Title,128),SafeHistory(plan.Preview.Target,4096),outcome.Status,SafeHistory(outcome.Message,4096),outcome.UndoId));return outcome;}
        catch{return outcome with{Message=outcome.Message+" Không lưu được lịch sử; kết quả native phía trên vẫn là trạng thái đã xác minh."};}
    }
    static string SafeHistory(string text,int limit)=>new string(text.Take(limit).Select(c=>char.IsControl(c)?' ':c).ToArray());
    async Task ValidateEndpointAsync(Dictionary<string,string> fields,CancellationToken ct)
    {
        if(reader==null)throw new AppException("endpoint","Không có adapter kiểm chứng endpoint; thao tác bị chặn.");
        var ports=await reader.ReadAsync(Module.Ports,false,ct);var endpoint=ports.Rows.FirstOrDefault(x=>x.Id==fields["endpointId"]);
        if(endpoint==null||endpoint.Meta("pid")!=fields["pid"]||endpoint.Meta("startTicks")!=fields["startTicks"])throw new AppException("stale","Endpoint hoặc process sở hữu đã thay đổi; hãy tải lại cổng mạng.");
    }
    ActionOutcome ApplyEnvironment(Dictionary<string,string> f,CancellationToken ct)
    {
        ValidateEnvironment(f["scope"],f["name"],f["newValue"]);var current=ReadEnvironment(f["scope"],f["name"]);
        if(Fingerprint(current)!=f["fingerprint"])throw new AppException("stale","Biến đã thay đổi sau preview; không ghi đè.");
        var backup=new Dictionary<string,string>(current){{"type","environment"},{"target",f["scope"]+":"+f["name"]}};var id=Guid.NewGuid().ToString("N");
        var delete=bool.Parse(f["delete"]);var kind=Enum.Parse<RegistryValueKind>(current["kind"]);if(kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))throw new AppException("kind","Kiểu biến không được hỗ trợ.");
        var expected=new Dictionary<string,string>(current) { ["exists"]=(!delete).ToString(),["value"]=delete?"":f["newValue"],["kind"]=delete?"String":kind.ToString()};backup["afterFingerprint"]=Fingerprint(expected);store.PutBackup(id,backup);
        ct.ThrowIfCancellationRequested();using var hive=RegistryKey.OpenBaseKey(Hive(f["scope"]),RegistryView.Registry64);using var key=hive.CreateSubKey(EnvKey(f["scope"]),true);
        // Re-read under the opened native key immediately before writing.
        if(Fingerprint(ReadEnvironment(f["scope"],f["name"]))!=f["fingerprint"])throw new AppException("stale","Dữ liệu registry thay đổi; thao tác bị chặn.");
        if(delete)key.DeleteValue(f["name"],false);else key.SetValue(f["name"],f["newValue"],kind);
        if(Fingerprint(ReadEnvironment(f["scope"],f["name"]))!=backup["afterFingerprint"])throw new AppException("verify","Không xác nhận được giá trị sau thay đổi. Bản sao gốc đã được giữ.");
        if(fixture==null)NativeSecurity.NotifyEnvironment();return new("success","Đã ghi và đọc lại giá trị. Process đang mở có thể chưa nhận thay đổi.",1,0,id);
    }
    Dictionary<string,string> StartupState(Row row)
    {
        var kind=row.Meta("kind");
        if(kind=="run-user")
        {
            var keyPath=row.Meta("key");var allowed=fixture?.RegistryKey+@"\Run"??@"Software\Microsoft\Windows\CurrentVersion\Run";
            if(!keyPath.Equals(allowed,StringComparison.OrdinalIgnoreCase))throw new AppException("scope","Chỉ hỗ trợ khóa Run User đã khai báo.");
            var view=Enum.TryParse<RegistryView>(row.Meta("view"),out var parsed)?parsed:RegistryView.Registry64;
            using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,view);using var key=hive.OpenSubKey(keyPath);
            var value=key?.GetValue(row.Meta("name"),null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            if(value is not string raw||raw!=row.Meta("value"))throw new AppException("stale","Mục Run đã thay đổi hoặc không còn tồn tại.");
            var valueKind=key!.GetValueKind(row.Meta("name"));if(valueKind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))throw new AppException("kind","Kiểu registry Run không được hỗ trợ.");
            return new(){{"type","startup-run"},{"key",keyPath},{"name",row.Meta("name")},{"value",raw},{"view",view.ToString()},{"registryKind",valueKind.ToString()}};
        }
        if(kind=="folder-user")
        {
            var path=row.Meta("path");var root=fixture?.Root??Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if(!NativeSecurity.IsWithin(path,root)||Path.GetDirectoryName(Path.GetFullPath(path))!=Path.GetFullPath(root))throw new AppException("scope","Chỉ hỗ trợ file trực tiếp trong Startup Folder User.");
            using var lease=new FileLease(path);if(lease.Length>4*1024*1024)throw new AppException("size","Mục Startup lớn hơn 4 MiB; chỉ đọc.");
            return new(){{"type","startup-file"},{"path",path},{"name",Path.GetFileName(path)},{"identity",lease.Identity},{"length",lease.Length.ToString()},{"lastWriteTicks",lease.WriteTicks.ToString()}};
        }
        throw new AppException("scope","Nguồn startup này chỉ đọc. Mở công cụ Windows để quản lý scheduled task/service/System.");
    }
    ActionOutcome DisableStartup(Dictionary<string,string> f,CancellationToken ct)
    {
        var id=Guid.NewGuid().ToString("N");var backup=new Dictionary<string,string>(f);backup.Remove("fingerprint");
        if(f["type"]=="startup-run")
        {
            var row=new Row("",new(),new(){{"kind","run-user"},{"key",f["key"]},{"name",f["name"]},{"value",f["value"]},{"view",f["view"]}});
            if(Fingerprint(StartupState(row))!=f["fingerprint"])throw new AppException("stale","Run thay đổi sau preview.");
            store.PutBackup(id,backup);ct.ThrowIfCancellationRequested();using var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,Enum.Parse<RegistryView>(f["view"]));using var key=hive.OpenSubKey(f["key"],true)??throw new AppException("missing","Khóa Run đã mất.");
            if((key.GetValue(f["name"],null,RegistryValueOptions.DoNotExpandEnvironmentNames)as string)!=f["value"])throw new AppException("stale","Run thay đổi; không xóa.");key.DeleteValue(f["name"],true);if(key.GetValue(f["name"])!=null)throw new AppException("verify","Run vẫn tồn tại sau thao tác.");
        }
        else
        {
            using var lease=new FileLease(f["path"],true);if(lease.Identity!=f["identity"]||lease.Length.ToString()!=f["length"]||lease.WriteTicks.ToString()!=f["lastWriteTicks"])throw new AppException("stale","File Startup thay đổi sau preview.");
            var data=new byte[lease.Length];var read=RandomAccess.Read(lease.Handle,data,0);if(read!=data.Length)throw new AppException("read","Không đọc đủ file Startup; không xóa.");backup["contents"]=Convert.ToBase64String(data);store.PutBackup(id,backup);ct.ThrowIfCancellationRequested();lease.Delete();
        }
        return new("success","Đã tắt riêng mục Startup và giữ bản sao mã hóa.",1,0,id);
    }
    void ValidateUndo(Dictionary<string,string> f)
    {
        var required=f.GetValueOrDefault("type") switch{"environment"=>new[]{"scope","name","value","exists","kind","afterFingerprint"},"startup-run"=>new[]{"key","name","value","view","registryKind"},"startup-file"=>new[]{"path","contents"},_=>Array.Empty<string>()};
        if(required.Length==0||required.Any(key=>!f.ContainsKey(key)))throw new AppException("backup","Bản sao thiếu dữ liệu hoặc không hỗ trợ hoàn tác.");
        switch(f.GetValueOrDefault("type"))
        {
            case "environment":ValidateEnvironment(f["scope"],f["name"],f["value"]);if(!bool.TryParse(f["exists"],out _)||!Enum.TryParse<RegistryValueKind>(f["kind"],out var envKind)||envKind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))throw new AppException("backup","Bản sao biến không hợp lệ.");if(Fingerprint(ReadEnvironment(f["scope"],f["name"]))!=f["afterFingerprint"])throw new AppException("conflict","Biến hiện tại đã thay đổi; không ghi đè bản sao.");break;
            case "startup-run":
                var allowed=fixture?.RegistryKey+@"\Run"??@"Software\Microsoft\Windows\CurrentVersion\Run";if(f["key"]!=allowed)throw new AppException("scope","Backup Run ngoài phạm vi.");
                if(f["name"].Length is 0 or >16383||f["name"].Contains('\0')||f["value"].Contains('\0')||!Enum.TryParse<RegistryValueKind>(f["registryKind"],out var runKind)||runKind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)||!Enum.TryParse<RegistryView>(f["view"],out var runView)||runView is not (RegistryView.Registry32 or RegistryView.Registry64))throw new AppException("backup","Bản sao Run có tên/kiểu không hợp lệ; không ghi registry.");
                using(var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,Enum.Parse<RegistryView>(f["view"])))using(var key=hive.OpenSubKey(f["key"]))if(key?.GetValue(f["name"])!=null)throw new AppException("conflict","Run đã có giá trị mới; không ghi đè.");break;
            case "startup-file":if(f.GetValueOrDefault("contents","").Length>6*1024*1024||Convert.FromBase64String(f["contents"]).Length>4*1024*1024)throw new AppException("backup","Bản sao file Startup vượt giới hạn.");var root=fixture?.Root??Environment.GetFolderPath(Environment.SpecialFolder.Startup);if(!NativeSecurity.IsWithin(f["path"],root)||Path.GetDirectoryName(f["path"])!=Path.GetFullPath(root))throw new AppException("scope","Backup Startup ngoài phạm vi.");NativeSecurity.CheckLocalPath(f["path"],true);if(File.Exists(f["path"]))throw new AppException("conflict","File Startup đã tồn tại; không ghi đè.");break;
            default:throw new AppException("undo","Bản sao này không hỗ trợ hoàn tác tự động.");
        }
    }
    ActionOutcome Undo(Dictionary<string,string> f,CancellationToken ct)
    {
        ValidateUndo(f);ct.ThrowIfCancellationRequested();
        switch(f["type"])
        {
            case "environment":using(var hive=RegistryKey.OpenBaseKey(Hive(f["scope"]),RegistryView.Registry64))using(var key=hive.CreateSubKey(EnvKey(f["scope"]),true)){ValidateUndo(f);if(bool.Parse(f["exists"]))key.SetValue(f["name"],f["value"],Enum.Parse<RegistryValueKind>(f["kind"]));else key.DeleteValue(f["name"],false);}var restored=ReadEnvironment(f["scope"],f["name"]);if(restored["value"]!=f["value"]||bool.Parse(restored["exists"])!=bool.Parse(f["exists"])||restored["kind"]!=f["kind"])throw new AppException("verify","Không xác nhận được biến đã hoàn tác.");if(fixture==null)NativeSecurity.NotifyEnvironment();break;
            case "startup-run":using(var hive=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,Enum.Parse<RegistryView>(f["view"])))using(var key=hive.CreateSubKey(f["key"],true)){ValidateUndo(f);key.SetValue(f["name"],f["value"],Enum.Parse<RegistryValueKind>(f["registryKind"]));if((key.GetValue(f["name"],null,RegistryValueOptions.DoNotExpandEnvironmentNames)as string)!=f["value"])throw new AppException("verify","Run khôi phục không khớp.");}break;
            case "startup-file":var data=Convert.FromBase64String(f["contents"]);using(var parent=new DirectoryLease(Path.GetDirectoryName(f["path"])!))using(var stream=new FileStream(f["path"],FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(data);stream.Flush(true);}if(Privacy.Hash(Convert.ToBase64String(File.ReadAllBytes(f["path"])))!=Privacy.Hash(f["contents"]))throw new AppException("verify","File Startup khôi phục không khớp.");break;
        }
        store.RemoveBackup(f["backupId"]);return new("success","Đã khôi phục và xác minh đối tượng gốc.",1);
    }
    public async Task RestoreAsync(string path,CancellationToken ct)
    {
        await serial.WaitAsync(ct);try{ct.ThrowIfCancellationRequested();plans.Clear();store.Restore(path);}finally{serial.Release();}
    }
    public string CleanupRoot=>fixture?.Root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Temp");
    public Dictionary<string,string> CleanupState(string path)
    {
        if(!NativeSecurity.IsWithin(path,CleanupRoot))throw new AppException("scope","Cleanup chỉ được thay đổi file trong Temp User đã quét.");
        using var lease=new FileLease(path);if(new DateTime(lease.WriteTicks,DateTimeKind.Utc)>DateTime.UtcNow.AddDays(-7))throw new AppException("age","File mới hơn 7 ngày; bỏ qua.");
        return new(){{"path",Path.GetFullPath(path)},{"identity",lease.Identity},{"length",lease.Length.ToString()},{"lastWriteTicks",lease.WriteTicks.ToString()}};
    }
    public async Task<ModuleResult> ReadCleanupAsync(CancellationToken ct)=>await Task.Run(()=>
    {
        var rows=new List<Row>();int skipped=0,seen=0;var stack=new Stack<string>();NativeSecurity.CheckLocalPath(CleanupRoot);stack.Push(CleanupRoot);
        while(stack.Count>0&&seen<10000&&rows.Count<2000)
        {
            ct.ThrowIfCancellationRequested();var directory=stack.Pop();
            try{foreach(var path in Directory.EnumerateFileSystemEntries(directory)){ct.ThrowIfCancellationRequested();if(++seen>10000)break;try{NativeSecurity.CheckLocalPath(path);if(Directory.Exists(path)){stack.Push(path);continue;}var f=CleanupState(path);rows.Add(new(Privacy.Hash(path),new(){{"name",Path.GetFileName(path)},{"size",$"{long.Parse(f["length"]):N0} B"},{"updated",new DateTime(long.Parse(f["lastWriteTicks"]),DateTimeKind.Utc).ToLocalTime().ToString("g")},{"path",Privacy.MaskPath(path)}},f));}catch(Exception e)when(e is IOException or UnauthorizedAccessException or AppException or Win32Exception){skipped++;}}}catch(Exception e)when(e is IOException or UnauthorizedAccessException){skipped++;}
        }
        return new ModuleResult(Module.Cleanup,new(){new("name","File temp"),new("size","Kích thước"),new("updated","Sửa lần cuối"),new("path","Đường dẫn")},rows,seen>=10000||rows.Count>=2000||skipped>0?ResultState.Partial:rows.Count==0?ResultState.Empty:ResultState.Ready,$"Chỉ Temp User cũ hơn 7 ngày; chưa chọn file nào. Bỏ qua {skipped} mục; giới hạn 10.000 mục/2.000 ứng viên.",DateTimeOffset.Now);
    },ct);
    async Task<ActionOutcome> CleanupAsync(List<Dictionary<string,string>> files,CancellationToken ct)
    {
        int success=0,failed=0;var reasons=new HashSet<string>();var quarantine=Path.Combine(store.Root,"quarantine");Directory.CreateDirectory(quarantine);NativeSecurity.CheckLocalPath(quarantine);
        foreach(var f in files)
        {
            if(ct.IsCancellationRequested)return new(success>0?"partial":"cancelled",$"Đã hủy; {success} file vào Recycle Bin, {failed} bỏ qua.",success,failed);
            string? moved=null;string? backupId=null;
            try
            {
                var fresh=CleanupState(f["path"]);if(Fingerprint(fresh)!=Fingerprint(f))throw new AppException("stale","File thay đổi sau preview.");
                using(var lease=new FileLease(f["path"],true))
                {
                    if(lease.Identity!=f["identity"]||lease.Length.ToString()!=f["length"]||lease.WriteTicks.ToString()!=f["lastWriteTicks"])throw new AppException("stale","Danh tính file thay đổi.");
                    moved=Path.Combine(quarantine,Guid.NewGuid().ToString("N")+"-"+Path.GetFileName(f["path"]));backupId=Guid.NewGuid().ToString("N");store.PutBackup(backupId,new(){{"type","quarantine"},{"path",f["path"]},{"quarantine",moved},{"identity",lease.Identity}});lease.RenameTo(moved);
                }
                var recycled=await RecycleFile.RecycleAsync(moved);if(!recycled.Recycled||File.Exists(moved))throw new AppException("recycle","Windows chưa xác nhận file vào Recycle Bin; giữ/khôi phục file an toàn.");success++;store.RemoveBackup(backupId);
            }
            catch(Exception error)
            {
                failed++;reasons.Add(Privacy.Error(error));
                if(moved!=null&&File.Exists(moved)&&!File.Exists(f["path"]))try{using var lease=new FileLease(moved,true);if(lease.Identity==f["identity"]){lease.RenameTo(f["path"]);if(backupId!=null)store.RemoveBackup(backupId);}}catch{reasons.Add("File giữ an toàn trong quarantine; xem hướng dẫn khôi phục.");}
            }
            await Task.Yield();
        }
        return new(failed==0?"success":success==0?"failed":"partial",$"Recycle Bin: {success}; bỏ qua/thất bại: {failed}. {string.Join(" ",reasons.Take(3))}",success,failed);
    }
}

public static class Elevation
{
    static string RequestRoot=>Path.Combine(NativeSecurity.LocalRoot,"requests");
    public static async Task<ActionOutcome> RunAsync(MutationPlan plan,CancellationToken ct)
    {
        if(plan.Preview.Kind is not (ActionKind.SetEnvironment or ActionKind.DeleteEnvironment or ActionKind.Undo or ActionKind.StartService or ActionKind.StopService or ActionKind.RestartService))throw new AppException("elevation","Action này không được nâng quyền.");
        Directory.CreateDirectory(RequestRoot);NativeSecurity.CheckLocalPath(RequestRoot);var id=Guid.NewGuid().ToString("N");var request=Path.Combine(RequestRoot,id+".request");var result=Path.Combine(RequestRoot,id+".result");
        File.WriteAllBytes(request,NativeSecurity.Protect(JsonSerializer.SerializeToUtf8Bytes(plan)));var active=Path.Combine(RequestRoot,id+".active");File.WriteAllText(active,"");
        try
        {
            var executable=Environment.ProcessPath??throw new AppException("executable","Không xác minh được executable của app.");
            using var process=Process.Start(new ProcessStartInfo(executable){UseShellExecute=true,Verb="runas",Arguments="--elevated-action "+id,WorkingDirectory=AppContext.BaseDirectory})??throw new AppException("uac","Không mở được helper UAC.");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(2));
            try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){return new("failed","Helper chưa xác nhận kết quả. Không lặp thao tác; tải lại để kiểm tra trạng thái Windows.");}
            if(!File.Exists(result))return new("failed","Helper không trả kết quả; thao tác chưa được xác nhận.");
            if(new FileInfo(result).Length>65536)throw new AppException("result","Kết quả helper vượt giới hạn.");
            return JsonSerializer.Deserialize<ActionOutcome>(NativeSecurity.Unprotect(File.ReadAllBytes(result)))??new("failed","Kết quả helper không hợp lệ.");
        }
        catch(Win32Exception e)when(e.NativeErrorCode==1223){return new("cancelled","Người dùng đã hủy UAC; không thay đổi hệ thống.");}
        finally{if(File.Exists(active))File.Delete(active);if(File.Exists(request))File.Delete(request);if(File.Exists(result))File.Delete(result);}
    }
    public static MutationPlan Consume(string id)
    {
        if(!Regex.IsMatch(id,"^[a-f0-9]{32}$"))throw new AppException("request","Mã yêu cầu không hợp lệ.");if(!NativeSecurity.IsAdministrator)throw new AppException("uac","Helper cần quyền Windows đã xác nhận.");
        NativeSecurity.CheckLocalPath(RequestRoot);var path=Path.Combine(RequestRoot,id+".request");NativeSecurity.CheckLocalPath(path);var processing=Path.Combine(RequestRoot,id+".consumed");
        File.Move(path,processing,false);
        try
        {
            if(new FileInfo(processing).Length>1024*1024)throw new AppException("request","Yêu cầu vượt giới hạn.");
            var plan=JsonSerializer.Deserialize<MutationPlan>(NativeSecurity.Unprotect(File.ReadAllBytes(processing)))??throw new AppException("request","Yêu cầu không hợp lệ.");
            if(plan.Preview.ExpiresAt<DateTimeOffset.UtcNow||plan.Preview.ExpiresAt>DateTimeOffset.UtcNow.AddMinutes(2)||!plan.Preview.RequiresElevation||plan.Files!=null)throw new AppException("expiry","Yêu cầu hết hạn hoặc ngoài phạm vi.");
            if(plan.Preview.Kind is ActionKind.SetEnvironment or ActionKind.DeleteEnvironment){if(plan.Fields.GetValueOrDefault("scope")!="Machine")throw new AppException("scope","Helper chỉ sửa System scope.");Mutations.ValidateEnvironment("Machine",plan.Fields["name"],plan.Fields["newValue"]);}
            else if(plan.Preview.Kind==ActionKind.Undo){if(plan.Fields.GetValueOrDefault("type")!="environment"||plan.Fields.GetValueOrDefault("scope")!="Machine")throw new AppException("scope","Helper chỉ hoàn tác biến System.");}
            else if(plan.Preview.Kind is ActionKind.StartService or ActionKind.StopService or ActionKind.RestartService)ServicesNative.ValidateName(plan.Fields["name"]);
            else throw new AppException("action","Helper từ chối action không được phép.");
            return plan;
        }finally{File.Delete(processing);}
    }
    public static void CheckActive(string id,MutationPlan plan)
    {
        if(!Regex.IsMatch(id,"^[a-f0-9]{32}$")||plan.Preview.ExpiresAt<DateTimeOffset.UtcNow)throw new AppException("expired","Yêu cầu đã hết hạn hoặc bị hủy; không thay đổi hệ thống.");
        NativeSecurity.CheckLocalPath(Path.Combine(RequestRoot,id+".active"));
    }
    public static void WriteResult(string id,ActionOutcome result){if(!Regex.IsMatch(id,"^[a-f0-9]{32}$")||!File.Exists(Path.Combine(RequestRoot,id+".active")))return;NativeSecurity.CheckLocalPath(RequestRoot);File.WriteAllBytes(Path.Combine(RequestRoot,id+".result"),NativeSecurity.Protect(JsonSerializer.SerializeToUtf8Bytes(result)));}
}
