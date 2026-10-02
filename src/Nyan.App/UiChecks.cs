using System.Diagnostics;
using System.IO;
using System.Windows;
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
