using System.Diagnostics;

namespace Nyan.Core;

public sealed class ControlCenter : IControlCenter
{
    readonly IWindowsReader reader;readonly AppStore store;readonly StorageScanner scanner;readonly Mutations mutations;
    readonly Dictionary<Module,ModuleResult> cache=new();readonly object gate=new();readonly CancellationTokenSource lifetime=new();bool disposed;
    public AppSettings Settings=>store.Settings;
    public ControlCenter(IWindowsReader? reader=null,AppStore? store=null,MutationFixture? fixture=null)
    {
        this.reader=reader??new WindowsReader();this.store=store??new AppStore();scanner=new StorageScanner();mutations=new(this.store,fixture,this.reader);
    }
    public Task SetDarkAsync(bool dark){store.SaveSettings(Settings with{Dark=dark});return Task.CompletedTask;}
    public async Task<ModuleResult> ReadAsync(Module module,bool reveal,CancellationToken cancellationToken)
    {
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,lifetime.Token);
        try
        {
            ModuleResult result=module switch
            {
                Module.History=>ReadHistory(),Module.Snapshots=>ReadSnapshots(),Module.Cleanup=>await mutations.ReadCleanupAsync(cancel.Token),
                Module.Storage=>new(module,new(){new("name","Tên"),new("size","Kích thước")},new(),ResultState.Empty,"Chọn thư mục local để quét metadata; không đọc nội dung file.",DateTimeOffset.Now),
                Module.Settings=>new(module,new(){new("name","Thiết lập"),new("value","Giá trị")},new(){new("theme",new(){{"name","Giao diện"},{"value",Settings.Dark?"Tối":"Sáng"}}),new("retention",new(){{"name","Lưu lịch sử"},{"value",$"{Settings.RetentionDays} ngày, tối đa 1.000 mục"}}),new("data",new(){{"name","Dữ liệu ứng dụng"},{"value","%LOCALAPPDATA%\\NyanControlCenter — DPAPI, schema 1"}})},ResultState.Ready,"Sao lưu/khôi phục chỉ dữ liệu app. Không phải backup Windows.",DateTimeOffset.Now),
                _=>await reader.ReadAsync(module,reveal,cancel.Token)
            };
            if(module==Module.Startup)
            {
                foreach(var backup in store.GetBackups().Where(x=>x.GetValueOrDefault("type") is "startup-run" or "startup-file"))
                {
                    var cells=result.Columns.ToDictionary(c=>c.Key,c=>"—");cells["name"]=backup.GetValueOrDefault("name","Mục đã tắt");cells["state"]="Đã tắt bởi app";cells["status"]="Đã tắt bởi app";cells["source"]="Bản sao DPAPI User";cells["command"]="Đã che";
                    result.Rows.Add(new(backup["id"],cells,new(){{"kind","disabled"},{"backupId",backup["id"]}}));
                }
            }
            lock(gate)cache[module]=result;return result;
        }
        catch(OperationCanceledException){return ModuleResult.Failure(module,"Tác vụ đã bị hủy; tải lại khi cần.",ResultState.Cancelled);}
        catch(Exception e){return ModuleResult.Failure(module,Privacy.Error(e),e is UnauthorizedAccessException?ResultState.Denied:ResultState.Error);}
    }
    ModuleResult ReadHistory()=>new(Module.History,new(){new("time","Thời gian"),new("action","Thao tác"),new("target","Đối tượng"),new("status","Kết quả"),new("message","Chi tiết")},store.GetHistory().OrderByDescending(x=>x.At).Select(x=>new Row(x.Id,new(){{"time",x.At.ToLocalTime().ToString("g")},{"action",x.Action},{"target",x.Target},{"status",StatusText(x.Status)},{"message",x.Message}},new(){{"undoId",x.UndoId??""}})).ToList(),ResultState.Ready,"Chỉ thao tác do app thực hiện; không thu thập hoạt động Windows.",DateTimeOffset.Now);
    static string StatusText(string s)=>s switch{"success"=>"Thành công","failed"=>"Thất bại","cancelled"=>"Đã hủy","partial"=>"Một phần",_=>s};
    ModuleResult ReadSnapshots()=>new(Module.Snapshots,new(){new("name","Tên ảnh chụp"),new("time","Thời gian"),new("schema","Schema"),new("count","Số mục")},store.GetSnapshots().OrderByDescending(x=>x.At).Select(x=>new Row(x.Id,new(){{"name",x.Name},{"time",x.At.ToLocalTime().ToString("g")},{"schema",x.SchemaVersion.ToString()},{"count",x.Modules.Values.Sum(v=>v.Count).ToString()}},new(){{"at",x.At.ToString("O")}})).ToList(),ResultState.Ready,"Ảnh chụp cấu hình, không phải Restore Point. Export mã hóa chỉ nhập lại dưới cùng tài khoản Windows.",DateTimeOffset.Now);
    public async Task<ScanReport> ScanAsync(string root,IProgress<ScanProgress>? progress,CancellationToken cancellationToken)
    {
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,lifetime.Token);
        return await scanner.ScanAsync(root,progress,cancel.Token);
    }
    public Task<ActionPreview> PreviewAsync(ActionRequest request,CancellationToken cancellationToken)
    {
        Row? row=null;List<Row>? selected=null;
        lock(gate)
        {
            var module=request.Kind switch{ActionKind.EndProcess=>Module.Processes,ActionKind.DisableStartup or ActionKind.EnableStartup=>Module.Startup,ActionKind.SetEnvironment or ActionKind.DeleteEnvironment=>Module.Environment,ActionKind.StartService or ActionKind.StopService or ActionKind.RestartService=>Module.Services,_=>Module.History};
            if(request.Kind==ActionKind.EndProcess)row=Find(Module.Ports,request.TargetId)??Find(Module.Processes,request.TargetId);
            else row=Find(module,request.TargetId);
            if(request.Kind==ActionKind.EnableStartup&&row?.Meta("backupId") is {Length:>0} id)request=request with{TargetId=id};
            if(request.Kind==ActionKind.CleanupFiles)
            {
                var ids=request.SelectedIds??new();if(ids.Count!=ids.Distinct().Count())throw new AppException("duplicate","Danh sách chọn có file trùng.");
                var rows=cache.GetValueOrDefault(Module.Cleanup)?.Rows??new();selected=rows.Where(x=>ids.Contains(x.Id,StringComparer.Ordinal)).ToList();if(selected.Count!=ids.Count)throw new AppException("stale","Danh sách cleanup đã cũ; hãy quét lại.");
            }
        }
        return mutations.PreviewAsync(request,row,selected,cancellationToken);
    }
    Row? Find(Module module,string id)=>cache.GetValueOrDefault(module)?.Rows.FirstOrDefault(x=>x.Id==id);
    public Task<ActionOutcome> ExecuteAsync(string token,CancellationToken cancellationToken)=>mutations.ExecuteAsync(token,cancellationToken);
    public async Task<Snapshot> CreateSnapshotAsync(string name,CancellationToken cancellationToken)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>120||name.Any(char.IsControl))throw new AppException("name","Tên ảnh chụp cần 1–120 ký tự hợp lệ.");
        var modules=new Dictionary<string,List<Row>>();
        foreach(var module in new[]{Module.Applications,Module.Startup,Module.DevTools,Module.Environment})
        {
            var result=await ReadAsync(module,false,cancellationToken);cancellationToken.ThrowIfCancellationRequested();
            if(result.State is ResultState.Error or ResultState.Denied or ResultState.Cancelled)throw new AppException("snapshot","Không tạo ảnh chụp vì một nguồn không đọc được. Tải lại và thử lại.");
            var rows=result.Rows.Select(r=>
            {
                var cells=module==Module.Environment?r.Cells.Where(x=>x.Key is "name" or "scope").ToDictionary(x=>x.Key,x=>x.Value):r.Cells.ToDictionary(x=>x.Key,x=>Privacy.MaskPath(x.Value));
                if(r.Meta("snapshotLegacyId") is {Length:>0} legacy)cells["__legacyId"]=Privacy.Hash(legacy);
                return new Row(Privacy.Hash(r.Id),cells);
            }).ToList();
            rows.Add(new("__coverage",new(){{"state",result.State.ToString()},{"detail",Privacy.MaskPath(result.Message)}}));modules[module.ToString()]=rows;
        }
        var snapshot=new Snapshot(Guid.NewGuid().ToString("N"),name,DateTimeOffset.Now,1,modules);store.SaveSnapshot(snapshot);return snapshot;
    }
    public Task<List<SnapshotChange>> CompareSnapshotsAsync(string olderId,string newerId,CancellationToken cancellationToken){cancellationToken.ThrowIfCancellationRequested();return Task.FromResult(store.CompareSnapshots(olderId,newerId));}
    public Task ExportSnapshotAsync(string id,string path,CancellationToken cancellationToken){cancellationToken.ThrowIfCancellationRequested();store.ExportSnapshot(id,path);return Task.CompletedTask;}
    public Task ImportSnapshotAsync(string path,CancellationToken cancellationToken){cancellationToken.ThrowIfCancellationRequested();store.ImportSnapshot(path);return Task.CompletedTask;}
    public Task BackupAsync(string path,CancellationToken cancellationToken){cancellationToken.ThrowIfCancellationRequested();store.Backup(path);return Task.CompletedTask;}
    public async Task RestoreAsync(string path,CancellationToken cancellationToken){await mutations.RestoreAsync(path,cancellationToken);lock(gate)cache.Clear();}
    public Task<StoredDataInventory> GetStoredDataAsync(CancellationToken cancellationToken){cancellationToken.ThrowIfCancellationRequested();return Task.FromResult(store.GetStoredDataInventory());}
    public void OpenWindowsTool(Module module)
    {
        string? uri=module switch{Module.Applications=>"ms-settings:appsfeatures",Module.Startup=>"ms-settings:startupapps",Module.Network=>"ms-settings:network-status",_=>null};
        if(uri!=null){Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});return;}
        var system=Environment.GetFolderPath(Environment.SpecialFolder.System);
        if(module==Module.Services)Process.Start(new ProcessStartInfo(Path.Combine(system,"mmc.exe")){UseShellExecute=true,Arguments=Path.Combine(system,"services.msc")});
        else if(module==Module.Environment)Process.Start(new ProcessStartInfo(Path.Combine(system,"SystemPropertiesAdvanced.exe")){UseShellExecute=true});
        else throw new AppException("tool","Không có công cụ Windows được phép cho trang này.");
    }
    public void Dispose(){if(disposed)return;disposed=true;lifetime.Cancel();if(reader is IDisposable d)d.Dispose();lifetime.Dispose();}
}
