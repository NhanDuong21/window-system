using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Nyan.Core;

namespace Nyan.App;

/// <summary>Launch this app in the same user's interactive session with a verified non-admin token.</summary>
internal static class UserLauncher
{
    private const uint TokenQuery = 8, TokenDuplicate = 2, MaximumAllowed = 0x02000000;
    private const int TokenSessionId = 12, TokenElevationType = 18, TokenLinkedToken = 19, TokenElevation = 20, TokenIntegrityLevel = 25;
    private const int MediumIntegrity = 0x2000;
    private const uint CreateSuspended = 4, CreateUnicodeEnvironment = 0x400, CreateNoWindow = 0x08000000;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Startup
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
        public ushort Show, Reserved2;
        public IntPtr ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint Pid, Tid; }
    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token, uint access, IntPtr security, int level, int type, out SafeAccessTokenHandle duplicate);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, IntPtr data, int size, out int needed);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(SafeAccessTokenHandle token, int kind, IntPtr data, int size);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool CreateRestrictedToken(SafeAccessTokenHandle token, uint flags, uint disableCount, IntPtr disableSids, uint deleteCount, IntPtr deletePrivileges, uint restrictCount, IntPtr restrictSids, out SafeAccessTokenHandle restricted);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, uint logon, string application, StringBuilder command, uint flags, IntPtr environment, string currentDirectory, ref Startup startup, out ProcessInfo info);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetEnvironmentStringsW();
    [DllImport("kernel32.dll")] private static extern bool FreeEnvironmentStringsW(IntPtr environment);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    public static void Start(string executable, string[] arguments)
    {
        var application = Path.GetFullPath(executable);
        if (!application.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), StringComparison.OrdinalIgnoreCase) || application.Contains('\0') || arguments.Any(x => x.Contains('\0')))
            throw new AppException("launch_scope", "Launcher chỉ được mở lại chính ứng dụng Nyan.");
        using var current = Process.GetCurrentProcess();
        if (!OpenProcessToken(current.Handle, TokenQuery | TokenDuplicate, out var ownToken)) throw LastError();
        using (ownToken)
        using (var identity = new WindowsIdentity(ownToken.DangerousGetHandle()))
        {
            var sid = identity.User ?? throw new AppException("launch_identity", "Không xác minh được tài khoản của ứng dụng.");
            using var ordinary = FindOrdinaryToken(sid, current.SessionId) ?? CreateOrdinaryToken(ownToken, sid, current.SessionId);
            LaunchVerified(ordinary, sid, current.SessionId, application, arguments);
        }
    }
    private static SafeAccessTokenHandle? FindOrdinaryToken(SecurityIdentifier sid, int session)
    {
        var shells = Process.GetProcessesByName("explorer");
        try
        {
            foreach (var shell in shells)
            {
                try
                {
                    if (shell.SessionId != session) continue;
                    var process = OpenProcess(0x1000, false, shell.Id);
                    if (process == IntPtr.Zero) continue;
                    try
                    {
                        if (!OpenProcessToken(process, TokenQuery | TokenDuplicate, out var shellToken)) continue;
                        using (shellToken)
                        {
                            if (!SameUserAndSession(shellToken, sid, session)) continue;
                            if (IsOrdinary(shellToken, sid, session)) return DuplicatePrimary(shellToken);
                            using var linked = GetLimitedLinkedToken(shellToken, sid, session);
                            if (linked is not null) return DuplicatePrimary(linked);
                        }
                    }
                    finally { CloseHandle(process); }
                }
                catch (Win32Exception) { /* Shell exited or denied access; own-token fallback remains safe. */ }
                catch (InvalidOperationException) { /* Process disappeared before token inspection. */ }
            }
        }
        finally { foreach (var shell in shells) shell.Dispose(); }
        return null;
    }
    private static SafeAccessTokenHandle CreateOrdinaryToken(SafeAccessTokenHandle own, SecurityIdentifier sid, int session)
    {
        using var linked = GetLimitedLinkedToken(own, sid, session);
        if (linked is not null) return DuplicatePrimary(linked);
        using var primary = DuplicatePrimary(own);
        // DISABLE_MAX_PRIVILEGE | LUA_TOKEN reduces only our own duplicated token. No system policy changes.
        if (!CreateRestrictedToken(primary, 0x1 | 0x4, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, out var restricted)) throw LastError();
        try
        {
            SetMediumIntegrity(restricted);
            if (!IsOrdinary(restricted, sid, session)) throw new AppException("launch_token", "Windows không cung cấp token user thường đã xác minh; ứng dụng chưa được mở.");
            return restricted;
        }
        catch { restricted.Dispose(); throw; }
    }
    private static SafeAccessTokenHandle DuplicatePrimary(SafeAccessTokenHandle token)
    {
        if (!DuplicateTokenEx(token, MaximumAllowed, IntPtr.Zero, 2, 1, out var duplicate)) throw LastError();
        return duplicate;
    }
    private static SafeAccessTokenHandle? GetLimitedLinkedToken(SafeAccessTokenHandle token, SecurityIdentifier sid, int session)
    {
        if (ReadInt(token, TokenElevationType) != 2) return null;
        var buffer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (!GetTokenInformation(token, TokenLinkedToken, buffer, IntPtr.Size, out _)) return null;
            var linked = new SafeAccessTokenHandle(Marshal.ReadIntPtr(buffer));
            try { if (IsOrdinary(linked, sid, session)) return linked; }
            catch { linked.Dispose(); throw; }
            linked.Dispose(); return null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static bool SameUserAndSession(SafeAccessTokenHandle token, SecurityIdentifier sid, int session)
    {
        using var identity = new WindowsIdentity(token.DangerousGetHandle());
        return identity.User?.Equals(sid) == true && ReadInt(token, TokenSessionId) == session;
    }
    private static bool IsOrdinary(SafeAccessTokenHandle token, SecurityIdentifier sid, int session)
    {
        using var identity = new WindowsIdentity(token.DangerousGetHandle());
        return identity.User?.Equals(sid) == true && ReadInt(token, TokenSessionId) == session && ReadInt(token, TokenElevation) == 0
            && Integrity(token) <= MediumIntegrity && !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    private static int ReadInt(SafeAccessTokenHandle token, int kind)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try { if (!GetTokenInformation(token, kind, buffer, sizeof(int), out _)) throw LastError(); return Marshal.ReadInt32(buffer); }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static int Integrity(SafeAccessTokenHandle token)
    {
        GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var size);
        if (size is < 16 or > 4096) throw new AppException("launch_integrity", "Không đọc được mức integrity của token.");
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _)) throw LastError();
            var sid = Marshal.ReadIntPtr(buffer);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            if (count == 0) throw new AppException("launch_integrity", "Token không có mức integrity hợp lệ.");
            return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static void SetMediumIntegrity(SafeAccessTokenHandle token)
    {
        var sid = new SecurityIdentifier("S-1-16-8192");
        var bytes = new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes, 0);
        var sidBuffer = Marshal.AllocHGlobal(bytes.Length);
        var labelBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<SidAndAttributes>());
        try
        {
            Marshal.Copy(bytes, 0, sidBuffer, bytes.Length);
            Marshal.StructureToPtr(new SidAndAttributes { Sid = sidBuffer, Attributes = 0x20 }, labelBuffer, false);
            if (!SetTokenInformation(token, TokenIntegrityLevel, labelBuffer, Marshal.SizeOf<SidAndAttributes>() + bytes.Length)) throw LastError();
        }
        finally { Marshal.FreeHGlobal(labelBuffer); Marshal.FreeHGlobal(sidBuffer); }
    }
    private static void LaunchVerified(SafeAccessTokenHandle token, SecurityIdentifier sid, int session, string application, string[] arguments)
    {
        if (!IsOrdinary(token, sid, session)) throw new AppException("launch_token", "Token đích chưa đạt quyền user thường.");
        var command = new StringBuilder(string.Join(' ', new[] { application }.Concat(arguments).Select(QuoteArgument)));
        if (command.Length >= 1024) throw new AppException("launch_arguments", "Đường dẫn hoặc tham số mở lại vượt giới hạn Windows của launcher.");
        var startup = new Startup { Size = Marshal.SizeOf<Startup>(), Desktop = @"winsta0\default" };
        // Preserve the same-user parent's environment opaquely, including the project-local DOTNET_ROOT.
        // It is passed as a native block only: no values are enumerated, recorded or logged.
        var environment = GetEnvironmentStringsW();
        if (environment == IntPtr.Zero) throw LastError();
        ProcessInfo info; bool created; int error;
        try
        {
            created = CreateProcessWithTokenW(token, 0, application, command, CreateSuspended | CreateUnicodeEnvironment | CreateNoWindow, environment, AppContext.BaseDirectory, ref startup, out info);
            error = Marshal.GetLastWin32Error();
        }
        finally { FreeEnvironmentStringsW(environment); }
        if (!created) throw new Win32Exception(error);
        var resumed = false;
        try
        {
            if (!OpenProcessToken(info.Process, TokenQuery | TokenDuplicate, out var child)) throw LastError();
            using (child)
                if (!IsOrdinary(child, sid, session)) throw new AppException("launch_verification", "Process mới không đạt quyền user thường; Nyan đã dừng process do launcher vừa tạo.");
            if (ResumeThread(info.Thread) == uint.MaxValue) throw LastError();
            resumed = true;
            if (WaitForSingleObject(info.Process, 1000) == 0 && GetExitCodeProcess(info.Process, out var exit) && exit != 0)
                throw new AppException("launch_startup", $"Process user thường đã dừng khi khởi động (mã 0x{exit:X8}). Dữ liệu Windows chưa được thay đổi.");
        }
        finally
        {
            if (!resumed) TerminateProcess(info.Process, 1); // Only our newly created, still-suspended process.
            CloseHandle(info.Thread); CloseHandle(info.Process);
        }
    }
    internal static string QuoteArgument(string argument)
    {
        // Windows argv quoting, including backslashes before quotes and at the end of an argument.
        var output = new StringBuilder("\""); var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') { output.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
            output.Append('\\', slashes).Append(character); slashes = 0;
        }
        return output.Append('\\', slashes * 2).Append('"').ToString();
    }
    private static Win32Exception LastError() => new(Marshal.GetLastWin32Error());
}
