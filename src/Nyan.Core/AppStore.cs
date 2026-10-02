using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyan.Core;

/// <summary>Bounded current-user encrypted state. Imported payloads remain inert data.</summary>
public sealed class AppStore
{
    private const int Version = 1, MaxBytes = 20 * 1024 * 1024;
    private const int MaxHistory = 1000, MaxSnapshots = 100, MaxBackups = 100;
    private const string Magic = "NYAN-NCC1\0";
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly string _root, _statePath, _lockPath;
    private readonly object _gate = new();
    public string Root => _root;

    public AppStore(string? root = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NyanControlCenter"));
        EnsureSafeDirectory(_root);
        Directory.CreateDirectory(_root);
        EnsureSafeDirectory(_root);
        ProtectDirectory(_root);
        _statePath = Path.Combine(_root, "state.ncc");
        _lockPath = Path.Combine(_root, "state.lock");
        Read(state => state.Settings);
    }

    public AppSettings Settings => Read(state => state.Settings);
    public void SaveSettings(AppSettings settings) => Write(state => { ValidateSettings(settings); state.Settings = settings; });
    public void AddHistory(HistoryEntry entry) => Write(state =>
    {
        ValidateHistory(entry);
        state.History.RemoveAll(item => item.Id == entry.Id);
        state.History.Add(entry);
    });
    public List<HistoryEntry> GetHistory() => Read(state => state.History.OrderByDescending(item => item.At).ToList());
    public void SaveSnapshot(Snapshot snapshot) => Write(state =>
    {
        ValidateSnapshot(snapshot);
        state.Snapshots.RemoveAll(item => item.Id == snapshot.Id);
        if (state.Snapshots.Count >= MaxSnapshots) throw Error("limit", "Chỉ lưu tối đa 100 snapshot.");
        state.Snapshots.Add(snapshot);
    });
    public List<Snapshot> GetSnapshots() => Read(state => state.Snapshots.OrderByDescending(item => item.At).ToList());
    public Snapshot GetSnapshot(string id) => Read(state => FindSnapshot(state, id));

    public List<SnapshotChange> CompareSnapshots(string olderId, string newerId) => Read(state =>
    {
        var older = FindSnapshot(state, olderId);
        var newer = FindSnapshot(state, newerId);
        var changes = new List<SnapshotChange>();
        foreach (var module in older.Modules.Keys.Union(newer.Modules.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var leftRows = older.Modules.GetValueOrDefault(module) ?? new();
            var rightRows = newer.Modules.GetValueOrDefault(module) ?? new();
            var leftCoverage = Coverage(leftRows);
            var rightCoverage = Coverage(rightRows);
            var complete = leftCoverage is "Ready" or "Empty" && rightCoverage is "Ready" or "Empty";
            if (!complete || leftCoverage != rightCoverage)
                changes.Add(new(module, "Coverage", "__coverage", leftCoverage, rightCoverage + (complete ? "" : " · Không kết luận thêm/xóa vì phạm vi đọc chưa đầy đủ.")));
            var oldRows = leftRows.Where(row => row.Id != "__coverage").ToDictionary(row => row.Id, StringComparer.Ordinal);
            var newRows = rightRows.Where(row => row.Id != "__coverage").ToDictionary(row => row.Id, StringComparer.Ordinal);
            foreach (var id in oldRows.Keys.Union(newRows.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (!oldRows.TryGetValue(id, out var before)) { if (complete) changes.Add(new(module, "Added", id, "", Describe(newRows[id]))); }
                else if (!newRows.TryGetValue(id, out var after)) { if (complete) changes.Add(new(module, "Removed", id, Describe(before), "")); }
                else foreach (var key in before.Cells.Keys.Union(after.Cells.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    var left = before.Cells.GetValueOrDefault(key, "");
                    var right = after.Cells.GetValueOrDefault(key, "");
                    if (!StringComparer.Ordinal.Equals(left, right)) changes.Add(new(module, "Changed", id + "/" + key, left, right));
                }
            }
        }
        return changes;
    });

    public void ExportSnapshot(string id, string path)
    {
        RequireExtension(path, ".nccsnapshot");
        SaveExternal(path, new Transfer { Kind = "snapshot", Snapshot = GetSnapshot(id) });
    }
    public void ImportSnapshot(string path)
    {
        RequireExtension(path, ".nccsnapshot");
        var transfer = ReadTransfer(path);
        if (transfer.Kind != "snapshot" || transfer.Snapshot is null || transfer.State is not null) throw Error("invalid_import", "Tệp không phải snapshot hợp lệ.");
        ValidateSnapshot(transfer.Snapshot);
        SaveSnapshot(transfer.Snapshot);
    }
    public void Backup(string path)
    {
        RequireExtension(path, ".nccbackup");
        Read(state => { SaveExternal(path, new Transfer { Kind = "backup", State = state }); return true; });
    }
    public void Restore(string path)
    {
        RequireExtension(path, ".nccbackup");
        var transfer = ReadTransfer(path);
        if (transfer.Kind != "backup" || transfer.State is null || transfer.Snapshot is not null) throw Error("invalid_import", "Tệp không phải bản sao lưu hợp lệ.");
        ValidateState(transfer.State);
        lock (_gate)
        {
            using var held = AcquireLock();
            Load();
            Prune(transfer.State);
            Save(transfer.State);
        }
    }
    public void PutBackup(string id, Dictionary<string, string> values) => Write(state =>
    {
        ValidateId(id);
        ValidateBackupMap(values);
        if (values.ContainsKey("id")) throw Error("invalid_import", "Trường id được dành riêng.");
        if (!state.Backups.ContainsKey(id) && state.Backups.Count >= MaxBackups) throw Error("limit", "Đã đạt giới hạn 100 bản hoàn tác.");
        state.Backups[id] = new(values, StringComparer.Ordinal);
    });
    public Dictionary<string, string> GetBackup(string id) => Read<Dictionary<string, string>>(state =>
    {
        ValidateId(id);
        return state.Backups.TryGetValue(id, out var backup) ? new(backup, StringComparer.Ordinal) : throw Error("not_found", "Không còn bản hoàn tác này.");
    });
    public List<Dictionary<string, string>> GetBackups() => Read(state => state.Backups.Select(pair =>
    {
        var result = new Dictionary<string, string>(pair.Value, StringComparer.Ordinal) { ["id"] = pair.Key };
        return result;
    }).ToList());
    public void RemoveBackup(string id) => Write(state => { ValidateId(id); state.Backups.Remove(id); });

    private T Read<T>(Func<State, T> action)
    {
        lock (_gate) { using var held = AcquireLock(); return action(Load()); }
    }
    private void Write(Action<State> action)
    {
        lock (_gate)
        {
            using var held = AcquireLock();
            var state = Load();
            action(state);
            Prune(state);
            ValidateState(state);
            Save(state);
        }
    }
    private FileStream AcquireLock()
    {
        EnsureSafeDirectory(_root);
        if (File.Exists(_lockPath)) EnsureSafeFile(_lockPath);
        var start = Environment.TickCount64;
        while (true)
        {
            try { return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Environment.TickCount64 - start < 3000) { Thread.Sleep(30); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw Error("store_busy", "Kho dữ liệu đang được sử dụng hoặc không thể truy cập."); }
        }
    }
    private State Load()
    {
        if (!File.Exists(_statePath))
        {
            var legacy = Path.Combine(_root, "state.v0.json");
            if (!File.Exists(legacy)) return new State();
            if (!File.Exists(Path.Combine(_root, ".nyan-fixture"))) throw Error("legacy_store", "Kho schema 0 cần quy trình chuyển đổi có kiểm soát.");
            try
            {
                var state = Deserialize<State>(ReadBounded(legacy));
                if (state.SchemaVersion != 0 || state.Settings is null) throw Error("invalid_schema", "Schema cũ không hợp lệ.");
                state.SchemaVersion = Version;
                state.Settings = state.Settings with { SchemaVersion = Version };
                ValidateState(state);
                Prune(state);
                Save(state);
                File.Delete(legacy);
                return state;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or AppException)
            { PreserveCorrupt(legacy); throw Error("store_corrupt", "Không thể chuyển đổi kho cũ; bản gốc đã được giữ lại."); }
        }
        try
        {
            var state = Deserialize<State>(Unseal(ReadBounded(_statePath)));
            ValidateState(state);
            Prune(state);
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or AppException)
        {
            PreserveCorrupt(_statePath);
            throw Error("store_corrupt", "Kho dữ liệu không đọc được hoặc thuộc người dùng khác. Bản lỗi được giữ nguyên và sao lưu để kiểm tra.");
        }
    }
    private void Save(State state) => AtomicWrite(_statePath, Seal(Serialize(state)));
    private static byte[] Serialize<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > MaxBytes) throw Error("limit", "Dữ liệu vượt quá giới hạn 20 MB.");
        return bytes;
    }
    private static T Deserialize<T>(byte[] value)
    {
        try { return JsonSerializer.Deserialize<T>(value, Json) ?? throw Error("invalid_import", "Dữ liệu trống."); }
        finally { CryptographicOperations.ZeroMemory(value); }
    }
    private static void SaveExternal(string path, Transfer transfer) => AtomicWrite(Path.GetFullPath(path), Seal(Serialize(transfer)));
    private static Transfer ReadTransfer(string path)
    {
        try
        {
            var transfer = Deserialize<Transfer>(Unseal(ReadBounded(Path.GetFullPath(path))));
            if (transfer.SchemaVersion != Version) throw Error("invalid_schema", "Schema của tệp không được hỗ trợ.");
            return transfer;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or AppException)
        { throw Error("invalid_import", "Tệp không hợp lệ, vượt giới hạn, hoặc không được mã hóa bởi người dùng Windows hiện tại."); }
    }
    private static void AtomicWrite(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path) ?? throw Error("invalid_path", "Đường dẫn không hợp lệ.");
        EnsureSafeDirectory(directory);
        if (!Directory.Exists(directory)) throw Error("invalid_path", "Thư mục đích không tồn tại.");
        if (File.Exists(path)) EnsureSafeFile(path);
        var temporary = Path.Combine(directory, ".nyan-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw Error("store_write", "Không thể lưu dữ liệu an toàn. Kho trước đó được giữ nguyên."); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static byte[] ReadBounded(string path)
    {
        EnsureSafeFile(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= 0 || stream.Length > MaxBytes + 64 * 1024) throw Error("limit", "Kích thước tệp không hợp lệ.");
        var data = new byte[(int)stream.Length];
        stream.ReadExactly(data);
        return data;
    }
    private void PreserveCorrupt(string path)
    {
        try
        {
            // Never follow a rejected link while trying to preserve corruption evidence.
            EnsureSafeFile(path);
            if (File.Exists(path) && Directory.GetFiles(_root, "corrupt-*.ncc").Length < 5)
                File.Copy(path, Path.Combine(_root, "corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N") + ".ncc"), false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AppException) { }
    }
    private static Snapshot FindSnapshot(State state, string id)
    {
        ValidateId(id);
        return state.Snapshots.Find(snapshot => snapshot.Id == id) ?? throw Error("not_found", "Không tìm thấy snapshot.");
    }
    private static string Describe(Row row) => string.Join("; ", row.Cells.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
    private static string Coverage(List<Row> rows) => rows.Find(row => row.Id == "__coverage")?.Cell("state") ?? "Unknown";
    private static void Prune(State state)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-state.Settings.RetentionDays);
        state.History = state.History.Where(item => item.At >= cutoff).OrderByDescending(item => item.At).Take(MaxHistory).ToList();
    }
    private static void ValidateState(State state)
    {
        if (state.SchemaVersion != Version || state.Settings is null || state.History is null || state.Snapshots is null || state.Backups is null) throw Error("invalid_schema", "Schema kho dữ liệu không hợp lệ.");
        ValidateSettings(state.Settings);
        if (state.History.Count > MaxHistory || state.Snapshots.Count > MaxSnapshots || state.Backups.Count > MaxBackups) throw Error("limit", "Kho dữ liệu vượt giới hạn số bản ghi.");
        foreach (var entry in state.History) ValidateHistory(entry);
        foreach (var snapshot in state.Snapshots) ValidateSnapshot(snapshot);
        if (state.History.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != state.History.Count || state.Snapshots.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != state.Snapshots.Count) throw Error("invalid_import", "ID bị trùng trong kho dữ liệu.");
        foreach (var backup in state.Backups) { ValidateId(backup.Key); ValidateBackupMap(backup.Value); }
    }
    private static void ValidateSettings(AppSettings settings)
    {
        if (settings.SchemaVersion != Version || settings.RetentionDays is < 1 or > 30) throw Error("invalid_settings", "Cài đặt hoặc thời hạn lưu không hợp lệ (1–30 ngày).");
    }
    private static void ValidateHistory(HistoryEntry entry)
    {
        if (entry is null) throw Error("invalid_import", "Lịch sử không hợp lệ.");
        ValidateId(entry.Id); ValidateText(entry.Action, 128); ValidateText(entry.Target, 4096); ValidateText(entry.Status, 64); ValidateText(entry.Message, 4096);
        if (entry.UndoId is not null) ValidateId(entry.UndoId);
        if (entry.At > DateTimeOffset.UtcNow.AddDays(1)) throw Error("invalid_import", "Thời gian lịch sử không hợp lệ.");
    }
    private static void ValidateSnapshot(Snapshot snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != Version || snapshot.Modules is null || snapshot.Modules.Count > 4) throw Error("invalid_schema", "Schema snapshot không hợp lệ.");
        ValidateId(snapshot.Id); ValidateText(snapshot.Name, 120, false);
        if (snapshot.At > DateTimeOffset.UtcNow.AddDays(1)) throw Error("invalid_import", "Thời gian snapshot không hợp lệ.");
        var total = 0;
        foreach (var pair in snapshot.Modules)
        {
            if (!Enum.TryParse<Module>(pair.Key, false, out var module) || module is not (Module.Applications or Module.Startup or Module.DevTools or Module.Environment) || pair.Key != module.ToString()) throw Error("invalid_import", "Module trong snapshot không hợp lệ.");
            if (pair.Value is null || pair.Value.Count > 10000 || (total += pair.Value.Count) > 30000) throw Error("limit", "Snapshot vượt giới hạn số dòng.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in pair.Value)
            {
                if (row is null || !ids.Add(row.Id)) throw Error("invalid_import", "Dòng snapshot không hợp lệ hoặc trùng ID.");
                ValidateText(row.Id, 4096, false);
                ValidateMap(row.Cells, 32, 16 * 1024);
                if (row.Data is { Count: > 0 }) throw Error("invalid_import", "Snapshot chỉ chứa trường hiển thị; metadata điều khiển không được nhập.");
                if (row.Id == "__coverage")
                {
                    if (row.Cells.Keys.Any(key => key is not ("state" or "detail")) || !Enum.TryParse<ResultState>(row.Cell("state"), false, out var coverage) || !Enum.IsDefined(coverage) || row.Cell("state") != coverage.ToString())
                        throw Error("invalid_import", "Trạng thái phạm vi snapshot không hợp lệ.");
                }
                else if (module == Module.Environment && row.Cells.Keys.Any(key => key is not ("name" or "scope"))) throw Error("invalid_import", "Snapshot môi trường chỉ được chứa tên và scope.");
            }
        }
    }
    private static void ValidateMap(Dictionary<string, string> map, int maxEntries, int maxValue)
    {
        if (map is null || map.Count > maxEntries) throw Error("limit", "Bản ghi vượt giới hạn trường.");
        foreach (var pair in map) { ValidateText(pair.Key, 64, false); ValidateText(pair.Value, maxValue); }
    }
    private static void ValidateBackupMap(Dictionary<string, string> map)
    {
        if (map is null || map.Count > 64 || map.ContainsKey("id")) throw Error("limit", "Bản hoàn tác vượt giới hạn trường.");
        foreach (var pair in map)
        {
            ValidateText(pair.Key, 64, false);
            ValidateText(pair.Value, pair.Key == "contents" ? 6 * 1024 * 1024 : 64 * 1024);
        }
    }
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))) throw Error("invalid_import", "ID không hợp lệ.");
    }
    private static void ValidateText(string value, int limit, bool allowEmpty = true)
    {
        if (value is null || value.Length > limit || (!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Any(character => character == '\0' || char.IsControl(character) && character is not '\r' and not '\n' and not '\t')) throw Error("invalid_import", "Trường dữ liệu không hợp lệ.");
    }
    private static void RequireExtension(string path, string extension)
    {
        if (!Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase)) throw Error("invalid_path", "Tệp phải có phần mở rộng " + extension + ".");
    }
    private static void EnsureSafeFile(string path)
    {
        EnsureSafeDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0 || ((uint)attributes & 0x00440000) != 0) throw Error("unsafe_path", "Không dùng liên kết hoặc tệp chỉ có trên cloud cho kho dữ liệu.");
    }
    private static void EnsureSafeDirectory(string directory)
    {
        var path = Path.GetFullPath(directory);
        if (path.StartsWith("\\\\", StringComparison.Ordinal)) throw Error("unsafe_path", "Kho dữ liệu phải nằm trên máy cục bộ.");
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
        {
            if (!item.Exists) continue;
            if ((item.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0 || ((uint)item.Attributes & 0x00440000) != 0) throw Error("unsafe_path", "Không dùng thư mục liên kết hoặc chỉ có trên cloud.");
        }
    }
    private static void ProtectDirectory(string path)
    {
        try
        {
            var user = WindowsIdentity.GetCurrent().User;
            if (user is null) return;
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.SetOwner(user);
            acl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(acl);
        }
        catch (SystemException) { /* DPAPI remains the confidentiality boundary when ACL update is unavailable. */ }
    }
    private static byte[] Seal(byte[] plain)
    {
        var protectedBytes = Crypt(plain, true);
        var prefix = Encoding.ASCII.GetBytes(Magic);
        var result = new byte[prefix.Length + protectedBytes.Length];
        prefix.CopyTo(result, 0); protectedBytes.CopyTo(result, prefix.Length);
        return result;
    }
    private static byte[] Unseal(byte[] bytes)
    {
        var prefix = Encoding.ASCII.GetBytes(Magic);
        if (bytes.Length <= prefix.Length || !bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix)) throw Error("invalid_import", "Định dạng mã hóa không hợp lệ.");
        var result = Crypt(bytes[prefix.Length..], false);
        if (result.Length > MaxBytes) throw Error("limit", "Dữ liệu giải mã vượt giới hạn.");
        return result;
    }
    private static byte[] Crypt(byte[] value, bool protect)
    {
        var input = new Blob { Length = value.Length, Pointer = Marshal.AllocHGlobal(value.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(value, 0, input.Pointer, value.Length);
            var success = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw Error("encryption", "Windows không thể bảo vệ hoặc giải mã dữ liệu cho người dùng hiện tại.");
            if (output.Length is <= 0 or > MaxBytes + 64 * 1024) throw Error("limit", "Dữ liệu mã hóa vượt giới hạn.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Pointer, result, 0, result.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(value);
            ZeroNative(input.Pointer, input.Length);
            Marshal.FreeHGlobal(input.Pointer);
            if (output.Pointer != IntPtr.Zero) { ZeroNative(output.Pointer, output.Length); LocalFree(output.Pointer); }
        }
    }
    private static void ZeroNative(IntPtr pointer, int length)
    {
        var zero = new byte[Math.Min(length, 16384)];
        for (var offset = 0; offset < length; offset += zero.Length)
            Marshal.Copy(zero, 0, IntPtr.Add(pointer, offset), Math.Min(zero.Length, length - offset));
    }
    private static AppException Error(string code, string message) => new(code, message);
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Pointer; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    private sealed class State
    {
        public State() { }
        [JsonRequired] public int SchemaVersion { get; set; } = Version;
        [JsonRequired] public AppSettings Settings { get; set; } = new();
        [JsonRequired] public List<HistoryEntry> History { get; set; } = new();
        [JsonRequired] public List<Snapshot> Snapshots { get; set; } = new();
        [JsonRequired] public Dictionary<string, Dictionary<string, string>> Backups { get; set; } = new(StringComparer.Ordinal);
    }
    private sealed class Transfer
    {
        public Transfer() { }
        [JsonRequired] public int SchemaVersion { get; set; } = Version;
        [JsonRequired] public string Kind { get; set; } = "";
        public Snapshot? Snapshot { get; set; }
        public State? State { get; set; }
    }
}
