using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Nyan.Core;

/// <summary>Read-only local Windows inventory. No mutation or arbitrary command endpoint.</summary>
public sealed class WindowsReader : IWindowsReader, IDisposable
{
    private readonly ReadProcess _commands = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _hardwareGate = new(1, 1);
    private List<(string Name, string Value)> _hardware = new();
    private DateTimeOffset _hardwareAt = DateTimeOffset.MinValue;
    private bool _hardwareUnavailable;
    private bool _disposed;

    public Task<ModuleResult> ReadAsync(Module module, bool reveal, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsWindows()) return ModuleResult.Failure(module, "Nguồn dữ liệu này yêu cầu Windows native.");
                return module switch
                {
                    Module.Dashboard => await DashboardAsync(reveal, linked.Token).ConfigureAwait(false),
                    Module.Applications => await ApplicationsAsync(reveal, linked.Token).ConfigureAwait(false),
                    Module.Startup => await StartupAsync(reveal, linked.Token).ConfigureAwait(false),
                    Module.Processes => await ProcessesAsync(linked.Token).ConfigureAwait(false),
                    Module.Services => ReadServices(reveal, linked.Token),
                    Module.DevTools => await DevToolsAsync(reveal, linked.Token).ConfigureAwait(false),
                    Module.Ports => await PortsAsync(reveal, linked.Token).ConfigureAwait(false),
                    Module.Environment => ReadEnvironment(reveal, linked.Token),
                    Module.Network => ReadNetwork(reveal, linked.Token),
                    _ => ModuleResult.Failure(module, "Module này do lớp ứng dụng hoặc lưu trữ cung cấp.")
                };
            }
            catch (OperationCanceledException) { return ModuleResult.Failure(module, "Đã hủy đọc dữ liệu.", ResultState.Cancelled); }
            catch (UnauthorizedAccessException) { return ModuleResult.Failure(module, "Không có quyền đọc nguồn dữ liệu này.", ResultState.Denied); }
            catch (System.ComponentModel.Win32Exception e) { return ModuleResult.Failure(module, "Windows không cho phép đọc nguồn dữ liệu native này (mã " + e.NativeErrorCode + ").", e.NativeErrorCode == 5 ? ResultState.Denied : ResultState.Error); }
            catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or System.Security.SecurityException)
            { return ModuleResult.Failure(module, "Không đọc được dữ liệu Windows. Thử tải lại; chi tiết nhạy cảm không được ghi vào log."); }
        }, CancellationToken.None);
    }

    private async Task<ModuleResult> DashboardAsync(bool reveal, CancellationToken token)
    {
        var rows = new List<Row>();
        var first = TrySystemTimes(out var idle1, out var kernel1, out var user1);
        await Task.Delay(400, token).ConfigureAwait(false);
        var second = TrySystemTimes(out var idle2, out var kernel2, out var user2);
        var total = kernel2 - kernel1 + user2 - user1;
        var cpu = first && second && total > 0 && idle2 >= idle1
            ? Math.Clamp(100.0 * (total - (idle2 - idle1)) / total, 0, 100).ToString("0.0", CultureInfo.InvariantCulture) + " %"
            : "Không đọc được";
        rows.Add(Metric("cpu", "CPU toàn máy", cpu, "Lấy mẫu hai lần, cách nhau 400 ms"));
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var ramOk = GlobalMemoryStatusEx(ref memory);
        rows.Add(Metric("ram", "RAM đang dùng / tổng", ramOk ? $"{Bytes(memory.TotalPhysical - memory.AvailablePhysical)} / {Bytes(memory.TotalPhysical)}" : "Không đọc được", "RAM vật lý"));
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        rows.Add(Metric("uptime", "Thời gian hoạt động", $"{uptime.Days} ngày {uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}", "Từ lần khởi động Windows gần nhất"));
        rows.Add(Metric("architecture", "Kiến trúc", RuntimeInformation.OSArchitecture + " • " + Environment.ProcessorCount + " CPU logic", "CPU logic được Windows cung cấp"));
        rows.Add(Metric("windows", "Windows / build", Environment.OSVersion.Version.ToString(), "Phiên bản Windows native"));
        var denied = 0;
        foreach (var drive in DriveInfo.GetDrives())
        {
            token.ThrowIfCancellationRequested();
            if (drive.DriveType == DriveType.Network) continue;
            try
            {
                if (!drive.IsReady) { rows.Add(Metric("drive:" + drive.Name, "Ổ " + drive.Name, "Chưa sẵn sàng", "Không truy cập ổ mạng")); continue; }
                rows.Add(Metric("drive:" + drive.Name, "Ổ " + drive.Name,
                    $"{Bytes((ulong)(drive.TotalSize - drive.TotalFreeSpace))} / {Bytes((ulong)drive.TotalSize)}",
                    "Đã dùng / tổng • khả dụng " + Bytes((ulong)drive.AvailableFreeSpace)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { denied++; rows.Add(Metric("drive:" + drive.Name, "Ổ " + drive.Name, "Không đọc được", "Thiếu quyền hoặc ổ không còn sẵn sàng")); }
        }
        await CacheHardwareAsync(token).ConfigureAwait(false);
        foreach (var item in _hardware) rows.Add(Metric("hardware:" + item.Name, item.Name, item.Value, "CIM, cache tối đa 20 phút"));
        if (_hardwareUnavailable) rows.Add(Metric("hardware:error", "Thông tin phần cứng", "Không đọc được", "CIM không khả dụng; các số đo native vẫn hoạt động"));
        return Result(Module.Dashboard, [new("name", "Thông tin"), new("value", "Giá trị"), new("source", "Nguồn / giải thích")], rows,
            !first || !second || !ramOk || denied > 0 || _hardwareUnavailable,
            "Số đo thật tại máy này. Không suy đoán nhiệt độ hoặc điểm sức khỏe.");
    }

    private async Task CacheHardwareAsync(CancellationToken token)
    {
        var freshFor = _hardwareUnavailable ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(20);
        if (DateTimeOffset.UtcNow - _hardwareAt < freshFor) return;
        await _hardwareGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow - _hardwareAt < freshFor) return;
            var output = await _commands.PowerShellAsync(HardwareScript, token).ConfigureAwait(false);
            _hardwareUnavailable = !output.Success;
            if (output.Success)
            {
                var values = JsonRows(output.Output);
                _hardware = values.Select(j => (Text(j, "Name"), Text(j, "Value"))).Where(x => !string.IsNullOrWhiteSpace(x.Item2)).ToList();
                _hardwareUnavailable = _hardware.Count == 0;
            }
            _hardwareAt = DateTimeOffset.UtcNow;
        }
        finally { _hardwareGate.Release(); }
    }

    private async Task<ModuleResult> ApplicationsAsync(bool reveal, CancellationToken token)
    {
        var rows = new List<Row>();
        var failures = 0;
        const string uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in RegistryViews())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var list = root.OpenSubKey(uninstall, false);
                if (list is null) continue;
                foreach (var name in list.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = list.OpenSubKey(name, false);
                        if (key is null) continue;
                        var display = RegistryText(key, "DisplayName");
                        if (string.IsNullOrWhiteSpace(display)) continue;
                        var id = $"{hive}:{view}:{uninstall}\\{name}";
                        var source = (hive == RegistryHive.CurrentUser ? "User" : "System") + " / " + (view == RegistryView.Registry64 ? "Registry 64 bit" : "Registry 32 bit");
                        var size = key.GetValue("EstimatedSize") is int kilobytes && kilobytes > 0 ? Bytes((ulong)kilobytes * 1024) : "Không có dữ liệu";
                        var date = RegistryText(key, "InstallDate");
                        if (DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var installed)) date = installed.ToString("dd/MM/yyyy");
                        rows.Add(new Row(id, new() {
                            ["name"] = display, ["version"] = Empty(RegistryText(key, "DisplayVersion")),
                            ["publisher"] = Empty(RegistryText(key, "Publisher")), ["source"] = source,
                            ["size"] = size, ["date"] = Empty(date), ["path"] = PathDisplay(RegistryText(key, "InstallLocation"), reveal)
                        }, new() { ["source"] = source }));
                    }
                    catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { failures++; }
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { failures++; }
        }
        var appx = await _commands.PowerShellAsync(AppxScript, token).ConfigureAwait(false);
        if (appx.Success)
        {
            foreach (var j in JsonRows(appx.Output)) rows.Add(new Row("appx:" + Text(j, "PackageFullName"), new() {
                ["name"] = Text(j, "Name"), ["version"] = Text(j, "Version"), ["publisher"] = Text(j, "Publisher"),
                ["source"] = "User / Appx", ["size"] = "Không có dữ liệu", ["date"] = "Không có dữ liệu",
                ["path"] = PathDisplay(Text(j, "InstallLocation"), reveal)
            }, new() { ["source"] = "Appx" }));
        }
        else failures++;
        rows.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Cell("name"), b.Cell("name")));
        return Result(Module.Applications, [new("name", "Ứng dụng"), new("version", "Phiên bản"), new("publisher", "Nhà phát hành"), new("source", "Nguồn"), new("size", "Kích thước khai báo"), new("date", "Ngày cài khai báo"), new("path", "Thư mục cài")], rows, failures > 0,
            $"Inventory Registry 32/64 bit và Appx của user hiện tại; không bảo đảm đầy đủ tuyệt đối. {FailureMessage(failures)}");
    }

    private async Task<ModuleResult> StartupAsync(bool reveal, CancellationToken token)
    {
        var rows = new List<Row>();
        var failures = 0;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in RegistryViews())
        foreach (var subkey in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(subkey, false);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                {
                    token.ThrowIfCancellationRequested();
                    var kind = hive == RegistryHive.CurrentUser ? "run-user" : "run-machine";
                    var value = RegistryText(key, name);
                    var valueKind = key.GetValueKind(name);
                    var writable = hive == RegistryHive.CurrentUser && subkey.EndsWith("\\Run", StringComparison.Ordinal) && valueKind is RegistryValueKind.String or RegistryValueKind.ExpandString;
                    var id = $"{kind}:{view}:{subkey}:{name}";
                    rows.Add(StartupRow(id, name, value, (hive == RegistryHive.CurrentUser ? "User" : "System") + " / " + subkey.Split('\\').Last() + " / " + (view == RegistryView.Registry64 ? "64 bit" : "32 bit"), writable, reveal,
                        new() { ["kind"] = kind, ["key"] = subkey, ["name"] = name, ["value"] = value, ["view"] = view.ToString(), ["registryKind"] = valueKind.ToString(), ["writable"] = writable.ToString().ToLowerInvariant(), ["supported"] = writable.ToString().ToLowerInvariant() }));
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { failures++; }
        }
        foreach (var scope in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            var folder = Environment.GetFolderPath(scope);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
            try
            {
                foreach (var path in Directory.EnumerateFiles(folder))
                {
                    token.ThrowIfCancellationRequested();
                    if (Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var file = new FileInfo(path);
                        var user = scope == Environment.SpecialFolder.Startup;
                        var writable = user && (file.Attributes & FileAttributes.ReparsePoint) == 0;
                        rows.Add(StartupRow(StableId("folder", path), file.Name, path, user ? "Startup Folder / User" : "Startup Folder / System", writable, reveal,
                            new() { ["kind"] = user ? "folder-user" : "folder-machine", ["path"] = path, ["name"] = file.Name,
                                ["length"] = file.Length.ToString(CultureInfo.InvariantCulture), ["writeTicks"] = file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                                ["creationTicks"] = file.CreationTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                                ["writable"] = writable.ToString().ToLowerInvariant(), ["supported"] = writable.ToString().ToLowerInvariant() }));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failures++; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failures++; }
        }
        var tasks = await _commands.PowerShellAsync(StartupTasksScript, token).ConfigureAwait(false);
        if (tasks.Success)
        {
            foreach (var j in JsonRows(tasks.Output))
            {
                var taskPath = Text(j, "TaskPath") + Text(j, "TaskName");
                rows.Add(StartupRow("task:" + taskPath, Text(j, "TaskName"), Text(j, "Command"), "Scheduled Task / đăng nhập hoặc khởi động", false, reveal,
                    new() { ["kind"] = "task", ["path"] = taskPath, ["name"] = Text(j, "TaskName"), ["state"] = Text(j, "State"), ["writable"] = "false", ["supported"] = "false" }, Text(j, "State")));
            }
        }
        else failures++;
        try
        {
            var services = WindowsNativeInventory.ReadServices(token); failures += services.FailedSources;
            foreach (var service in services.Items.Where(s => s.StartMode == "Auto"))
                rows.Add(StartupRow("service:" + service.Name, service.DisplayName, service.Path, "Service / tự động", false, reveal,
                    new() { ["kind"] = "service", ["name"] = service.Name, ["state"] = service.State, ["writable"] = "false", ["supported"] = "false" }, service.State));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidDataException) { failures++; }
        return Result(Module.Startup, [new("name", "Mục tự khởi động"), new("source", "Nguồn"), new("state", "Trạng thái"), new("command", "Lệnh / đường dẫn"), new("control", "Phạm vi điều khiển")], rows, failures > 0,
            "Điều khiển hỗ trợ đăng ký Run User và file Startup Folder User. Có đăng ký không bảo đảm Windows cho phép chạy: kiểm tra Startup Apps. RunOnce, System, task và service tại đây chỉ đọc. " + FailureMessage(failures));
    }

    private static Row StartupRow(string id, string name, string command, string source, bool writable, bool reveal, Dictionary<string, string> data, string state = "Đã đăng ký; kiểm tra Startup Apps") =>
        new(id, new() { ["name"] = name, ["source"] = source, ["state"] = TranslateState(state),
            ["command"] = reveal ? Empty(command) : "Đã che lệnh / đường dẫn",
            ["control"] = writable ? "Có thể tắt sau preview" : "Chỉ đọc; dùng công cụ Windows" }, data);

    private async Task<ModuleResult> ProcessesAsync(CancellationToken token)
    {
        var before = ProcessSamples(token);
        var watch = Stopwatch.StartNew();
        await Task.Delay(400, token).ConfigureAwait(false);
        var after = ProcessSamples(token);
        var elapsed = watch.Elapsed.TotalSeconds;
        var rows = new List<Row>();
        var unavailable = 0;
        foreach (var current in after.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var cpu = "Đang lấy mẫu";
            if (!current.CpuTicks.HasValue) cpu = "Không đọc được";
            else if (before.TryGetValue(current.Pid, out var previous) && previous.StartTicks > 0 && previous.StartTicks == current.StartTicks && previous.CpuTicks.HasValue)
                cpu = Math.Clamp((current.CpuTicks.Value - previous.CpuTicks.Value) / (double)TimeSpan.TicksPerSecond / elapsed / Environment.ProcessorCount * 100, 0, 100).ToString("0.0", CultureInfo.InvariantCulture) + " %";
            if (current.StartTicks == 0 || current.Memory is null || current.CpuTicks is null) unavailable++;
            rows.Add(new Row(current.Pid.ToString(CultureInfo.InvariantCulture), new() {
                ["name"] = current.Name, ["pid"] = current.Pid.ToString(CultureInfo.InvariantCulture), ["cpu"] = cpu,
                ["ram"] = current.Memory.HasValue ? Bytes((ulong)current.Memory.Value) : "Không đọc được",
                ["started"] = current.StartTicks > 0 ? new DateTime(current.StartTicks, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "Không đọc được",
                ["access"] = current.StartTicks > 0 ? "Có danh tính PID + thời điểm tạo" : "Được Windows bảo vệ / đã kết thúc"
            }, new() { ["pid"] = current.Pid.ToString(CultureInfo.InvariantCulture), ["startTicks"] = current.StartTicks.ToString(CultureInfo.InvariantCulture), ["name"] = current.Name }));
        }
        return Result(Module.Processes, [new("name", "Tiến trình"), new("pid", "PID"), new("cpu", "CPU / toàn máy"), new("ram", "RAM đang dùng"), new("started", "Thời điểm tạo"), new("access", "Khả năng đọc")], rows, unavailable > 0,
            "CPU được lấy mẫu theo khoảng thời gian và chia cho CPU logic. Không đọc command line. " + (unavailable > 0 ? $"{unavailable} tiến trình có trường bị Windows bảo vệ hoặc đã kết thúc." : ""));
    }

    private sealed record ProcessSample(int Pid, string Name, long StartTicks, long? CpuTicks, long? Memory);
    private static Dictionary<int, ProcessSample> ProcessSamples(CancellationToken token)
    {
        var result = new Dictionary<int, ProcessSample>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                token.ThrowIfCancellationRequested();
                int pid;
                try { pid = process.Id; } catch (InvalidOperationException) { continue; }
                var name = "Không đọc được"; long start = 0; long? cpu = null; long? memory = null;
                try { name = process.ProcessName; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                try { start = process.StartTime.ToUniversalTime().Ticks; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                try { cpu = process.TotalProcessorTime.Ticks; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                try { memory = process.WorkingSet64; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                result[pid] = new(pid, name, start, cpu, memory);
            }
        }
        return result;
    }

    private static ModuleResult ReadServices(bool reveal, CancellationToken token)
    {
        var inventory = WindowsNativeInventory.ReadServices(token);
        var rows = new List<Row>();
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in inventory.Items)
            foreach (var dependency in service.Dependencies ?? new())
            {
                if (dependency.StartsWith('+')) continue; // Load-order group, not a service name.
                if (!dependents.TryGetValue(dependency, out var list)) dependents[dependency] = list = new();
                list.Add(service.Name);
            }
        foreach (var service in inventory.Items.DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var dependencies = service.Dependencies is null ? "Không đọc được" : service.Dependencies.Count == 0 ? "Không có" : string.Join("; ", service.Dependencies);
            var dependentNames = dependents.GetValueOrDefault(service.Name) ?? new();
            var dependentText = dependentNames.Count == 0 ? "Không có trong config đọc được" : string.Join("; ", dependentNames);
            if (inventory.FailedSources > 0) dependentText += " (một phần)";
            var name = service.Name;
            rows.Add(new Row(name, new() {
                ["name"] = name, ["display"] = service.DisplayName, ["state"] = TranslateState(service.State),
                ["startMode"] = service.StartMode.Length == 0 ? "Không đọc được" : TranslateStart(service.StartMode),
                ["path"] = reveal ? service.Path.Length == 0 ? "Không đọc được" : service.Path : "Đã che lệnh / đường dẫn",
                ["dependencies"] = dependencies, ["dependents"] = dependentText
            }, new() { ["name"] = name, ["state"] = service.State, ["startMode"] = service.StartMode,
                ["path"] = service.Path, ["serviceType"] = service.ServiceType,
                ["dependencies"] = string.Join("; ", service.Dependencies ?? new()), ["dependents"] = string.Join("; ", dependentNames),
                ["configReadable"] = (service.Dependencies is not null).ToString().ToLowerInvariant() }));
        }
        return Result(Module.Services, [new("name", "Tên service"), new("display", "Tên hiển thị"), new("state", "Trạng thái"), new("startMode", "Kiểu khởi động"), new("dependencies", "Cần service"), new("dependents", "Service phụ thuộc"), new("path", "Lệnh thực thi")], rows, inventory.FailedSources > 0,
            "Service Control Manager native, chỉ quyền đọc. Windows có thể bỏ qua service không cho query status; danh sách phụ thuộc suy ra từ config đọc được. Mutation có policy riêng. " + FailureMessage(inventory.FailedSources));
    }

    private Task<ModuleResult> PortsAsync(bool reveal, CancellationToken token)
    {
        var before = ProcessSamples(token);
        var inventory = WindowsNativeInventory.ReadEndpoints(token);
        if (inventory.FailedSources == 4 && inventory.Items.Count == 0)
            return Task.FromResult(ModuleResult.Failure(Module.Ports, "Không đọc được các bảng endpoint IP Helper tại máy này.", inventory.Denied ? ResultState.Denied : ResultState.Error));
        var identity = ProcessSamples(token);
        var rows = new List<Row>();
        foreach (var endpoint in inventory.Items)
        {
            token.ThrowIfCancellationRequested();
            var protocol = endpoint.Protocol;
            var address = endpoint.LocalAddress;
            var remote = endpoint.RemoteAddress;
            var pidText = endpoint.Pid;
            int.TryParse(pidText, out var pid);
            identity.TryGetValue(pid, out var process);
            if (process is null || process.StartTicks <= 0 || !before.TryGetValue(pid, out var previous) || previous.StartTicks != process.StartTicks)
                process = null;
            var id = StableId("endpoint", string.Join(":", protocol, address, endpoint.LocalPort, remote, endpoint.RemotePort, pidText, endpoint.State));
            rows.Add(new Row(id, new() {
                ["protocol"] = protocol + (address.Contains(':') ? " / IPv6" : " / IPv4"),
                ["address"] = reveal ? address : "Đã che địa chỉ", ["port"] = endpoint.LocalPort,
                ["remote"] = protocol == "UDP" ? "—" : reveal ? remote + ":" + endpoint.RemotePort : "Đã che địa chỉ",
                ["state"] = protocol == "UDP" ? "Endpoint UDP" : endpoint.State, ["pid"] = pidText,
                ["name"] = process?.Name ?? "Không đọc được / đã kết thúc"
            }, new() { ["pid"] = pidText, ["startTicks"] = (process?.StartTicks ?? 0).ToString(CultureInfo.InvariantCulture),
                ["name"] = process?.Name ?? "", ["protocol"] = protocol, ["localAddress"] = address, ["localPort"] = endpoint.LocalPort,
                ["remoteAddress"] = remote, ["remotePort"] = endpoint.RemotePort, ["state"] = endpoint.State }));
        }
        return Task.FromResult(Result(Module.Ports, [new("protocol", "Giao thức"), new("address", "Địa chỉ bind"), new("port", "Port"), new("remote", "Endpoint từ xa"), new("state", "Trạng thái"), new("pid", "PID"), new("name", "Tiến trình sở hữu")], rows, inventory.FailedSources > 0,
            "IP Helper native: TCP/UDP IPv4/IPv6, quyền user thường. PID + thời điểm tạo phải ổn định trước/sau inventory. Không suy đoán ứng dụng theo số port. " + FailureMessage(inventory.FailedSources)));
    }

    private static ModuleResult ReadEnvironment(bool reveal, CancellationToken token)
    {
        var rows = new List<Row>();
        var failures = 0;
        foreach (var source in new[] { (RegistryHive.CurrentUser, @"Environment", "User"), (RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "Machine") })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(source.Item1, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
                using var key = root.OpenSubKey(source.Item2, false);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                {
                    token.ThrowIfCancellationRequested();
                    var value = RegistryText(key, name);
                    rows.Add(EnvironmentRow(name, value, source.Item3, key.GetValueKind(name).ToString(), reveal));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { failures++; }
        }
        try
        {
            foreach (DictionaryEntry item in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process))
            {
                token.ThrowIfCancellationRequested();
                rows.Add(EnvironmentRow(item.Key.ToString() ?? "", item.Value?.ToString() ?? "", "Process", "Bản sao trong process hiện tại", reveal));
            }
        }
        catch (System.Security.SecurityException) { failures++; }
        rows.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Cell("scope") + a.Cell("name"), b.Cell("scope") + b.Cell("name")));
        return Result(Module.Environment, [new("scope", "Phạm vi"), new("name", "Biến"), new("value", "Giá trị"), new("kind", "Kiểu / nguồn"), new("control", "Điều khiển")], rows, failures > 0,
            "Mọi giá trị được che mặc định. Registry được đọc nguyên gốc, không mở rộng %biến% và giữ thứ tự PATH. Process đang mở có thể chưa nhận thay đổi User/System. " + FailureMessage(failures));
    }

    private static Row EnvironmentRow(string name, string value, string scope, string kind, bool reveal) =>
        new(scope + ":" + name, new() { ["scope"] = scope switch { "User" => "User — người dùng", "Machine" => "System — toàn máy", _ => "Process — app hiện tại" },
            ["name"] = name, ["value"] = reveal ? value : "•••• (đã che)", ["kind"] = kind,
            ["control"] = scope switch { "User" => "Sửa sau preview", "Machine" => "Cần xác nhận và UAC riêng", _ => "Chỉ đọc" } },
            new() { ["name"] = name, ["scope"] = scope, ["value"] = value, ["registryKind"] = kind });

    private static ModuleResult ReadNetwork(bool reveal, CancellationToken token)
    {
        var rows = new List<Row>();
        var failures = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var properties = adapter.GetIPProperties();
                var addresses = properties.UnicastAddresses.Select(a => a.Address.ToString()).ToArray();
                var dns = properties.DnsAddresses.Select(a => a.ToString()).ToArray();
                var gateways = properties.GatewayAddresses.Select(a => a.Address.ToString()).ToArray();
                rows.Add(new Row(adapter.Id, new() {
                    ["name"] = adapter.Name, ["description"] = adapter.Description, ["type"] = adapter.NetworkInterfaceType.ToString(),
                    ["state"] = adapter.OperationalStatus switch { OperationalStatus.Up => "Đang hoạt động", OperationalStatus.Down => "Đang tắt / chưa kết nối", _ => adapter.OperationalStatus.ToString() },
                    ["speed"] = adapter.Speed > 0 ? (adapter.Speed / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + " Mbit/s" : "Không có dữ liệu",
                    ["ip"] = addresses.Length == 0 ? "Không có địa chỉ" : reveal ? string.Join("; ", addresses) : "Đã che (" + addresses.Length + " địa chỉ)",
                    ["dns"] = dns.Length == 0 ? "Không có dữ liệu" : reveal ? string.Join("; ", dns) : "Đã che (" + dns.Length + " địa chỉ)",
                    ["gateway"] = gateways.Length == 0 ? "Không có gateway" : reveal ? string.Join("; ", gateways) : "Đã che địa chỉ"
                }));
            }
            catch (Exception e) when (e is NetworkInformationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { failures++; }
        }
        return Result(Module.Network, [new("name", "Adapter"), new("description", "Mô tả"), new("type", "Loại"), new("state", "Trạng thái"), new("speed", "Tốc độ liên kết"), new("ip", "IP cục bộ"), new("dns", "DNS"), new("gateway", "Gateway")], rows, failures > 0,
            "Chỉ đọc thông tin adapter tại máy; không gọi dịch vụ IP công cộng và không đổi cấu hình mạng. Trạng thái adapter không bảo đảm truy cập Internet. " + FailureMessage(failures));
    }

    private async Task<ModuleResult> DevToolsAsync(bool reveal, CancellationToken token)
    {
        var rows = new List<Row>();
        var failures = 0;
        var directories = ToolDirectories();
        foreach (var tool in ToolSpecs)
        {
            token.ThrowIfCancellationRequested();
            var found = new List<string>();
            foreach (var directory in directories)
            {
                foreach (var executable in tool.Executables)
                {
                    try
                    {
                        var full = Path.GetFullPath(Path.Combine(directory, executable));
                        if (File.Exists(full) && !found.Contains(full, StringComparer.OrdinalIgnoreCase)) found.Add(full);
                    }
                    catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
                }
            }
            if (found.Count == 0)
            {
                rows.Add(new Row("tool:" + tool.Name, new() { ["name"] = tool.Name, ["version"] = "Không phát hiện", ["path"] = "—", ["source"] = "PATH + vị trí cài phổ biến", ["state"] = "Chưa tìm thấy; không khẳng định chưa cài" }));
                continue;
            }
            for (var index = 0; index < found.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var path = found[index];
                var verified = ReadProcess.IsTrustedTool(path);
                var version = "Không có dữ liệu";
                var state = verified ? "Chữ ký hợp lệ; chỉ probe phiên bản" : "Chưa xác minh; không thực thi";
                try
                {
                    var metadata = FileVersionInfo.GetVersionInfo(path);
                    version = Empty(metadata.ProductVersion ?? metadata.FileVersion);
                    if (tool.Name == "npm") version = NpmVersion(path);
                }
                catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or JsonException) { failures++; }
                if (verified)
                {
                    var probe = await _commands.VersionAsync(path, tool.Argument, token).ConfigureAwait(false);
                    if (probe.Success)
                    {
                        version = probe.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Không có dữ liệu";
                        if (version.Length > 240) version = version[..240];
                    }
                    else { failures++; state = probe.Code == "timeout" ? "Probe phiên bản quá thời gian" : "Không đọc được phiên bản thực thi"; }
                }
                if (tool.Name == "Docker")
                {
                    if (verified)
                    {
                        var daemon = await _commands.DockerStatusAsync(path, token).ConfigureAwait(false);
                        var serverVersion = daemon.Output.Trim();
                        state += daemon.Success && System.Text.RegularExpressions.Regex.IsMatch(serverVersion, @"^\d[\w.\-+]{0,80}$")
                            ? "; daemon đang chạy (server " + serverVersion + ")"
                            : daemon.Code == "access-denied" ? "; daemon từ chối quyền" : "; không kết nối được daemon";
                    }
                    else
                    {
                        using var running = FindNamedProcess("com.docker.backend");
                        state += running is null ? "; không thấy backend (daemon chưa xác minh)" : "; có backend (daemon chưa xác minh)";
                    }
                }
                rows.Add(new Row(StableId("tool", tool.Name + ":" + path), new() { ["name"] = tool.Name, ["version"] = PathDisplay(version, reveal),
                    ["path"] = PathDisplay(path, reveal), ["source"] = index == 0 ? "Resolve đầu tiên; PATH + vị trí cài phổ biến" : "Bản cài khác; cùng tồn tại hợp lệ", ["state"] = state },
                    new() { ["name"] = tool.Name, ["path"] = path, ["verified"] = verified.ToString().ToLowerInvariant() }));
            }
        }
        return Result(Module.DevTools, [new("name", "Công cụ"), new("version", "Phiên bản / metadata"), new("path", "Đường dẫn resolve"), new("source", "Nguồn phát hiện"), new("state", "Trạng thái xác minh")], rows, failures > 0,
            "Chỉ probe executable thuộc danh sách cho phép, trong thư mục cài tin cậy, không có reparse point và chữ ký Authenticode hợp lệ. File khác chỉ đọc metadata; không khởi chạy daemon. " + FailureMessage(failures));
    }

    private sealed record ToolSpec(string Name, string[] Executables, string Argument = "--version");
    private static readonly ToolSpec[] ToolSpecs = [
        new("Node.js", ["node.exe"]), new("npm", ["npm.cmd"]), new("Java", ["java.exe"], "-version"),
        new("Python", ["python.exe", "python3.exe"]), new("Git", ["git.exe"]), new("Docker", ["docker.exe"]),
        new(".NET", ["dotnet.exe"]), new("PostgreSQL", ["psql.exe"]), new("MySQL", ["mysql.exe"]),
        new("MongoDB Shell", ["mongosh.exe"]), new("SQLite", ["sqlite3.exe"])
    ];

    private static List<string> ToolDirectories()
    {
        var result = new List<string>();
        var rawPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var item in rawPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = item.Trim().Trim('"');
            if (Path.IsPathFullyQualified(directory) && !result.Contains(directory, StringComparer.OrdinalIgnoreCase)) result.Add(directory);
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            foreach (var suffix in new[] { "nodejs", @"Git\cmd", @"Git\bin", @"Docker\Docker\resources\bin", "dotnet", @"Java\jre1.8.0_", @"MySQL\MySQL Server 8.0\bin", @"MongoDB\mongosh\bin" })
                result.Add(Path.Combine(root, suffix));
            foreach (var family in new[] { "Java", "PostgreSQL" })
            {
                try
                {
                    var parent = Path.Combine(root, family);
                    if (Directory.Exists(parent)) foreach (var folder in Directory.EnumerateDirectories(parent).Take(40)) result.Add(Path.Combine(folder, "bin"));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        try
        {
            var pythonRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
            if (Directory.Exists(pythonRoot)) result.AddRange(Directory.EnumerateDirectories(pythonRoot).Take(40));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(160).ToList();
    }

    private static string NpmVersion(string commandPath)
    {
        var package = Path.Combine(Path.GetDirectoryName(commandPath)!, "node_modules", "npm", "package.json");
        if (!File.Exists(package) || new FileInfo(package).Length > 1024 * 1024) return "Không có dữ liệu";
        using var json = JsonDocument.Parse(File.ReadAllText(package));
        return Text(json.RootElement, "version") + " (package metadata; không thực thi .cmd)";
    }

    private static Process? FindNamedProcess(string name)
    {
        var matches = Process.GetProcessesByName(name);
        foreach (var process in matches.Skip(1)) process.Dispose();
        return matches.FirstOrDefault();
    }

    private static RegistryView[] RegistryViews() => Environment.Is64BitOperatingSystem ? [RegistryView.Registry64, RegistryView.Registry32] : [RegistryView.Registry32];
    private static string StableId(string prefix, string identity) => prefix + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
    private static string RegistryText(RegistryKey key, string name) => key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
    private static string Empty(string? value) => string.IsNullOrWhiteSpace(value) ? "Không có dữ liệu" : value;
    private static string FailureMessage(int failures) => failures > 0 ? $"{failures} nguồn/mục không đọc được; kết quả là một phần." : "";
    private static Row Metric(string id, string name, string value, string source) => new(id, new() { ["name"] = name, ["value"] = value, ["source"] = source });
    private static ModuleResult Result(Module module, List<Column> columns, List<Row> rows, bool partial, string message) =>
        new(module, columns, rows, partial ? ResultState.Partial : rows.Count == 0 ? ResultState.Empty : ResultState.Ready, message, DateTimeOffset.Now);
    private static ModuleResult CommandFailure(Module module, ReadCommandResult result) => ModuleResult.Failure(module,
        result.Code switch { "timeout" => "Lệnh Windows vượt giới hạn 20 giây. Có thể thử tải lại.", "access-denied" => "Windows từ chối quyền đọc.", "output-limit" => "Inventory vượt giới hạn 8 MiB. Không hiển thị dữ liệu bị cắt.", _ => "Không đọc được nguồn Windows; không dùng dữ liệu giả thay thế." },
        result.Code == "access-denied" ? ResultState.Denied : ResultState.Error);

    internal static string PathDisplay(string value, bool reveal)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Không có dữ liệu";
        if (reveal) return value;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if ((!string.IsNullOrEmpty(profile) && value.Contains(profile, StringComparison.OrdinalIgnoreCase))
            || System.Text.RegularExpressions.Regex.IsMatch(value, @"(?i)[a-z]:\\Users\\"))
            return "Đã che đường dẫn cá nhân";
        if (value.Contains(@"\\", StringComparison.Ordinal)) return "Đã che đường dẫn mạng";
        return value;
    }

    internal static string Bytes(ulong bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double size = bytes; var index = 0;
        while (size >= 1024 && index < units.Length - 1) { size /= 1024; index++; }
        return size.ToString(index == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + " " + units[index];
    }

    private static List<JsonElement> JsonRows(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return new();
        using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 20 });
        return document.RootElement.ValueKind switch { JsonValueKind.Array => document.RootElement.EnumerateArray().Select(j => j.Clone()).ToList(), JsonValueKind.Object => [document.RootElement.Clone()], JsonValueKind.Null => new(), _ => throw new JsonException("Inventory không đúng schema.") };
    }
    private static string Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString() : "";
    private static string TranslateState(string state) => state switch { "Running" => "Đang chạy", "Stopped" => "Đã dừng", "Disabled" => "Đã tắt", "Ready" => "Sẵn sàng", "Start Pending" => "Đang khởi động", "Stop Pending" => "Đang dừng", _ => state };
    private static string TranslateStart(string mode) => mode switch { "Auto" => "Tự động", "Manual" => "Thủ công", "Disabled" => "Đã vô hiệu hóa", _ => mode };

    private static bool TrySystemTimes(out ulong idle, out ulong kernel, out ulong user)
    {
        var success = GetSystemTimes(out var i, out var k, out var u);
        idle = ((ulong)i.High << 32) | i.Low; kernel = ((ulong)k.High << 32) | k.Low; user = ((ulong)u.High << 32) | u.Low;
        return success;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeFileTime { public uint Low; public uint High; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length; public uint MemoryLoad; public ulong TotalPhysical; public ulong AvailablePhysical;
        public ulong TotalPageFile; public ulong AvailablePageFile; public ulong TotalVirtual; public ulong AvailableVirtual; public ulong AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private const string HardwareScript = """
        $r=@();$cs=Get-CimInstance Win32_ComputerSystem;$os=Get-CimInstance Win32_OperatingSystem;
        $r+=[pscustomobject]@{Name='Máy';Value=($cs.Manufacturer+' / '+$cs.Model)};
        $r+=[pscustomobject]@{Name='Windows';Value=($os.Caption+' / build '+$os.BuildNumber)};
        foreach($cpu in Get-CimInstance Win32_Processor){$r+=[pscustomobject]@{Name='CPU';Value=$cpu.Name}};
        ConvertTo-Json -InputObject @($r) -Compress -Depth 3
        """;
    private const string AppxScript = """
        $r=@(Get-AppxPackage | ForEach-Object {[pscustomobject]@{Name=$_.Name;Version=$_.Version.ToString();Publisher=$_.Publisher;InstallLocation=$_.InstallLocation;PackageFullName=$_.PackageFullName}});
        ConvertTo-Json -InputObject $r -Compress -Depth 3
        """;
    private const string StartupTasksScript = """
        $r=@(Get-ScheduledTask | Where-Object {@($_.Triggers | Where-Object {$_.CimClass.CimClassName -match 'LogonTrigger|BootTrigger'}).Count -gt 0} | ForEach-Object {
            [pscustomobject]@{TaskName=$_.TaskName;TaskPath=$_.TaskPath;State=$_.State.ToString();Command=($_.Actions | ForEach-Object {$_.Execute+' '+$_.Arguments}) -join '; '}
        });ConvertTo-Json -InputObject $r -Compress -Depth 3
        """;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _commands.Dispose();
    }
}
