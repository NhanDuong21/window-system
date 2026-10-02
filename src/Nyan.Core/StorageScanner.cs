using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nyan.Core;

/// <summary>Metadata-only scan. It never follows links, opens file contents, or hydrates cloud files.</summary>
public sealed class StorageScanner
{
    private const uint UnsafeAttributes = 0x400 | 0x1000 | 0x40000 | 0x400000;
    private const int MaxRows = 2000;

    public Task<ScanReport> ScanAsync(string root, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        => Task.Run(() => Scan(root, progress, cancellationToken), CancellationToken.None);

    private static ScanReport Scan(string input, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var root = ValidateRoot(input);
        var retained = new PriorityQueue<Row, long>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var frames = new Stack<Frame>();
        long files = 0, bytes = 0, allocated = 0;
        var skipped = 0;
        var allAllocated = true;
        var lastProgress = Environment.TickCount64;
        Row? rootRow = null;
        if (!ReadMetadata(root, true, out var rootMetadata)) throw new AppException("scan_denied", "Không thể mở metadata thư mục đã chọn.");
        frames.Push(new Frame(root, rootMetadata));
        try
        {
            while (frames.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var frame = frames.Peek();
                if (!frame.Started)
                {
                    frame.Started = true;
                    try
                    {
                        CheckAncestors(frame.Path);
                        if (!ReadMetadata(frame.Path, true, out var current) || current.Identity != frame.Metadata.Identity) { skipped++; FinishFrame(); continue; }
                        frame.Entries = new DirectoryInfo(frame.Path).EnumerateFileSystemInfos("*", new EnumerationOptions
                        {
                            RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0,
                            ReturnSpecialDirectories = false, MatchType = MatchType.Simple
                        }).GetEnumerator();
                    }
                    catch (Exception ex) when (IsPartialError(ex)) { skipped++; FinishFrame(); continue; }
                }
                FileSystemInfo entry;
                try
                {
                    if (frame.Entries is null || !frame.Entries.MoveNext()) { FinishFrame(); continue; }
                    entry = frame.Entries.Current;
                }
                catch (Exception ex) when (IsPartialError(ex)) { skipped++; FinishFrame(); continue; }
                try
                {
                    var attributes = (uint)entry.Attributes;
                    if ((attributes & UnsafeAttributes) != 0) { skipped++; continue; }
                    var directory = (attributes & (uint)FileAttributes.Directory) != 0;
                    if (!ReadMetadata(entry.FullName, directory, out var metadata)) { skipped++; continue; }
                    if (directory)
                    {
                        if (frames.Count >= 1024) { skipped++; continue; }
                        frames.Push(new Frame(entry.FullName, metadata));
                    }
                    else
                    {
                        files++;
                        // Single-link files need no retained identity entry, keeping large ordinary scans bounded.
                        var unique = metadata.Links <= 1 || identities.Add(metadata.Identity);
                        var logical = unique ? metadata.Length : 0;
                        var physical = unique ? metadata.Allocated : 0;
                        bytes = checked(bytes + logical);
                        allocated = checked(allocated + (physical ?? 0));
                        frame.Bytes = checked(frame.Bytes + logical);
                        frame.Allocated = checked(frame.Allocated + (physical ?? 0));
                        if (unique && physical is null) { allAllocated = false; frame.AllAllocated = false; }
                        Retain(MakeRow(entry.FullName, false, metadata.Length, metadata.Allocated, metadata, !unique));
                    }
                }
                catch (Exception ex) when (IsPartialError(ex)) { skipped++; }
                if (Environment.TickCount64 - lastProgress >= 150)
                {
                    progress?.Report(new(files, bytes, skipped, "Đang quét metadata trên ổ cục bộ…"));
                    lastProgress = Environment.TickCount64;
                }
            }
            // Aggregate every open ancestor even when cancellation returns a partial result.
            while (frames.Count > 0) FinishFrame();
        }
        finally { foreach (var frame in frames) frame.Entries?.Dispose(); }
        var rows = retained.UnorderedItems.Select(item => item.Element).ToList();
        if (rootRow is not null) rows.Add(rootRow);
        rows.Sort((left, right) => ParseLength(right).CompareTo(ParseLength(left)));
        var cancelled = cancellationToken.IsCancellationRequested;
        progress?.Report(new(files, bytes, skipped, cancelled ? "Đã hủy; giữ kết quả một phần." : skipped > 0 ? "Hoàn tất; có mục không truy cập hoặc được bỏ qua." : "Hoàn tất."));
        return new(root, rows, files, bytes, allAllocated ? allocated : null, skipped, cancelled, DateTimeOffset.Now);

        void Retain(Row row)
        {
            var priority = ParseLength(row);
            if (retained.Count < MaxRows - 1) retained.Enqueue(row, priority);
            else if (retained.TryPeek(out _, out var smallest) && priority > smallest) { retained.Dequeue(); retained.Enqueue(row, priority); }
        }
        void FinishFrame()
        {
            var complete = frames.Pop();
            complete.Entries?.Dispose();
            var row = MakeRow(complete.Path, true, complete.Bytes, complete.AllAllocated ? complete.Allocated : null, complete.Metadata, false);
            if (frames.TryPeek(out var parent))
            {
                parent.Bytes = checked(parent.Bytes + complete.Bytes);
                parent.Allocated = checked(parent.Allocated + complete.Allocated);
                parent.AllAllocated &= complete.AllAllocated;
                Retain(row);
            }
            else rootRow = row;
        }
    }

    private static string ValidateRoot(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new AppException("scan_path", "Hãy chọn thư mục trên ổ cục bộ.");
        if (input.StartsWith(@"\\?\", StringComparison.Ordinal)) input = input[4..];
        if (input.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(input)) throw new AppException("scan_path", "Không quét UNC, mạng hoặc đường dẫn tương đối.");
        string root;
        try { root = Path.GetFullPath(input); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { throw new AppException("scan_path", "Đường dẫn không hợp lệ."); }
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady) throw new AppException("scan_drive", "Chỉ quét ổ cục bộ fixed hoặc removable đang sẵn sàng.");
        if (!Directory.Exists(root)) throw new AppException("scan_path", "Thư mục không tồn tại hoặc không thể truy cập.");
        CheckAncestors(root);
        return Path.TrimEndingDirectorySeparator(root);
    }
    private static void CheckAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (((uint)directory.Attributes & UnsafeAttributes) != 0) throw new AppException("scan_link", "Không quét liên kết reparse hoặc thư mục chỉ có trên cloud.");
    }
    private static bool IsPartialError(Exception ex) => ex is IOException or UnauthorizedAccessException or Win32Exception or AppException or System.Security.SecurityException;
    private static Row MakeRow(string path, bool directory, long length, long? allocated, Metadata metadata, bool duplicate)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (name.Length == 0) name = Path.GetPathRoot(path) ?? path;
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["path"] = path, ["kind"] = directory ? "directory" : "file",
            ["length"] = length.ToString(CultureInfo.InvariantCulture),
            ["lastWriteTicks"] = metadata.LastWriteTicks.ToString(CultureInfo.InvariantCulture),
            ["identity"] = metadata.Identity, ["duplicate"] = duplicate ? "true" : "false"
        };
        return new(id, new()
        {
            ["name"] = name, ["size"] = FormatBytes(length),
            ["allocated"] = allocated.HasValue ? FormatBytes(duplicate ? 0 : allocated.Value) : "Không xác định",
            ["type"] = directory ? "Thư mục" : duplicate ? "Hard link (đã tính)" : Path.GetExtension(path) is { Length: > 0 } extension ? extension : "Tệp",
            ["path"] = MaskPath(path)
        }, data);
    }
    private static long ParseLength(Row row) => long.TryParse(row.Meta("length"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = value; var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return size.ToString(unit == 0 ? "N0" : "N2", CultureInfo.GetCultureInfo("vi-VN")) + " " + units[unit];
    }
    private static string MaskPath(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile) && (path.Equals(profile, StringComparison.OrdinalIgnoreCase) || path.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            return "%USERPROFILE%" + path[profile.Length..];
        return path;
    }
    private static bool ReadMetadata(string path, bool directory, out Metadata metadata)
    {
        metadata = default;
        var nativePath = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + Path.GetFullPath(path);
        using var handle = CreateFile(nativePath, 0x80, 1 | 2 | 4, IntPtr.Zero, 3, 0x00200000 | (directory ? 0x02000000u : 0), IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) || (info.Attributes & UnsafeAttributes) != 0 || ((info.Attributes & 0x10) != 0) != directory) return false;
        // Reject an ancestor redirect that occurred after attribute checks.
        var finalPath = new StringBuilder(32768);
        var characters = GetFinalPathNameByHandle(handle, finalPath, (uint)finalPath.Capacity, 0);
        if (characters == 0 || characters >= finalPath.Capacity || !Path.TrimEndingDirectorySeparator(finalPath.ToString()).Equals(Path.TrimEndingDirectorySeparator(nativePath), StringComparison.OrdinalIgnoreCase)) return false;
        var length = ((long)info.SizeHigh << 32) | info.SizeLow;
        var writeTime = ((long)info.LastWrite.High << 32) | info.LastWrite.Low;
        long ticks;
        try { ticks = DateTime.FromFileTimeUtc(writeTime).Ticks; }
        catch (ArgumentOutOfRangeException) { return false; }
        var identity = info.Volume.ToString("X8") + ":" + info.IndexHigh.ToString("X8") + info.IndexLow.ToString("X8");
        if (GetFileId(handle, 18, out var id, (uint)Marshal.SizeOf<FileIdInfo>()))
            identity = id.Volume.ToString("X16") + ":" + id.Low.ToString("X16") + id.High.ToString("X16");
        long? allocated = null;
        if (!directory && GetStandardInfo(handle, 1, out var standard, (uint)Marshal.SizeOf<FileStandardInfo>()) && standard.AllocationSize >= 0 && standard.EndOfFile >= 0)
        { length = standard.EndOfFile; allocated = standard.AllocationSize; }
        metadata = new(length, allocated, ticks, identity, info.Links);
        return true;
    }
    private readonly record struct Metadata(long Length, long? Allocated, long LastWriteTicks, string Identity, uint Links);
    private sealed class Frame(string path, Metadata metadata)
    {
        public string Path { get; } = path;
        public Metadata Metadata { get; } = metadata;
        public bool Started, AllAllocated = true;
        public long Bytes, Allocated;
        public IEnumerator<FileSystemInfo>? Entries;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeTime { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct HandleInfo
    {
        public uint Attributes;
        public NativeTime Created, Accessed, LastWrite;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileStandardInfo
    {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending, Directory;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileIdInfo { public ulong Volume, Low, High; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo info);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetStandardInfo(SafeFileHandle handle, int infoClass, out FileStandardInfo info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileId(SafeFileHandle handle, int infoClass, out FileIdInfo info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder name, uint length, uint flags);
}
