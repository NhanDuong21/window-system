using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Nyan.Core;

namespace Nyan.App;

public sealed class MainWindow : Window
{
    private readonly IControlCenter _app;
    private readonly bool _fixtureMode;
    private readonly ListBox _navigation = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _description = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _selection = new();
    private readonly TextBlock _details = new();
    private readonly TextBox _search = new();
    private readonly TextBlock _searchHint = new() { Text = "Tìm trong bảng…", IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 16, 0) };
    private readonly ComboBox _filter = new();
    private readonly CheckBox _reveal = new();
    private readonly WrapPanel _actions = new();
    private readonly Button _themeButton = new();
    private readonly Button _cancelButton = new() { Content = "Hủy tác vụ", Visibility = Visibility.Collapsed };
    private readonly DataGrid _table = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<Module, ModuleResult> _cache = new();
    private readonly List<Button> _actionButtons = new();
    private CancellationTokenSource? _readCts;
    private CancellationTokenSource? _taskCts;
    private ModuleResult? _result;
    private Module _module;
    private bool _dark, _closing, _loading, _taskRunning, _updatingFilter;
    private int _generation;
    private string? _sortKey, _filterKey, _baseRoot, _currentRoot;
    private ListSortDirection _sortDirection;

    internal static readonly Dictionary<Module, (string Title, string Description)> Pages = new()
    {
        [Module.Dashboard] = ("Tổng quan", "CPU theo khoảng lấy mẫu, bộ nhớ, ổ đĩa và Windows hiện tại."),
        [Module.Applications] = ("Ứng dụng", "Ứng dụng từ registry và gói Windows. Danh sách có thể chưa bao gồm mọi phần mềm."),
        [Module.Startup] = ("Khởi động cùng Windows", "Bật hoặc tắt nguồn được hỗ trợ. Scheduled task và service có chính sách riêng."),
        [Module.Processes] = ("Tiến trình", "CPU, RAM và PID. Kết thúc tiến trình có thể làm mất dữ liệu chưa lưu."),
        [Module.Storage] = ("Dung lượng", "Chọn thư mục local để quét. Không đọc nội dung file hoặc tải file chỉ có trên cloud."),
        [Module.Services] = ("Dịch vụ", "Trạng thái và kiểu khởi động. Thao tác giới hạn theo chính sách dịch vụ."),
        [Module.DevTools] = ("Công cụ phát triển", "Phiên bản và đường dẫn resolve của công cụ được phép kiểm tra."),
        [Module.Ports] = ("Cổng mạng", "Endpoint TCP/UDP và tiến trình sở hữu khi Windows cho phép đọc."),
        [Module.Environment] = ("Biến môi trường", "Phân biệt User, System và Process. Tiến trình đang mở có thể chưa nhận thay đổi."),
        [Module.Network] = ("Mạng", "Adapter, IP và DNS local. Dữ liệu thiếu không đồng nghĩa mất kết nối."),
        [Module.Snapshots] = ("Ảnh chụp cấu hình", "Lưu và so sánh inventory ứng dụng, startup và công cụ; không phải Windows Restore Point."),
        [Module.Cleanup] = ("Dọn file tạm", "Danh sách ứng viên theo chính sách. Chỉ file bạn chọn mới được đưa vào preview."),
        [Module.History] = ("Lịch sử thao tác", "Kết quả do Nyan thực hiện. Chỉ có hoàn tác khi backend cung cấp bản backup hợp lệ."),
        [Module.Settings] = ("Cài đặt", "Giao diện, backup và khôi phục dữ liệu ứng dụng local.")
    };

    public MainWindow(IControlCenter app, bool fixtureMode = false)
    {
        _app = app;
        _fixtureMode = fixtureMode;
        _dark = app.Settings.Dark;
        Title = "Nyan Control Center";
        Width = 1220; Height = 820; MinWidth = 920; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Apply(this, _dark);
        BuildLayout();
        _navigation.SelectionChanged += async (_, _) =>
        {
            if (_navigation.SelectedItem is ListBoxItem { Tag: Module module } && module != _module)
                await NavigateForTestAsync(module);
        };
        _search.TextChanged += (_, _) => { _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; ApplyFilter(); };
        _filter.SelectionChanged += (_, _) => { if (!_updatingFilter) ApplyFilter(); };
        _reveal.Checked += async (_, _) => await RefreshForTestAsync();
        _reveal.Unchecked += async (_, _) =>
        {
            // Previously revealed values must not survive masking in the table, detail pane or palette cache.
            _cache.Clear(); _result = null; _table.ItemsSource = null; _details.Text = "";
            await RefreshForTestAsync();
        };
        _table.SelectionChanged += (_, _) => UpdateSelection();
        _table.Sorting += SortTable;
        _table.MouseDoubleClick += async (_, _) =>
        {
            if (_module == Module.Storage && SelectedRow is { } row && IsDirectory(row))
                await ScanFolderAsync(Meta(row, "path"));
        };
        _cancelButton.Click += (_, _) => CancelWork();
        _themeButton.Click += async (_, _) => await ChangeThemeAsync();
        _timer.Tick += async (_, _) =>
        {
            if (IsActive && IsVisible && !_loading && !_taskRunning && _module is Module.Dashboard or Module.Processes)
                await RefreshForTestAsync();
        };
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            { e.Handled = true; await OpenPaletteAsync(); }
            else if (e.Key == Key.F5) { e.Handled = true; await RefreshForTestAsync(); }
            else if (e.Key == Key.Escape && (_loading || _taskRunning)) { e.Handled = true; CancelWork(); }
        };
        Loaded += async (_, _) => { await NavigateForTestAsync(Module.Dashboard); _timer.Start(); };
        Closed += (_, _) =>
        {
            _closing = true; _timer.Stop(); CancelWork(); _readCts?.Dispose(); _taskCts?.Dispose(); _app.Dispose();
        };
    }

    private void BuildLayout()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var side = new DockPanel { LastChildFill = true };
        side.SetResourceReference(Panel.BackgroundProperty, "SurfaceBrush");
        var brand = new StackPanel { Margin = new Thickness(20, 24, 16, 16) };
        brand.Children.Add(new TextBlock { Text = "Nyan", FontSize = 24, FontWeight = FontWeights.SemiBold });
        brand.Children.Add(Muted("Control Center · Windows local"));
        DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
        var footer = new TextBlock { Text = "Ctrl+K  Tìm nhanh\nF5  Làm mới", Margin = new Thickness(20, 16, 16, 20), LineHeight = 24 };
        footer.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(footer, Dock.Bottom); side.Children.Add(footer);
        _navigation.BorderThickness = new Thickness(0);
        _navigation.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        foreach (var (module, page) in Pages)
            _navigation.Items.Add(new ListBoxItem { Content = page.Title, Tag = module, ToolTip = page.Description });
        side.Children.Add(_navigation); root.Children.Add(side);
        var main = new Grid { Margin = new Thickness(24, 24, 24, 16) };
        Grid.SetColumn(main, 1); root.Children.Add(main);
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            main.RowDefinitions.Add(new RowDefinition { Height = height });
        var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        _title.FontSize = 26; _title.FontWeight = FontWeights.SemiBold; _title.TextWrapping = TextWrapping.Wrap;
        _description.TextWrapping = TextWrapping.Wrap; _description.Margin = new Thickness(0, 8, 16, 18);
        _description.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        titles.Children.Add(_title); titles.Children.Add(_description); heading.Children.Add(titles);
        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var palette = MakeButton("Tìm nhanh  Ctrl+K", OpenPaletteAsync); headerButtons.Children.Add(palette);
        _themeButton.Content = _dark ? "Giao diện sáng" : "Giao diện tối"; headerButtons.Children.Add(_themeButton);
        Grid.SetColumn(headerButtons, 1); heading.Children.Add(headerButtons); main.Children.Add(heading);
        var fixture = new TextBlock { Text = "CHẾ ĐỘ KIỂM THỬ · Dữ liệu fixture, không phải inventory máy thật", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Visibility = _fixtureMode ? Visibility.Visible : Visibility.Collapsed, FontWeight = FontWeights.SemiBold };
        fixture.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); Grid.SetRow(fixture, 1); main.Children.Add(fixture);
        Grid.SetRow(_actions, 2); main.Children.Add(_actions);
        var filters = new Grid { Margin = new Thickness(0, 4, 0, 12) };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search.ToolTip = "Tìm trong các giá trị đang hiển thị; không index nội dung file.";
        _search.Margin = new Thickness(0, 0, 12, 0); _search.MinWidth = 150;
        System.Windows.Automation.AutomationProperties.SetName(_search, "Tìm kiếm trong bảng");
        var searchBox = new Grid { Margin = new Thickness(0, 0, 12, 0) }; _search.Margin = new Thickness(0);
        _searchHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        searchBox.Children.Add(_search); searchBox.Children.Add(_searchHint); filters.Children.Add(searchBox);
        _filter.Margin = new Thickness(0, 0, 12, 0); _filter.ToolTip = "Lọc theo nguồn, phạm vi hoặc trạng thái";
        Grid.SetColumn(_filter, 1); filters.Children.Add(_filter);
        _reveal.Content = "Hiện dữ liệu nhạy cảm"; _reveal.VerticalAlignment = VerticalAlignment.Center;
        _reveal.ToolTip = "Chủ động hiển thị đường dẫn, giá trị biến và địa chỉ mạng trên màn này.";
        Grid.SetColumn(_reveal, 2); filters.Children.Add(_reveal); Grid.SetRow(filters, 3); main.Children.Add(filters);
        _table.IsReadOnly = true; _table.AutoGenerateColumns = false; _table.CanUserAddRows = false;
        _table.CanUserDeleteRows = false; _table.SelectionUnit = DataGridSelectionUnit.FullRow;
        _table.HeadersVisibility = DataGridHeadersVisibility.Column; _table.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _table.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "BorderBrush");
        _table.EnableRowVirtualization = true; _table.EnableColumnVirtualization = true;
        _table.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        _table.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        ScrollViewer.SetHorizontalScrollBarVisibility(_table, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_table, ScrollBarVisibility.Auto);
        Grid.SetRow(_table, 4); main.Children.Add(_table);
        var detailBox = new Expander { Header = "Chi tiết dòng được chọn", Content = _details, Margin = new Thickness(0, 10, 0, 0) };
        _details.TextWrapping = TextWrapping.Wrap; _details.Margin = new Thickness(8); _details.MaxHeight = 150;
        Grid.SetRow(detailBox, 5); main.Children.Add(detailBox);
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
        _selection.Margin = new Thickness(12, 0, 0, 0); _selection.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(_selection, Dock.Right); bottom.Children.Add(_selection);
        _status.TextWrapping = TextWrapping.Wrap; _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); bottom.Children.Add(_status);
        Grid.SetRow(bottom, 6); main.Children.Add(bottom); Content = root;
    }

    private static TextBlock Muted(string text)
    { var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap }; block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); return block; }
    private Button MakeButton(string text, Func<Task> action, bool danger = false)
    {
        var button = new Button { Content = text };
        if (danger) button.SetResourceReference(Control.ForegroundProperty, "DangerBrush");
        button.Click += async (_, _) => { if (!_taskRunning && !_closing) await RunUiAsync(action); };
        return button;
    }
    private void Action(string text, Func<Task> action, bool danger = false)
    { var button = MakeButton(text, action, danger); _actions.Children.Add(button); _actionButtons.Add(button); }

    private void BuildActions()
    {
        _actions.Children.Clear(); _actionButtons.Clear();
        Action("Làm mới  F5", RefreshForTestAsync);
        switch (_module)
        {
            case Module.Applications: WindowsTool(); break;
            case Module.Startup:
                Action("Bật mục đã chọn…", () => SelectedActionAsync(ActionKind.EnableStartup));
                Action("Tắt mục đã chọn…", () => SelectedActionAsync(ActionKind.DisableStartup), true); WindowsTool(); break;
            case Module.Processes: Action("Kết thúc tiến trình…", () => SelectedActionAsync(ActionKind.EndProcess), true); break;
            case Module.Storage:
                Action("Chọn thư mục để quét…", PickFolderAsync); Action("Mở thư mục đã chọn", DrillDownAsync); Action("Lên một cấp", UpAsync); break;
            case Module.Services:
                Action("Khởi chạy…", () => SelectedActionAsync(ActionKind.StartService));
                Action("Dừng…", () => SelectedActionAsync(ActionKind.StopService), true);
                Action("Khởi động lại…", () => SelectedActionAsync(ActionKind.RestartService), true); WindowsTool(); break;
            case Module.Ports:
                Action("Xem tiến trình", NavigateProcessAsync); Action("Kết thúc tiến trình sở hữu…", () => SelectedActionAsync(ActionKind.EndProcess), true); break;
            case Module.Environment:
                Action("Thêm biến…", () => EditEnvironmentAsync(null)); Action("Sửa biến đã chọn…", EditSelectedEnvironmentAsync);
                Action("Xóa biến đã chọn…", () => SelectedActionAsync(ActionKind.DeleteEnvironment), true); WindowsTool(); break;
            case Module.Network: WindowsTool(); break;
            case Module.Snapshots:
                Action("Tạo ảnh chụp…", CreateSnapshotAsync); Action("So sánh hai dòng đã chọn", CompareSnapshotsAsync);
                Action("Xuất ảnh chụp…", ExportSnapshotAsync); Action("Nhập ảnh chụp…", ImportSnapshotAsync); break;
            case Module.Cleanup: Action("Preview file đã chọn…", CleanupAsync, true); break;
            case Module.History: Action("Hoàn tác dòng đã chọn…", UndoAsync); break;
            case Module.Settings:
                Action("Backup dữ liệu app…", BackupAsync); Action("Khôi phục dữ liệu app…", RestoreAsync, true); break;
        }
        _actions.Children.Add(_cancelButton);
        _table.SelectionMode = _module is Module.Cleanup or Module.Snapshots ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single;
        _reveal.Visibility = _module is Module.Settings or Module.History or Module.Dashboard ? Visibility.Collapsed : Visibility.Visible;
    }
    private void WindowsTool() => Action("Mở công cụ Windows", () => { _app.OpenWindowsTool(_module); return Task.CompletedTask; });

    public async Task NavigateForTestAsync(Module module)
    {
        if (_closing) return;
        CancelWork(); _taskRunning = false; _generation++; _module = module; _result = null;
        _title.Text = Pages[module].Title; _description.Text = Pages[module].Description;
        _search.Text = ""; _sortKey = null; _details.Text = ""; _table.ItemsSource = null; _table.Columns.Clear();
        _updatingFilter = true; _filter.Items.Clear(); _filter.Items.Add("Tất cả"); _filter.SelectedIndex = 0; _updatingFilter = false;
        _navigation.SelectedItem = _navigation.Items.Cast<ListBoxItem>().First(x => (Module)x.Tag == module);
        BuildActions();
        await RefreshForTestAsync();
    }

    public async Task RefreshForTestAsync()
    {
        if (_closing || _taskRunning) return;
        if (_module == Module.Storage && _currentRoot is not null) { await RunUiAsync(() => ScanFolderAsync(_currentRoot)); return; }
        var generation = _generation; var module = _module;
        _readCts?.Cancel(); _readCts?.Dispose();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40)); _readCts = cts;
        _loading = true; UpdateBusy(); SetStatus("Đang tải dữ liệu…");
        try
        {
            var result = await _app.ReadAsync(module, _reveal.IsChecked == true, cts.Token);
            if (!_closing && generation == _generation && !cts.IsCancellationRequested)
                ShowResult(result);
        }
        catch (OperationCanceledException) { if (generation == _generation && !_closing) SetStatus("Đã hủy tải dữ liệu. Dùng Làm mới để thử lại."); }
        catch (Exception ex) { if (generation == _generation && !_closing) ShowError(ex); }
        finally
        {
            if (ReferenceEquals(_readCts, cts)) { _loading = false; UpdateBusy(); }
        }
    }

    private void ShowResult(ModuleResult result)
    {
        if (result.State is ResultState.Error or ResultState.Denied or ResultState.Cancelled && result.Rows.Count == 0 && _result is { Rows.Count: > 0 } old)
        {
            SetStatus($"Chưa cập nhật được dữ liệu · Bảng vẫn hiển thị dữ liệu lúc {old.Timestamp:HH:mm:ss dd/MM/yyyy}.\n{result.Message} · Dùng Làm mới để thử lại."); return;
        }
        var selected = _module == Module.Cleanup ? Array.Empty<string>() : _table.SelectedItems.Cast<Row>().Select(x => x.Id).ToArray();
        _result = result; _cache[result.Module] = result;
        var keys = _table.Columns.Select(x => x.SortMemberPath).ToArray();
        if (!keys.SequenceEqual(result.Columns.Select(x => x.Key)))
        {
            _table.Columns.Clear();
            foreach (var column in result.Columns)
            {
                var style = new Style(typeof(TextBlock));
                style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
                style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
                style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding($"Cells[{column.Key}]")));
                _table.Columns.Add(new DataGridTextColumn { Header = column.Title, Binding = new Binding($"Cells[{column.Key}]"), SortMemberPath = column.Key, ElementStyle = style, MinWidth = 100, Width = new DataGridLength(IsLongColumn(column.Key) ? 270 : 150) });
            }
        }
        UpdateFilterOptions(); ApplyFilter();
        foreach (var row in _table.Items.Cast<Row>().Where(x => selected.Contains(x.Id)))
        {
            if (_table.SelectionMode == DataGridSelectionMode.Single) { _table.SelectedItem = row; break; }
            _table.SelectedItems.Add(row);
        }
        if (_module == Module.Cleanup) _table.UnselectAll();
        var state = result.State switch { ResultState.Partial => "Dữ liệu một phần", ResultState.Empty => "Chưa có dữ liệu", ResultState.Denied => "Thiếu quyền truy cập", ResultState.Error => "Không đọc được dữ liệu", ResultState.Cancelled => "Đã hủy", _ => "Đã cập nhật" };
        SetStatus($"{state} · {result.Rows.Count:N0} dòng · {result.Timestamp:HH:mm:ss dd/MM/yyyy}" + (string.IsNullOrWhiteSpace(result.Message) ? "" : $"\n{result.Message}"));
        UpdateSelection();
    }

    private void UpdateFilterOptions()
    {
        if (_result is null) return;
        var previous = _filter.SelectedItem as string;
        _filterKey = _result.Columns.Select(x => x.Key).FirstOrDefault(x => new[] { "source", "scope", "state", "status", "kind" }.Contains(x.ToLowerInvariant()));
        _updatingFilter = true; _filter.Items.Clear(); _filter.Items.Add("Tất cả");
        if (_filterKey is not null)
            foreach (var value in _result.Rows.Select(x => Cell(x, _filterKey)).Distinct().OrderBy(x => x).Take(60)) _filter.Items.Add(value);
        _filter.SelectedItem = _filter.Items.Contains(previous) ? previous : "Tất cả";
        _filter.IsEnabled = _filter.Items.Count > 1; _updatingFilter = false;
    }

    private void ApplyFilter()
    {
        if (_result is null) return;
        var text = _search.Text.Trim(); var filter = _filter.SelectedItem as string;
        IEnumerable<Row> rows = _result.Rows;
        if (text.Length > 0) rows = rows.Where(x => x.Cells.Values.Any(v => v.Contains(text, StringComparison.CurrentCultureIgnoreCase)));
        if (_filterKey is not null && filter is not null && filter != "Tất cả") rows = rows.Where(x => Cell(x, _filterKey) == filter);
        if (_sortKey is not null)
            rows = _sortDirection == ListSortDirection.Ascending ? rows.OrderBy(x => Cell(x, _sortKey), CellComparer.Instance) : rows.OrderByDescending(x => Cell(x, _sortKey), CellComparer.Instance);
        _table.ItemsSource = rows.ToList(); UpdateSelection();
    }

    private void SortTable(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true; _sortKey = e.Column.SortMemberPath;
        _sortDirection = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in _table.Columns) column.SortDirection = null;
        e.Column.SortDirection = _sortDirection; ApplyFilter();
    }
    private sealed class CellComparer : IComparer<string>
    {
        internal static readonly CellComparer Instance = new();
        public int Compare(string? left, string? right)
        {
            if (NumericValue(left, out var a) && NumericValue(right, out var b)) return a.CompareTo(b);
            return StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
        }
        private static bool NumericValue(string? text, out double number)
        {
            number = 0; if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2 || !double.TryParse(parts[0], NumberStyles.Number, parts[0].Contains(',') ? CultureInfo.GetCultureInfo("vi-VN") : CultureInfo.InvariantCulture, out number)) return false;
            if (parts.Length == 1) return true;
            var power = parts[1] switch { "B" or "byte" or "%" => 0, "KiB" => 1, "MiB" => 2, "GiB" => 3, "TiB" => 4, _ => -1 };
            if (power < 0) return false; number *= Math.Pow(1024, power); return true;
        }
    }
    private static bool IsLongColumn(string key) => new[] { "path", "value", "address", "message", "before", "after", "target" }.Contains(key.ToLowerInvariant());
    private Row? SelectedRow => _table.SelectedItem as Row;
    private static string Meta(Row row, string key) => row.Data?.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? "";
    private static string Cell(Row row, string key) => row.Cells.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? "—";
    private void UpdateSelection()
    {
        _selection.Text = $"{_table.Items.Count:N0} hiển thị · {_table.SelectedItems.Count:N0} chọn";
        _details.Text = SelectedRow is { } row && _result is not null ? string.Join("\n", _result.Columns.Select(c => $"{c.Title}: {Cell(row, c.Key)}")) : "Chọn một dòng để xem giá trị đang hiển thị.";
    }
    private void SetStatus(string message) => _status.Text = message;
    private void ShowError(Exception exception)
    {
        SetStatus(exception is AppException known ? $"{known.Message}\nMã: {known.Code}. Bạn có thể làm mới hoặc thử lại." : $"Tác vụ không hoàn thành ({exception.GetType().Name}). Bạn có thể làm mới hoặc thử lại.");
    }
    private void UpdateBusy()
    {
        if (_closing) return;
        _cancelButton.Visibility = _loading || _taskRunning ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in _actionButtons) button.IsEnabled = !_loading && !_taskRunning;
        _reveal.IsEnabled = !_loading && !_taskRunning;
    }
    private void CancelWork() { _readCts?.Cancel(); _taskCts?.Cancel(); }
    private async Task RunUiAsync(Func<Task> action)
    { try { await action(); } catch (OperationCanceledException) { SetStatus("Đã hủy tác vụ."); } catch (Exception ex) { ShowError(ex); } }

    private async Task SelectedActionAsync(ActionKind kind)
    {
        if (SelectedRow is not { } row) { SetStatus("Chọn một dòng trước khi thao tác."); return; }
        if (_module == Module.Startup && Meta(row, "supported").Equals("false", StringComparison.OrdinalIgnoreCase))
        { SetStatus("Nguồn này chỉ đọc. Mở công cụ Windows để quản lý đúng loại nguồn."); return; }
        if (_module == Module.Environment && Meta(row, "scope").Equals("Process", StringComparison.OrdinalIgnoreCase))
        { SetStatus("Phạm vi Process chỉ đọc. Thay đổi User hoặc System cho các tiến trình mở sau."); return; }
        var target = row.Id;
        if (string.IsNullOrWhiteSpace(target)) { SetStatus("Không xác định được tiến trình sở hữu của dòng này."); return; }
        var values = kind == ActionKind.DeleteEnvironment ? new Dictionary<string, string> { ["scope"] = Meta(row, "scope"), ["name"] = Meta(row, "name") } : null;
        await PreviewAndExecuteAsync(new ActionRequest(kind, target, values));
    }

    private async Task PreviewAndExecuteAsync(ActionRequest request)
    {
        _taskRunning = true; _taskCts?.Dispose(); _taskCts = new CancellationTokenSource(TimeSpan.FromMinutes(3)); UpdateBusy();
        var token = _taskCts.Token;
        try
        {
            SetStatus("Đang kiểm tra đối tượng và tạo preview…");
            var preview = await _app.PreviewAsync(request, token);
            var dialog = new PreviewDialog(preview, _dark) { Owner = this };
            if (dialog.ShowDialog() != true)
            { SetStatus("Đã hủy preview. Chưa gửi yêu cầu thực thi."); return; }
            token.ThrowIfCancellationRequested();
            SetStatus("Đang thực thi và kiểm tra lại kết quả…");
            var outcome = await _app.ExecuteAsync(preview.Token, token);
            SetStatus($"{outcome.Status}: {outcome.Message}\nThành công: {outcome.Succeeded:N0} · Không hoàn thành: {outcome.Failed:N0}");
            _taskRunning = false; await RefreshForTestAsync();
            SetStatus($"{outcome.Status}: {outcome.Message}\nThành công: {outcome.Succeeded:N0} · Không hoàn thành: {outcome.Failed:N0} · {DateTime.Now:HH:mm:ss}");
        }
        finally { _taskRunning = false; UpdateBusy(); }
    }

    private async Task ChangeThemeAsync()
    {
        var next = !_dark;
        try { await _app.SetDarkAsync(next); _dark = next; Theme.Apply(this, _dark); _themeButton.Content = _dark ? "Giao diện sáng" : "Giao diện tối"; }
        catch (Exception ex) { ShowError(ex); }
    }
    public bool DarkForTest => _dark;
    public void ToggleThemeForTest() => _ = ChangeThemeAsync();
    public Task ToggleThemeForTestAsync() => ChangeThemeAsync();

    private async Task PickFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Chọn thư mục local để phân tích dung lượng", Multiselect = false };
        if (dialog.ShowDialog(this) == true) { _baseRoot = dialog.FolderName; await ScanFolderAsync(dialog.FolderName); }
    }
    private static bool IsDirectory(Row row) => Meta(row, "kind").Equals("directory", StringComparison.OrdinalIgnoreCase) || Meta(row, "directory").Equals("true", StringComparison.OrdinalIgnoreCase);
    private async Task DrillDownAsync()
    {
        if (SelectedRow is { } row && IsDirectory(row)) await ScanFolderAsync(Meta(row, "path"));
        else SetStatus("Chọn một thư mục trong kết quả để mở sâu hơn.");
    }
    private async Task UpAsync()
    {
        if (_currentRoot is null || _baseRoot is null || Path.GetFullPath(_currentRoot).TrimEnd('\\').Equals(Path.GetFullPath(_baseRoot).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        { SetStatus("Bạn đang ở thư mục gốc đã chọn."); return; }
        var parent = Directory.GetParent(_currentRoot)?.FullName;
        if (parent is not null && IsWithinBase(parent)) await ScanFolderAsync(parent);
    }
    private bool IsWithinBase(string path)
    {
        if (_baseRoot is null) return false;
        var root = Path.GetFullPath(_baseRoot).TrimEnd('\\') + "\\";
        var full = Path.GetFullPath(path).TrimEnd('\\') + "\\";
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
    private async Task ScanFolderAsync(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) { SetStatus("Thư mục không có đường dẫn được phép đọc."); return; }
        if (!IsWithinBase(root)) { SetStatus("Thư mục nằm ngoài phạm vi gốc đã chọn."); return; }
        _readCts?.Cancel(); _taskCts?.Dispose(); _taskCts = new CancellationTokenSource();
        _taskRunning = true; UpdateBusy(); var generation = _generation;
        try
        {
            SetStatus("Đang quét thư mục…");
            var progress = new Progress<ScanProgress>(p => { if (!_closing && generation == _generation) SetStatus($"Đang quét · {p.Files:N0} file · {FormatBytes(p.Bytes)} · Bỏ qua: {p.Skipped:N0}\n{p.Status}"); });
            var report = await _app.ScanAsync(root, progress, _taskCts.Token);
            if (!_closing && generation == _generation)
            {
                _currentRoot = root;
                var columns = new List<Column> { new("name", "Tên"), new("type", "Loại"), new("size", "Kích thước logic"), new("allocated", "Chiếm đĩa"), new("path", "Đường dẫn") };
                var rows = _reveal.IsChecked == true ? report.Rows.Select(row => row with { Cells = new Dictionary<string, string>(row.Cells) { ["path"] = Meta(row, "path") } }).ToList() : report.Rows;
                ShowResult(new ModuleResult(Module.Storage, columns, rows, report.Cancelled || report.Skipped > 0 ? ResultState.Partial : report.Rows.Count == 0 ? ResultState.Empty : ResultState.Ready,
                    $"{report.Files:N0} file · {FormatBytes(report.Bytes)} logic · Chiếm đĩa: {(report.AllocatedBytes is { } allocated ? FormatBytes(allocated) : "chưa đọc được")} · Bỏ qua: {report.Skipped:N0}" + (report.Cancelled ? " · Đã hủy, giữ kết quả một phần" : ""), report.CompletedAt));
            }
        }
        finally { if (generation == _generation) { _taskRunning = false; UpdateBusy(); } }
    }
    private static string FormatBytes(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):N2} GiB" : bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):N2} MiB" : $"{bytes:N0} byte";

    private async Task NavigateProcessAsync()
    {
        if (SelectedRow is not { } row || string.IsNullOrWhiteSpace(Meta(row, "pid"))) { SetStatus("Chọn endpoint có PID đọc được."); return; }
        var pid = Meta(row, "pid"); await NavigateForTestAsync(Module.Processes);
        var process = _table.Items.Cast<Row>().FirstOrDefault(x => Meta(x, "pid") == pid || x.Id == pid);
        if (process is not null) { _table.SelectedItem = process; _table.ScrollIntoView(process); }
        else SetStatus("Tiến trình đã kết thúc hoặc không còn đọc được. Bạn có thể làm mới.");
    }

    private async Task EditSelectedEnvironmentAsync()
    {
        if (SelectedRow is not { } row) { SetStatus("Chọn biến cần sửa."); return; }
        await EditEnvironmentAsync(row);
    }
    private async Task EditEnvironmentAsync(Row? row)
    {
        if (row is not null && Meta(row, "scope").Equals("Process", StringComparison.OrdinalIgnoreCase)) { SetStatus("Phạm vi Process chỉ đọc."); return; }
        var dialog = new EnvironmentDialog(row, _dark) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var values = new Dictionary<string, string> { ["name"] = dialog.VariableName, ["scope"] = dialog.Scope, ["value"] = dialog.Value };
        await PreviewAndExecuteAsync(new ActionRequest(ActionKind.SetEnvironment, row?.Id ?? $"{dialog.Scope}:{dialog.VariableName}", values));
    }
    private async Task CleanupAsync()
    {
        var ids = _table.SelectedItems.Cast<Row>().Select(x => x.Id).ToList();
        if (ids.Count == 0) { SetStatus("Chọn riêng các file bạn muốn dọn. Mặc định không file nào được chọn."); return; }
        await PreviewAndExecuteAsync(new ActionRequest(ActionKind.CleanupFiles, "selected-temp-files", SelectedIds: ids));
    }
    private async Task UndoAsync()
    {
        if (SelectedRow is not { } row || string.IsNullOrWhiteSpace(Meta(row, "undoId"))) { SetStatus("Dòng này không có bản hoàn tác hợp lệ."); return; }
        await PreviewAndExecuteAsync(new ActionRequest(ActionKind.Undo, Meta(row, "undoId")));
    }

    private async Task CreateSnapshotAsync()
    {
        var name = AskText("Tạo ảnh chụp cấu hình", "Tên để dễ nhận biết", $"Cấu hình {DateTime.Now:dd/MM/yyyy HH:mm}", 120);
        if (name is null) return;
        await AppTaskAsync(async token => { await _app.CreateSnapshotAsync(name, token); }, "Đang tạo ảnh chụp cấu hình…", "Đã lưu ảnh chụp cấu hình.");
    }
    private async Task CompareSnapshotsAsync()
    {
        var rows = _table.SelectedItems.Cast<Row>().ToList();
        if (rows.Count != 2) { SetStatus("Giữ Ctrl và chọn đúng hai ảnh chụp để so sánh."); return; }
        var ordered = rows.OrderBy(x => DateTimeOffset.TryParse(Meta(x, "at"), out var at) ? at : DateTimeOffset.TryParse(Cell(x, "time"), out at) ? at : DateTimeOffset.MinValue).ToList();
        await AppTaskAsync(async token =>
        {
            var changes = await _app.CompareSnapshotsAsync(ordered[0].Id, ordered[1].Id, token);
            ShowComparison(changes);
        }, "Đang so sánh…", "Đã so sánh hai ảnh chụp.", refresh: false);
    }
    private void ShowComparison(List<SnapshotChange> changes)
    {
        var window = DialogWindow("So sánh ảnh chụp cấu hình", 1000, 650);
        var panel = new DockPanel { Margin = new Thickness(20) };
        var close = new Button { Content = "Đóng", IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => window.Close(); DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        var note = new TextBlock { Text = changes.Count == 0 ? "Không có thay đổi trong phạm vi đã chụp." : $"{changes.Count:N0} thay đổi · Cột Trước thuộc ảnh cũ, cột Sau thuộc ảnh mới.", Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
        var grid = new DataGrid { ItemsSource = changes, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, EnableRowVirtualization = true };
        foreach (var (key, title) in new[] { ("Module", "Module"), ("Kind", "Thay đổi"), ("Key", "Đối tượng"), ("Before", "Trước"), ("After", "Sau") })
            grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(key), Width = key is "Before" or "After" ? 280 : 150 });
        panel.Children.Add(grid); window.Content = panel; window.ShowDialog();
    }
    private async Task ExportSnapshotAsync()
    {
        if (SelectedRow is not { } row) { SetStatus("Chọn ảnh chụp cần xuất."); return; }
        var file = new SaveFileDialog { Title = "Xuất ảnh chụp được bảo vệ", Filter = "Ảnh chụp Nyan (*.nccsnapshot)|*.nccsnapshot", FileName = "cau-hinh.nccsnapshot" };
        if (file.ShowDialog(this) == true) await AppTaskAsync(token => _app.ExportSnapshotAsync(row.Id, file.FileName, token), "Đang xuất…", "Đã xuất ảnh chụp. Dữ liệu bảo vệ có thể gắn với tài khoản Windows này.", false);
    }
    private async Task ImportSnapshotAsync()
    {
        var file = new OpenFileDialog { Title = "Nhập ảnh chụp cấu hình", Filter = "Ảnh chụp Nyan (*.nccsnapshot)|*.nccsnapshot" };
        if (file.ShowDialog(this) == true) await AppTaskAsync(token => _app.ImportSnapshotAsync(file.FileName, token), "Đang validate và nhập…", "Đã nhập ảnh chụp cấu hình.");
    }
    private async Task BackupAsync()
    {
        var file = new SaveFileDialog { Title = "Backup dữ liệu Nyan", Filter = "Backup Nyan (*.nccbackup)|*.nccbackup", FileName = "nyan-data.nccbackup" };
        if (file.ShowDialog(this) == true) await AppTaskAsync(token => _app.BackupAsync(file.FileName, token), "Đang backup…", "Đã backup dữ liệu ứng dụng; đây không phải backup Windows.", false);
    }
    private async Task RestoreAsync()
    {
        var file = new OpenFileDialog { Title = "Chọn backup dữ liệu Nyan", Filter = "Backup Nyan (*.nccbackup)|*.nccbackup" };
        if (file.ShowDialog(this) != true) return;
        if (!Confirm("Khôi phục dữ liệu ứng dụng", "Dữ liệu Nyan hiện tại sẽ được thay bằng backup sau khi validate. Thao tác này không khôi phục cấu hình Windows. Bạn cần xác nhận riêng trước khi tiếp tục.")) return;
        await AppTaskAsync(async token => { await _app.RestoreAsync(file.FileName, token); _dark = _app.Settings.Dark; Theme.Apply(this, _dark); _themeButton.Content = _dark ? "Giao diện sáng" : "Giao diện tối"; }, "Đang validate và khôi phục…", "Đã khôi phục dữ liệu Nyan.");
    }
    private async Task AppTaskAsync(Func<CancellationToken, Task> action, string loading, string done, bool refresh = true)
    {
        _taskCts?.Dispose(); _taskCts = new CancellationTokenSource(TimeSpan.FromMinutes(3)); _taskRunning = true; UpdateBusy();
        try { SetStatus(loading); await action(_taskCts.Token); _taskRunning = false; if (refresh) await RefreshForTestAsync(); SetStatus(done); }
        finally { _taskRunning = false; UpdateBusy(); }
    }

    private Window DialogWindow(string title, double width, double height)
    { var window = new Window { Owner = this, Title = title, Width = width, Height = height, MinWidth = Math.Min(width, 500), MinHeight = Math.Min(height, 280), WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false }; Theme.Apply(window, _dark); return window; }
    private string? AskText(string title, string label, string value, int maximum)
    {
        var window = DialogWindow(title, 520, 250); var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { Text = value, MaxLength = maximum, Margin = new Thickness(0, 0, 0, 20) }; panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Hủy", IsCancel = true, IsDefault = true }; var save = new Button { Content = "Tạo" };
        cancel.Click += (_, _) => window.DialogResult = false; save.Click += (_, _) => { if (input.Text.Trim().Length > 0) window.DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons); window.Content = panel;
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
    private bool Confirm(string title, string description)
    {
        var preview = new ActionPreview("app-data-confirm", ActionKind.Undo, title, "Dữ liệu ứng dụng Nyan", "Dữ liệu local hiện tại", "Dữ liệu từ file backup đã chọn", description, false, false, DateTimeOffset.Now.AddMinutes(3));
        return new PreviewDialog(preview, _dark) { Owner = this }.ShowDialog() == true;
    }

    private Task OpenPaletteAsync() => OpenPaletteCoreAsync(null);
    private async Task OpenPaletteCoreAsync(Action<Window, TextBox, ListBox>? test)
    {
        var window = DialogWindow("Tìm nhanh · Ctrl+K", 720, 570);
        var root = new DockPanel { Margin = new Thickness(20) };
        var input = new TextBox { Margin = new Thickness(0, 0, 0, 12), ToolTip = "Tìm trang hoặc dữ liệu đang có trong bộ nhớ." };
        System.Windows.Automation.AutomationProperties.SetName(input, "Tìm nhanh"); DockPanel.SetDock(input, Dock.Top); root.Children.Add(input);
        var note = new TextBlock { Text = "Chỉ điều hướng và chọn dòng. Thao tác hệ thống luôn cần preview riêng.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(note, Dock.Bottom); root.Children.Add(note);
        var list = new ListBox { DisplayMemberPath = nameof(PaletteEntry.Label) }; root.Children.Add(list); window.Content = root;
        var entries = Pages.Select(x => new PaletteEntry(x.Key, null, x.Value.Title + " · " + x.Value.Description)).ToList();
        foreach (var cached in _cache)
            foreach (var row in cached.Value.Rows.Take(20000))
                entries.Add(new PaletteEntry(cached.Key, row.Id, Pages[cached.Key].Title + " › " + string.Join(" · ", row.Cells.Values.Take(3))));
        void FilterPalette()
        {
            var value = input.Text.Trim(); var hits = entries.Where(x => value.Length == 0 ? x.RowId is null : x.Label.Contains(value, StringComparison.CurrentCultureIgnoreCase)).Take(80).ToList();
            list.ItemsSource = hits; list.SelectedIndex = hits.Count > 0 ? 0 : -1;
            note.Text = hits.Count == 0 ? "Không có kết quả trong trang và dữ liệu đã tải." : "Enter để mở · Esc để đóng. Thao tác hệ thống cần preview riêng.";
        }
        PaletteEntry? chosen = null;
        void Choose() { if (list.SelectedItem is PaletteEntry entry) { chosen = entry; window.DialogResult = true; } }
        input.TextChanged += (_, _) => FilterPalette();
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { list.Focus(); if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0; e.Handled = true; }
            if (e.Key == Key.Enter) { Choose(); e.Handled = true; }
        };
        list.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Choose(); e.Handled = true; } };
        list.MouseDoubleClick += (_, _) => Choose();
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { window.DialogResult = false; e.Handled = true; } };
        FilterPalette(); window.Loaded += (_, _) => { input.Focus(); test?.Invoke(window, input, list); };
        if (window.ShowDialog() == true && chosen is not null)
        {
            await NavigateForTestAsync(chosen.Module);
            if (chosen.RowId is not null)
            {
                var row = _table.Items.Cast<Row>().FirstOrDefault(x => x.Id == chosen.RowId);
                if (row is not null) { _table.SelectedItem = row; _table.ScrollIntoView(row); }
                else SetStatus("Đối tượng không còn trong kết quả mới nhất.");
            }
        }
    }
    private sealed record PaletteEntry(Module Module, string? RowId, string Label);

    internal int VisibleRowsForTest => _table.Items.Count;
    internal int SelectedRowsForTest => _table.SelectedItems.Count;
    internal bool RevealForTest => _reveal.IsChecked == true;
    internal string StatusForTest => _status.Text;
    internal Module CurrentModuleForTest => _module;
    internal void FilterForTest(string text) => _search.Text = text;
    internal void ShowResultForTest(ModuleResult result) => ShowResult(result);
    internal bool VirtualizedForTest => _table.EnableRowVirtualization && _table.EnableColumnVirtualization;
    internal bool PersistedDarkForTest => _app.Settings.Dark;
    internal bool FixtureModeForTest => _fixtureMode;
    internal string? CurrentRootForTest => _currentRoot;
    internal void CancelForTest() => CancelWork();
    internal Task PaletteForTestAsync(Action<Window, TextBox, ListBox> test) => OpenPaletteCoreAsync(test);
    internal async Task SetRevealForTestAsync(bool reveal) { _reveal.IsChecked = reveal; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
    internal bool CachedContainsForTest(string value) => _cache.Values.SelectMany(x => x.Rows).Any(row => row.Cells.Values.Any(cell => cell.Contains(value, StringComparison.Ordinal)));
    internal async Task DrillDownForTestAsync() { _table.SelectedItem = _table.Items.Cast<Row>().FirstOrDefault(row => IsDirectory(row) && !Meta(row, "path").Equals(_currentRoot, StringComparison.OrdinalIgnoreCase)); await DrillDownAsync(); }
    internal async Task ScanForTestAsync(string root) { _baseRoot = root; await ScanFolderAsync(root); }
}

internal sealed class PreviewDialog : Window
{
    internal readonly Button ExecuteButton;
    internal readonly Button CancelButton;
    internal readonly CheckBox ConfirmCheck;
    internal PreviewDialog(ActionPreview preview, bool dark)
    {
        Title = "Preview và xác nhận"; Width = 700; Height = 630; MinWidth = 540; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Theme.Apply(this, dark);
        var root = new DockPanel { Margin = new Thickness(24) };
        var bottom = new StackPanel();
        ConfirmCheck = new CheckBox { Content = "Tôi đã đọc preview và xác nhận đúng đối tượng, phạm vi.", Margin = new Thickness(0, 12, 0, 16) };
        bottom.Children.Add(ConfirmCheck);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        CancelButton = new Button { Content = "Hủy", IsCancel = true, IsDefault = true };
        ExecuteButton = new Button { Content = preview.RequiresElevation ? "Xác nhận và yêu cầu quyền Windows" : "Xác nhận thực thi", IsEnabled = false };
        ExecuteButton.SetResourceReference(Control.ForegroundProperty, "DangerBrush");
        buttons.Children.Add(CancelButton); buttons.Children.Add(ExecuteButton); bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = preview.Title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        Add("Đối tượng / phạm vi", preview.Target); Add("Trước", preview.Before); Add("Sau", preview.After); Add("Cảnh báo", preview.Warning);
        Add("Hoàn tác", preview.CanUndo ? "Có bản backup để yêu cầu hoàn tác sau khi thành công." : "Thao tác này không có hoàn tác được bảo đảm.");
        Add("Quyền và thời hạn", (preview.RequiresElevation ? "Windows sẽ yêu cầu quyền cao cho action hẹp.\n" : "Chạy với quyền hiện tại.\n") + $"Preview hết hạn lúc {preview.ExpiresAt:HH:mm:ss dd/MM/yyyy}.");
        void Add(string heading, string value)
        {
            content.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
            var text = new TextBox { Text = string.IsNullOrWhiteSpace(value) ? "—" : value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            content.Children.Add(text);
        }
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
        ConfirmCheck.Checked += (_, _) => ExecuteButton.IsEnabled = DateTimeOffset.Now < preview.ExpiresAt;
        ConfirmCheck.Unchecked += (_, _) => ExecuteButton.IsEnabled = false;
        CancelButton.Click += (_, _) => DialogResult = false;
        ExecuteButton.Click += (_, _) => { if (ConfirmCheck.IsChecked == true && DateTimeOffset.Now < preview.ExpiresAt) DialogResult = true; };
        Loaded += (_, _) => CancelButton.Focus();
    }
}

internal sealed class EnvironmentDialog : Window
{
    private readonly TextBox _name, _value;
    private readonly ComboBox _scope;
    internal string VariableName => _name.Text;
    internal string Value => _value.Text;
    internal string Scope => _scope.SelectedIndex == 1 ? "Machine" : "User";
    internal EnvironmentDialog(Row? row, bool dark)
    {
        Title = row is null ? "Thêm biến môi trường" : "Sửa biến môi trường"; Width = 680; Height = 510; MinWidth = 540; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Theme.Apply(this, dark);
        string Data(string key) => row?.Data?.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? "";
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "Tên biến", Margin = new Thickness(0, 0, 0, 6) });
        _name = new TextBox { Text = Data("name"), IsReadOnly = row is not null, MaxLength = 255, Margin = new Thickness(0, 0, 0, 12) }; root.Children.Add(_name);
        root.Children.Add(new TextBlock { Text = "Phạm vi", Margin = new Thickness(0, 0, 0, 6) });
        _scope = new ComboBox { ItemsSource = new[] { "User · Người dùng hiện tại", "System · Cần quyền cao" }, SelectedIndex = Data("scope").Equals("Machine", StringComparison.OrdinalIgnoreCase) ? 1 : 0, IsEnabled = row is null, Margin = new Thickness(0, 0, 0, 12) }; root.Children.Add(_scope);
        root.Children.Add(new TextBlock { Text = "Giá trị nguyên gốc · Nội dung có thể nhạy cảm", Margin = new Thickness(0, 0, 0, 6) });
        _value = new TextBox { Text = Data("value"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 32760, Margin = new Thickness(0, 0, 0, 12) }; root.Children.Add(_value);
        root.Children.Add(new TextBlock { Text = "PATH: giữ nguyên thứ tự, ngăn cách entry bằng dấu ;. Không xóa entry chỉ vì thư mục hiện chưa tồn tại. Tiến trình đang mở có thể cần mở lại để nhận thay đổi.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Hủy", IsCancel = true, IsDefault = true }; var preview = new Button { Content = "Tạo preview…" };
        cancel.Click += (_, _) => DialogResult = false;
        preview.Click += (_, _) => { if (_name.Text.Length > 0 && !_name.Text.Contains('=') && !_name.Text.Contains('\0')) DialogResult = true; else _name.Focus(); };
        buttons.Children.Add(cancel); buttons.Children.Add(preview); root.Children.Add(buttons); Content = root;
        Loaded += (_, _) => cancel.Focus();
    }
}
