using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nyan.Core;

namespace Nyan.App;

public static class UiChecks
{
    public static async Task<List<string>> RunAsync(MainWindow window, string evidenceDirectory)
    {
        if (!window.FixtureModeForTest || !Directory.Exists(evidenceDirectory) || !File.Exists(Path.Combine(evidenceDirectory, ".nyan-owned")))
            throw new InvalidOperationException("UI evidence phải có marker .nyan-owned do runner tạo.");
        var results = new List<string>();
        void Check(string name, bool passed) { results.Add($"{(passed ? "PASS" : "FAIL")} {name}"); }
        Check("sensitive-hidden-on-first-open", !window.RevealForTest);
        Check("table-row-column-virtualization", window.VirtualizedForTest);
        foreach (var module in Enum.GetValues<Module>())
        {
            await window.NavigateForTestAsync(module);
            Check("navigate-" + module, window.CurrentModuleForTest == module && window.StatusForTest.Length > 0);
        }
        await window.NavigateForTestAsync(Module.Applications);
        var originalDark = window.DarkForTest;
        if (originalDark) await window.ToggleThemeForTestAsync();
        await LayoutAsync(window);
        SavePng(window, Path.Combine(evidenceDirectory, "applications-light.png"));
        Check("light-mode", !window.DarkForTest);
        await window.ToggleThemeForTestAsync(); Check("dark-mode", window.DarkForTest);
        Check("dark-choice-persisted", window.PersistedDarkForTest);
        await LayoutAsync(window); SavePng(window, Path.Combine(evidenceDirectory, "applications-dark.png"));
        await window.ToggleThemeForTestAsync(); Check("light-restored", !window.DarkForTest);
        var rows = Enumerable.Range(0, 12000).Select(i => new Row(i.ToString(), new() { ["name"] = $"Ứng dụng thử {i:00000}", ["path"] = $"C:\\fixture có dấu\\thư mục {i}\\đường dẫn dài kiểm tra hiển thị\\app.exe", ["source"] = i % 2 == 0 ? "User" : "Machine" })).ToList();
        window.ShowResultForTest(new ModuleResult(Module.Applications, new() { new("name", "Tên"), new("path", "Đường dẫn"), new("source", "Nguồn") }, rows));
        var stopwatch = Stopwatch.StartNew(); window.FilterForTest("Ứng dụng thử 11999"); stopwatch.Stop();
        Check("filter-large-unicode-table", window.VisibleRowsForTest == 1);
        results.Add($"METRIC filter-12000-rows-ms={stopwatch.Elapsed.TotalMilliseconds:F2}");
        window.FilterForTest("không có trong fixture"); Check("filter-empty", window.VisibleRowsForTest == 0);
        window.FilterForTest(""); Check("filter-reset", window.VisibleRowsForTest == 12000);
        window.SelectFirstForTest();var selectedId=window.SelectedIdForTest;
        window.ShowResultForTest(new(Module.Applications,new(){new("name","Tên"),new("path","Đường dẫn"),new("source","Nguồn")},rows.Select(r=>r with{Cells=new(r.Cells)}).ToList()));
        Check("refresh-retains-selected-stable-row",window.SelectedRowsForTest==1&&window.SelectedIdForTest==selectedId);
        window.Width = 940; window.Height = 680;
        await LayoutAsync(window); SavePng(window, Path.Combine(evidenceDirectory, "resized-unicode.png"));
        Check("minimum-window-layout", window.ActualWidth >= window.MinWidth && window.ActualHeight >= window.MinHeight);
        window.Width = 1220; window.Height = 820;
        foreach (var state in new[] { ResultState.Empty, ResultState.Denied, ResultState.Error, ResultState.Partial, ResultState.Cancelled })
        {
            window.ShowResultForTest(new ModuleResult(Module.Applications, new() { new("name", "Tên") }, state == ResultState.Partial ? new() { new("partial", new() { ["name"] = "Một mục đọc được" }) } : new(), state, "Tình huống fixture: nguồn thiếu dữ liệu hoặc quyền.", DateTimeOffset.Now));
            Check("state-" + state, window.StatusForTest.Contains("Tình huống fixture", StringComparison.Ordinal));
            if (state is ResultState.Denied or ResultState.Partial) { await LayoutAsync(window); SavePng(window, Path.Combine(evidenceDirectory, "state-" + state.ToString().ToLowerInvariant() + ".png")); }
        }
        var preview = new ActionPreview("ui-fixture-preview", ActionKind.EndProcess, "Kết thúc tiến trình fixture", "Tiến trình do test sở hữu · PID fixture", "Đang chạy", "Đã kết thúc", "Dữ liệu chưa lưu sẽ mất. Chỉ fixture được kiểm thử.", false, false, DateTimeOffset.Now.AddMinutes(2));
        var dialog = new PreviewDialog(preview, false) { Owner = window };
        dialog.Loaded += async (_, _) =>
        {
            Check("preview-default-cancel", dialog.CancelButton.IsDefault && dialog.CancelButton.IsCancel);
            Check("preview-requires-explicit-confirm", !dialog.ExecuteButton.IsEnabled);
            await LayoutAsync(dialog); SavePng(dialog, Path.Combine(evidenceDirectory, "preview-cancel.png"));
            dialog.ConfirmCheck.IsChecked = true; Check("preview-confirm-enables-action", dialog.ExecuteButton.IsEnabled);
            dialog.ConfirmCheck.IsChecked = false; Check("preview-uncheck-disables-action", !dialog.ExecuteButton.IsEnabled);
            dialog.DialogResult = false;
        };
        Check("preview-cancel-result", dialog.ShowDialog() != true);
        var confirmed = new PreviewDialog(preview, false) { Owner = window };
        confirmed.Loaded += (_, _) => { confirmed.ConfirmCheck.IsChecked = true; confirmed.ExecuteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); };
        Check("preview-explicit-confirm-result", confirmed.ShowDialog() == true);
        var expired = new PreviewDialog(preview with { ExpiresAt = DateTimeOffset.Now.AddSeconds(-1) }, false) { Owner = window };
        expired.Loaded += (_, _) => { expired.ConfirmCheck.IsChecked = true; Check("expired-preview-disabled", !expired.ExecuteButton.IsEnabled); expired.DialogResult = false; };
        expired.ShowDialog();
        await window.PaletteForTestAsync((palette, input, list) =>
        {
            Check("palette-module-navigation", list.Items.Count == Enum.GetValues<Module>().Length);
            input.Text = "Một mục đọc được"; Check("palette-cached-row-search", list.Items.Count == 1);
            input.Text = "không có trong fixture"; Check("palette-empty", list.Items.Count == 0);
            palette.DialogResult = false;
        });
        var scanRoot = Path.Combine(evidenceDirectory, "scan-fixture có dấu");
        Directory.CreateDirectory(Path.Combine(scanRoot, "thư mục con"));
        File.WriteAllText(Path.Combine(scanRoot, ".nyan-owned"), "Nyan owned UI scan fixture");
        for (var i = 0; i < 256; i++) File.WriteAllText(Path.Combine(scanRoot, $"file thử {i}.txt"), "Nyan fixture");
        File.WriteAllText(Path.Combine(scanRoot, "thư mục con", "dữ liệu thử.txt"), "Nyan fixture");
        await window.NavigateForTestAsync(Module.Storage); await window.ScanForTestAsync(scanRoot);
        Check("storage-native-fixture-scan", window.VisibleRowsForTest > 0);
        await LayoutAsync(window); SavePng(window, Path.Combine(evidenceDirectory, "storage-fixture.png"));
        await window.DrillDownForTestAsync();
        Check("storage-directory-drilldown", window.CurrentRootForTest == Path.Combine(scanRoot, "thư mục con"));
        var cancelledScan = window.ScanForTestAsync(scanRoot); window.CancelForTest(); await cancelledScan;
        Check("storage-cancellation", window.StatusForTest.Contains("hủy", StringComparison.OrdinalIgnoreCase));
        await CheckStorageInteractionsAsync(Check, evidenceDirectory);
        await CheckStoredDataAsync(Check, evidenceDirectory);
        await window.NavigateForTestAsync(Module.Environment); await window.SetRevealForTestAsync(true);
        window.ShowResultForTest(new ModuleResult(Module.Environment, new() { new("value", "Giá trị") }, new() { new("secret-fixture", new() { ["value"] = "ui-fixture-secret-value" }) }));
        Check("reveal-fixture-value-cached", window.CachedContainsForTest("ui-fixture-secret-value"));
        await window.SetRevealForTestAsync(false);
        Check("reveal-off-clears-sensitive-cache", !window.CachedContainsForTest("ui-fixture-secret-value") && !window.RevealForTest);
        await window.NavigateForTestAsync(Module.Cleanup); Check("cleanup-default-unselected", window.SelectedRowsForTest == 0);
        await window.NavigateForTestAsync(Module.Dashboard); await LayoutAsync(window);
        SavePng(window, Path.Combine(evidenceDirectory, "dashboard-light.png"));
        if (originalDark) await window.ToggleThemeForTestAsync();
        return results;
    }
    private static async Task CheckStorageInteractionsAsync(Action<string, bool> check, string evidenceDirectory)
    {
        var center = new InteractionCenter(); var window = new MainWindow(center, true);
        var root = Path.Combine(evidenceDirectory, "controlled-scan");
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, ".nyan-owned"), "Owned controlled UI scan fixture; fake metadata only");
        try
        {
            await window.NavigateForTestAsync(Module.Storage);
            var columns = new List<Column> { new("name", "Tên"), new("size", "Kích thước logic"), new("allocated", "Chiếm đĩa") };
            var rows = new List<Row>
            {
                new("large", new() { ["name"] = "Lớn", ["size"] = "1,00 GB", ["allocated"] = "0 B" }, new() { ["length"] = "1073741824", ["allocatedBytes"] = "0" }),
                new("small", new() { ["name"] = "Nhỏ", ["size"] = "900.00 MB", ["allocated"] = "900,00 MB" }, new() { ["length"] = "943718400", ["allocatedBytes"] = "943718400" }),
                new("unknown", new() { ["name"] = "Chưa đọc được", ["size"] = "Không xác định", ["allocated"] = "Không xác định" }, new() { ["length"] = "", ["allocatedBytes"] = "" })
            };
            window.ShowResultForTest(new(Module.Storage, columns, rows));
            foreach (var (key, ascending, descending) in new[] { ("size", new[] { "small", "large", "unknown" }, new[] { "large", "small", "unknown" }), ("allocated", new[] { "large", "small", "unknown" }, new[] { "small", "large", "unknown" }) })
            {
                window.SortForTest(key, ListSortDirection.Ascending); check("storage-sort-" + key + "-ascending-numeric-unknown-last", window.VisibleIdsForTest.SequenceEqual(ascending));
                window.SortForTest(key, ListSortDirection.Descending); check("storage-sort-" + key + "-descending-numeric-unknown-last", window.VisibleIdsForTest.SequenceEqual(descending));
            }
            var child = new Row("child", new() { ["name"] = "Thư mục con" }, new() { ["kind"] = "directory", ["path"] = Path.Combine(root, "child") });
            window.ShowResultForTest(new(Module.Storage, new() { new("name", "Tên") }, new() { child }));
            var first = window.ScanForTestAsync(root);
            window.DoubleClickDirectoryForTest("child"); window.DoubleClickDirectoryForTest("child");
            check("storage-repeated-doubleclick-does-not-overlap", center.Scans.Count == 1 && window.TaskRunningForTest);
            window.CancelForTest(); check("storage-cancel-current-scan-token", center.Scans[0].Token.IsCancellationRequested);
            center.Scans[0].Complete(new() { child }, cancelled: true); await first;
            check("storage-cancel-completion-clears-busy", !window.TaskRunningForTest);
            var old = window.ScanForTestAsync(root);
            await window.NavigateForTestAsync(Module.Settings);
            check("storage-navigation-cancels-scan-token", center.Scans[1].Token.IsCancellationRequested);
            var settingsStatus = window.StatusForTest;
            center.Scans[1].Progress?.Report(new(123, 456, 0, "LATE OLD PROGRESS")); await DrainAsync(window);
            check("storage-late-progress-does-not-change-other-page", window.StatusForTest == settingsStatus);
            var revisit = window.NavigateForTestAsync(Module.Storage);
            check("storage-revisit-starts-new-scan", center.Scans.Count == 3 && window.TaskRunningForTest);
            center.Scans[1].Progress?.Report(new(123, 456, 0, "LATE OLD PROGRESS"));
            center.Scans[1].Complete(new() { new("old", new() { ["name"] = "Cũ" }) }); await old; await DrainAsync(window);
            check("storage-late-result-and-finally-preserve-current-scan", window.TaskRunningForTest && !window.VisibleIdsForTest.Contains("old") && !window.StatusForTest.Contains("LATE OLD PROGRESS", StringComparison.Ordinal));
            center.Scans[2].Progress?.Report(new(1, 2, 0, "CURRENT PROGRESS")); await DrainAsync(window);
            check("storage-current-progress-visible", window.StatusForTest.Contains("CURRENT PROGRESS", StringComparison.Ordinal));
            window.CancelForTest(); check("storage-revisit-cancel-targets-new-token", center.Scans[2].Token.IsCancellationRequested);
            center.Scans[2].Complete(new() { new("fresh", new() { ["name"] = "Mới" }) }, cancelled: true); await revisit;
            var currentStatus = window.StatusForTest;
            center.Scans[1].Progress?.Report(new(999, 999, 0, "LATE OLD PROGRESS")); await DrainAsync(window);
            check("storage-revisit-retains-current-result-after-late-progress", window.VisibleIdsForTest.SequenceEqual(new[] { "fresh" }) && !window.TaskRunningForTest && window.StatusForTest == currentStatus);
        }
        finally { foreach (var scan in center.Scans) scan.Complete(new(), cancelled: true); window.Close(); center.Dispose(); }
    }
    private static async Task CheckStoredDataAsync(Action<string, bool> check, string evidenceDirectory)
    {
        var center = new InteractionCenter(); var window = new MainWindow(center, true);
        _ = new WindowInteropHelper(window).EnsureHandle();
        try
        {
            await window.NavigateForTestAsync(Module.Settings);
            window.PreviewOpenedForTest = dialog => dialog.DialogResult = false;
            await window.StoredDataForTestAsync(dialog =>
            {
                check("stored-data-shows-persisted-usage", dialog.ItemsForTest == 5 && dialog.UsageForTest.Contains("4/100", StringComparison.Ordinal) && dialog.UsageForTest.Contains("1/100", StringComparison.Ordinal));
                dialog.SelectForTest("startup"); check("stored-data-startup-undo-only", dialog.UndoEnabledForTest && !dialog.DiscardEnabledForTest);
                dialog.SelectForTest("quarantine"); check("stored-data-quarantine-view-only", !dialog.UndoEnabledForTest && !dialog.DiscardEnabledForTest);
                dialog.SelectForTest("environment"); check("stored-data-environment-discard-and-undo", dialog.UndoEnabledForTest && dialog.DiscardEnabledForTest);
                SavePng(dialog, Path.Combine(evidenceDirectory, "stored-data-light.png"));
                dialog.DiscardForTest();
                check("stored-data-cancel-preview-does-not-execute", center.Requests.LastOrDefault()?.Kind == ActionKind.DiscardBackup && center.Executed == 0);
                dialog.Close();
            });
            await window.ToggleThemeForTestAsync();
            window.PreviewOpenedForTest = dialog => { check("stored-data-discard-explicit-confirm-required", !dialog.ExecuteButton.IsEnabled); dialog.ConfirmCheck.IsChecked = true; dialog.ExecuteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); };
            await window.StoredDataForTestAsync(dialog =>
            {
                dialog.SelectForTest("snapshot"); check("stored-data-snapshot-discard-only", !dialog.UndoEnabledForTest && dialog.DiscardEnabledForTest);
                SavePng(dialog, Path.Combine(evidenceDirectory, "stored-data-dark.png"));
                dialog.DiscardForTest();
                check("stored-data-confirm-routes-snapshot-delete", center.Requests.LastOrDefault()?.Kind == ActionKind.DeleteSnapshot && center.Executed == 1);
                check("stored-data-refreshes-list-after-discard", dialog.ItemsForTest == 4 && dialog.UsageForTest.Contains("0/100", StringComparison.Ordinal));
                dialog.Width = 670; dialog.Height = 510; SavePng(dialog, Path.Combine(evidenceDirectory, "stored-data-resized.png"));
                dialog.Close();
            });
            window.PreviewOpenedForTest = dialog => { dialog.ConfirmCheck.IsChecked = true; dialog.ExecuteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); };
            await window.StoredDataForTestAsync(dialog =>
            {
                dialog.SelectForTest("startup"); dialog.UndoForTest();
                check("stored-data-startup-direct-backup-undo", center.Requests.LastOrDefault() is { Kind: ActionKind.Undo, TargetId: "startup" } && center.Executed == 2 && dialog.ItemsForTest == 3);
                dialog.Close();
            });
        }
        finally { window.Close(); center.Dispose(); }
    }
    private static async Task DrainAsync(Window window) => await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    private sealed class InteractionCenter : IControlCenter
    {
        internal sealed class Scan(string root, IProgress<ScanProgress>? progress, CancellationToken token)
        {
            internal IProgress<ScanProgress>? Progress { get; } = progress;
            internal CancellationToken Token { get; } = token;
            internal TaskCompletionSource<ScanReport> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal void Complete(List<Row> rows, bool cancelled = false) => Done.TrySetResult(new(root, rows, 0, 0, 0, 0, cancelled, DateTimeOffset.Now));
        }
        internal List<Scan> Scans { get; } = new();
        internal List<ActionRequest> Requests { get; } = new();
        internal int Executed { get; private set; }
        private readonly List<StoredDataItem> _storedItems = new() { new("environment", "environment", "Biến fixture", "Bản hoàn tác fixture, giá trị được giữ kín.", true, true), new("startup", "startup-run", "Startup fixture", "Mục startup đã tắt; hoàn tác để khôi phục.", false, true), new("startup-file", "startup-file", "Startup file fixture", "Bản file khởi động.", false, true), new("quarantine", "quarantine", "File fixture", "File phục hồi cần kiểm tra thủ công.", false, false), new("snapshot", "snapshot", "Ảnh fixture", "Ảnh cấu hình đã lưu.", true, false) };
        public AppSettings Settings { get; } = new();
        public Task SetDarkAsync(bool dark) => Task.CompletedTask;
        public Task<ModuleResult> ReadAsync(Module module, bool reveal, CancellationToken token) => Task.FromResult(new ModuleResult(module, new(), new(), ResultState.Empty));
        public Task<ScanReport> ScanAsync(string root, IProgress<ScanProgress>? progress, CancellationToken token) { var scan = new Scan(root, progress, token); Scans.Add(scan); return scan.Done.Task; }
        public Task<ActionPreview> PreviewAsync(ActionRequest request, CancellationToken token) { Requests.Add(request); return Task.FromResult(new ActionPreview("stored-data-ui-fixture", request.Kind, "Bỏ dữ liệu fixture", request.TargetId, "Có dữ liệu", "Không còn dữ liệu", "Chỉ dữ liệu giả trong bộ nhớ.", false, false, DateTimeOffset.Now.AddMinutes(2))); }
        public Task<ActionOutcome> ExecuteAsync(string token, CancellationToken cancellationToken) { Executed++; _storedItems.RemoveAll(item => item.Id == Requests[^1].TargetId); return Task.FromResult(new ActionOutcome("success", "Đã xử lý dữ liệu fixture.")); }
        public Task<StoredDataInventory> GetStoredDataAsync(CancellationToken token) => Task.FromResult(new StoredDataInventory(100, 100, _storedItems.ToList()));
        public Task<Snapshot> CreateSnapshotAsync(string name, CancellationToken token) => throw new NotSupportedException();
        public Task<List<SnapshotChange>> CompareSnapshotsAsync(string olderId, string newerId, CancellationToken token) => throw new NotSupportedException();
        public Task ExportSnapshotAsync(string id, string path, CancellationToken token) => throw new NotSupportedException();
        public Task ImportSnapshotAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task BackupAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task RestoreAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public void OpenWindowsTool(Module module) => throw new NotSupportedException();
        public void Dispose() { }
    }
    private static async Task LayoutAsync(Window window)
    { window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle); }
    public static void SavePng(Window window, string path)
    {
        window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
