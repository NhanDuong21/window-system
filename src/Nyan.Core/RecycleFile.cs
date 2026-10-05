using System.Runtime.InteropServices;

namespace Nyan.Core;

public sealed record RecycleResult(bool Recycled, string? RecyclePath);

/// <summary>
/// Recycles a single previously quarantined local file. The caller owns mutation preview,
/// quarantine identity, and recovery. This class never falls back to permanent deletion.
/// </summary>
public static class RecycleFile
{
    private const uint OperationFlags = 0x0004 /* FOF_SILENT */ | 0x0010 /* FOF_NOCONFIRMATION */
        | 0x0200 /* FOF_NOCONFIRMMKDIR */ | 0x0400 /* FOF_NOERRORUI */
        | 0x00080000 /* FOFX_RECYCLEONDELETE */ | 0x00100000 /* FOFX_EARLYFAILURE */
        | 0x00800000 /* FOFX_NOCOPYHOOKS */ | 0x20000000 /* FOFX_ADDUNDORECORD */;
    private const int Abort = unchecked((int)0x80004004);
    private static readonly SemaphoreSlim OperationSlot = new(1, 1);

    public static async Task<RecycleResult> RecycleAsync(string quarantinePath)
    {
        if (!OperatingSystem.IsWindows()) return new(false, null);
        await OperationSlot.WaitAsync().ConfigureAwait(false);
        try
        {
            var completion = new TaskCompletionSource<RecycleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { completion.TrySetResult(RecycleOnSta(quarantinePath)); }
                catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or AppException or ArgumentException or System.ComponentModel.Win32Exception)
                { completion.TrySetResult(new(false, null)); }
                catch (Exception) { completion.TrySetResult(new(false, null)); }
            }) { IsBackground = true, Name = "Nyan recycle STA" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return await completion.Task.ConfigureAwait(false);
        }
        finally { OperationSlot.Release(); }
    }

    private static RecycleResult RecycleOnSta(string path)
    {
        var full = Path.GetFullPath(path);
        NativeSecurity.CheckLocalPath(full);
        if (new DriveInfo(Path.GetPathRoot(full)!).DriveType != DriveType.Fixed) return new(false, null);
        string identity; long length; long writeTicks;
        using (var lease = new FileLease(full, true)) { identity = lease.Identity; length = lease.Length; writeTicks = lease.WriteTicks; }
        var init = CoInitializeEx(IntPtr.Zero, 2 /* COINIT_APARTMENTTHREADED */);
        if (init < 0) return new(false, null);
        IFileOperation? operation = null;
        IShellItem? source = null;
        var advised = false; uint cookie = 0;
        try
        {
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!)!;
            var shellItemId = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(full, IntPtr.Zero, ref shellItemId, out source) < 0 || source is null) return new(false, null);
            var sink = new RecycleSink(full, identity, length, writeTicks);
            if (operation.SetOperationFlags(OperationFlags) < 0 || operation.Advise(sink, out cookie) < 0) return new(false, null);
            advised = true;
            // Advise supplies the sink globally. Passing it again would duplicate notifications.
            if (operation.DeleteItem(source, null) < 0) return new(false, null);
            var performed = operation.PerformOperations();
            var abortedResult = operation.GetAnyOperationsAborted(out var aborted);
            var result = sink.Result;
            var confirmed = performed >= 0 && abortedResult >= 0 && !aborted && result.Recycled && !File.Exists(full);
            GC.KeepAlive(sink);
            return new(confirmed, result.RecyclePath);
        }
        finally
        {
            if (advised && operation is not null) operation.Unadvise(cookie);
            if (source is not null && Marshal.IsComObject(source)) Marshal.FinalReleaseComObject(source);
            if (operation is not null && Marshal.IsComObject(operation)) Marshal.FinalReleaseComObject(operation);
            CoUninitialize();
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class RecycleSink : IFileOperationProgressSink
    {
        private readonly string _sourcePath;
        private readonly string _identity;
        private readonly long _length;
        private readonly long _writeTicks;
        private bool _postDelete;
        private bool _recycled;
        private int _finishResult;
        private string? _recyclePath;
        private readonly object _gate = new();

        public RecycleSink(string path, string identity, long length, long writeTicks)
        { _sourcePath = path; _identity = identity; _length = length; _writeTicks = writeTicks; }

        public RecycleResult Result { get { lock (_gate) return new(_postDelete && _recycled && _finishResult >= 0, _recyclePath); } }
        public int StartOperations() => 0;
        public int FinishOperations(int result) { lock (_gate) _finishResult = result; return 0; }
        public int PreDeleteItem(uint flags, IShellItem item)
        {
            // The Shell may change operation flags; veto a non-recycle delete before it happens.
            if ((flags & 0x80 /* TSF_DELETE_RECYCLE_IF_POSSIBLE */) == 0) return Abort;
            try
            {
                if (!string.Equals(DisplayName(item, 0x80058000 /* SIGDN_FILESYSPATH */), _sourcePath, StringComparison.OrdinalIgnoreCase)) return Abort;
                using var lease = new FileLease(_sourcePath, true);
                return lease.Identity == _identity && lease.Length == _length && lease.WriteTicks == _writeTicks ? 0 : Abort;
            }
            catch { return Abort; }
        }
        public int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? newlyCreated)
        {
            lock (_gate)
            {
                _postDelete = true;
                // Microsoft documents NULL as permanent deletion. It is never accepted as recycled.
                if (result < 0 || newlyCreated is null) return Abort;
                _recycled = true;
                try
                {
                    _recyclePath = DisplayName(newlyCreated, 0x80058000 /* SIGDN_FILESYSPATH */)
                        ?? DisplayName(newlyCreated, 0x80028000 /* SIGDN_DESKTOPABSOLUTEPARSING */);
                }
                catch { _recyclePath = null; }
                return 0;
            }
        }
        public int PreRenameItem(uint flags, IShellItem item, string name) => Abort;
        public int PostRenameItem(uint flags, IShellItem item, string name, int result, IShellItem? created) => Abort;
        public int PreMoveItem(uint flags, IShellItem item, IShellItem destination, string? name) => Abort;
        public int PostMoveItem(uint flags, IShellItem item, IShellItem destination, string? name, int result, IShellItem? created) => Abort;
        public int PreCopyItem(uint flags, IShellItem item, IShellItem destination, string? name) => Abort;
        public int PostCopyItem(uint flags, IShellItem item, IShellItem destination, string? name, int result, IShellItem? created) => Abort;
        public int PreNewItem(uint flags, IShellItem destination, string name) => Abort;
        public int PostNewItem(uint flags, IShellItem destination, string name, string? template, uint attributes, int result, IShellItem? created) => Abort;
        public int UpdateProgress(uint total, uint completed) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }

    private static string? DisplayName(IShellItem item, uint displayKind)
    {
        var pointer = IntPtr.Zero;
        try { return item.GetDisplayName(displayKind, out pointer) >= 0 && pointer != IntPtr.Zero ? Marshal.PtrToStringUni(pointer) : null; }
        finally { if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
    }

    // Vtable order follows the Microsoft Windows SDK shobjidl_core.h declarations.
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint displayKind, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem item, uint hint, out int order);
    }
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IFileOperationProgressSink sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr owner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink);
        [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);
        [PreserveSig] int MoveItems(IntPtr items, IShellItem destination);
        [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);
        [PreserveSig] int CopyItems(IntPtr items, IShellItem destination);
        [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IFileOperationProgressSink? sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [ComImport, ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, int result, IShellItem? created);
        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name);
        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, IShellItem? created);
        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name);
        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? name, int result, IShellItem? created);
        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int result, IShellItem? newlyCreated);
        [PreserveSig] int PreNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int PostNewItem(uint flags, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, uint attributes, int result, IShellItem? created);
        [PreserveSig] int UpdateProgress(uint total, uint completed);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
