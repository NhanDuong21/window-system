namespace Nyan.Core;

public enum Module { Dashboard, Applications, Startup, Processes, Storage, Services, DevTools, Ports, Environment, Network, Snapshots, Cleanup, History, Settings }
public enum ResultState { Ready, Partial, Empty, Denied, Error, Cancelled }
public sealed record Column(string Key, string Title);
public sealed record Row(string Id, Dictionary<string,string> Cells, Dictionary<string,string>? Data = null)
{
    public string Cell(string key) => Cells.GetValueOrDefault(key, "—");
    public string Meta(string key) => Data?.GetValueOrDefault(key, "") ?? "";
}
public sealed record ModuleResult(Module Module, List<Column> Columns, List<Row> Rows, ResultState State = ResultState.Ready, string Message = "", DateTimeOffset? UpdatedAt = null)
{
    public DateTimeOffset Timestamp => UpdatedAt ?? DateTimeOffset.Now;
    public static ModuleResult Failure(Module module, string message, ResultState state = ResultState.Error) => new(module, new(), new(), state, message, DateTimeOffset.Now);
}
public sealed record ScanProgress(long Files, long Bytes, int Skipped, string Status);
public sealed record ScanReport(string Root, List<Row> Rows, long Files, long Bytes, long? AllocatedBytes, int Skipped, bool Cancelled, DateTimeOffset CompletedAt);
public enum ActionKind { EndProcess, DisableStartup, EnableStartup, SetEnvironment, DeleteEnvironment, StartService, StopService, RestartService, CleanupFiles, Undo }
public sealed record ActionRequest(ActionKind Kind, string TargetId, Dictionary<string,string>? Values = null, List<string>? SelectedIds = null);
public sealed record ActionPreview(string Token, ActionKind Kind, string Title, string Target, string Before, string After, string Warning, bool CanUndo, bool RequiresElevation, DateTimeOffset ExpiresAt);
public sealed record ActionOutcome(string Status, string Message, int Succeeded = 0, int Failed = 0, string? UndoId = null);
public sealed record HistoryEntry(string Id, DateTimeOffset At, string Action, string Target, string Status, string Message, string? UndoId = null);
public sealed record Snapshot(string Id, string Name, DateTimeOffset At, int SchemaVersion, Dictionary<string,List<Row>> Modules);
public sealed record SnapshotChange(string Module, string Kind, string Key, string Before, string After);
public sealed record AppSettings(int SchemaVersion = 1, bool Dark = false, int RetentionDays = 30);

public interface IWindowsReader
{
    Task<ModuleResult> ReadAsync(Module module, bool reveal, CancellationToken cancellationToken);
}
public interface IControlCenter : IDisposable
{
    AppSettings Settings { get; }
    Task SetDarkAsync(bool dark);
    Task<ModuleResult> ReadAsync(Module module, bool reveal, CancellationToken cancellationToken);
    Task<ScanReport> ScanAsync(string root, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
    Task<ActionPreview> PreviewAsync(ActionRequest request, CancellationToken cancellationToken);
    Task<ActionOutcome> ExecuteAsync(string token, CancellationToken cancellationToken);
    Task<Snapshot> CreateSnapshotAsync(string name, CancellationToken cancellationToken);
    Task<List<SnapshotChange>> CompareSnapshotsAsync(string olderId, string newerId, CancellationToken cancellationToken);
    Task ExportSnapshotAsync(string id, string path, CancellationToken cancellationToken);
    Task ImportSnapshotAsync(string path, CancellationToken cancellationToken);
    Task BackupAsync(string path, CancellationToken cancellationToken);
    Task RestoreAsync(string path, CancellationToken cancellationToken);
    void OpenWindowsTool(Module module);
}
public sealed class AppException : Exception
{
    public string Code { get; }
    public AppException(string code, string message) : base(message) { Code = code; }
}
