using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Nyan.Core;

// This adapter accepts only source-owned scripts; it is never reachable from UI input.
internal sealed class ReadProcess : IDisposable
{
    private readonly SemaphoreSlim _slots = new(2, 2);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int,Process> _active = new();
    internal int[] ActiveProcessIds=>_active.Keys.ToArray();
    internal const int OutputLimit = 8 * 1024 * 1024;

    internal Task<ReadCommandResult> PowerShellAsync(string sourceScript, CancellationToken cancellationToken, TimeSpan? testTimeout = null)
    {
        if (testTimeout is { } shorter && (shorter <= TimeSpan.Zero || shorter > TimeSpan.FromSeconds(20)))
            throw new ArgumentOutOfRangeException(nameof(testTimeout));
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var script = "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';try{" + sourceScript
            + "}catch{if($_.Exception.HResult -eq -2147024891 -or $_.FullyQualifiedErrorId -match 'AccessDenied|UnauthorizedAccess'){[Console]::Error.WriteLine('NYAN_ACCESS_DENIED');exit 5};[Console]::Error.WriteLine('NYAN_READ_FAILED');exit 1}";
        return ExecuteAsync(executable, ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], testTimeout ?? TimeSpan.FromSeconds(20), cancellationToken, windowsPowerShell: true);
    }

    internal Task<ReadCommandResult> VersionAsync(string executable, string argument, CancellationToken cancellationToken)
    {
        if (argument is not ("--version" or "-version"))
            throw new ArgumentException("Chỉ cho phép đọc phiên bản.", nameof(argument));
        if (!IsTrustedTool(executable))
            return Task.FromResult(new ReadCommandResult(false, "", "unverified", false));
        return ExecuteAsync(executable, [argument], TimeSpan.FromSeconds(5), cancellationToken, toolProbe: true);
    }

    internal Task<ReadCommandResult> DockerStatusAsync(string executable, CancellationToken cancellationToken)
    {
        if (!Path.GetFileName(executable).Equals("docker.exe", StringComparison.OrdinalIgnoreCase) || !IsTrustedTool(executable))
            return Task.FromResult(new ReadCommandResult(false, "", "unverified", false));
        // Docker info does not start the daemon. Select only its version, never registry/account metadata.
        var isolatedConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NyanControlCenter", "DockerProbe-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(isolatedConfig)) return Task.FromResult(new ReadCommandResult(false, "", "unverified", false));
        return ExecuteAsync(executable, ["--config", isolatedConfig, "--host", "npipe:////./pipe/docker_engine", "info", "--format", "{{.ServerVersion}}"],
            TimeSpan.FromSeconds(5), cancellationToken, toolProbe: true, localDocker: true);
    }

    private async Task<ReadCommandResult> ExecuteAsync(string executable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken, bool toolProbe = false, bool localDocker = false, bool windowsPowerShell = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _slots.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            deadline.CancelAfter(timeout);
            using var process = new Process { StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            }};
            foreach (var name in new[] { "COR_ENABLE_PROFILING", "COR_PROFILER", "COR_PROFILER_PATH", "COR_PROFILER_PATH_32", "COR_PROFILER_PATH_64",
                "CORECLR_ENABLE_PROFILING", "CORECLR_PROFILER", "CORECLR_PROFILER_PATH", "CORECLR_PROFILER_PATH_32", "CORECLR_PROFILER_PATH_64" })
                process.StartInfo.Environment.Remove(name);
            if (windowsPowerShell)
            {
                // Do not auto-load commands from user-controlled PSModulePath directories.
                process.StartInfo.Environment["PSModulePath"] = Path.Combine(Path.GetDirectoryName(executable)!, "Modules");
                process.StartInfo.Environment.Remove("PSExecutionPolicyPreference");
            }
            if (toolProbe)
            {
                // Version switches must not load caller-configured hooks or agents.
                foreach (var name in new[] { "JAVA_TOOL_OPTIONS", "JDK_JAVA_OPTIONS", "_JAVA_OPTIONS", "NODE_OPTIONS", "NODE_PATH",
                    "PYTHONPATH", "PYTHONHOME", "PYTHONSTARTUP", "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE" })
                    process.StartInfo.Environment.Remove(name);
                process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
                process.StartInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
                process.StartInfo.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
            }
            if (localDocker)
                foreach (var name in new[] { "DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "DOCKER_CONFIG", "DOCKER_API_VERSION" })
                    process.StartInfo.Environment.Remove(name);
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            int ownedPid=0;
            try
            {
                if (!process.Start()) return new(false, "", "start-failed", false);
                ownedPid=process.Id;_active[ownedPid]=process;if(linked.IsCancellationRequested)TryKill(process);
                var stdout = CaptureAsync(process.StandardOutput, OutputLimit, deadline.Token);
                var stderr = CaptureAsync(process.StandardError, 16 * 1024, deadline.Token);
                try
                {
                    await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                    var output = await stdout.ConfigureAwait(false);
                    var error = await stderr.ConfigureAwait(false);
                    // Raw errors may contain personal paths or server names: never surface them.
                    var result = output.Text.Length == 0 ? error.Text : output.Text;
                    var errorCode = process.ExitCode == 5 || error.Text.Contains("NYAN_ACCESS_DENIED", StringComparison.Ordinal)
                        || error.Text.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
                        || error.Text.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
                        ? "access-denied" : "native-failed";
                    return process.ExitCode == 0 && !output.Truncated
                        ? new(true, result, "", false)
                        : new(false, output.Text, output.Truncated ? "output-limit" : errorCode, output.Truncated);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    using var reapDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try { await process.WaitForExitAsync(reapDeadline.Token).ConfigureAwait(false); }
                    catch (Exception e) when (e is InvalidOperationException or OperationCanceledException) { }
                    try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    linked.Token.ThrowIfCancellationRequested();
                    return new(false, "", "timeout", false);
                }
            }
            catch (Win32Exception e) { return new(false, "", e.NativeErrorCode == 5 ? "access-denied" : "unavailable", false); }
            catch (InvalidOperationException) { return new(false, "", "unavailable", false); }
            finally { if(ownedPid>0)_active.TryRemove(ownedPid,out _); }
        }
        finally { _slots.Release(); }
    }

    private static async Task<(string Text, bool Truncated)> CaptureAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var output = new StringBuilder(Math.Min(limit, 4096));
        var buffer = new char[4096];
        var truncated = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) break;
            var remaining = limit - output.Length;
            if (remaining > 0) output.Append(buffer, 0, Math.Min(count, remaining));
            if (count > remaining) truncated = true;
        }
        return (output.ToString(), truncated);
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    internal static bool IsTrustedTool(string executable)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(executable) || !File.Exists(executable)) return false;
            var full = Path.GetFullPath(executable);
            var allowed = new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            }.Any(root => !string.IsNullOrWhiteSpace(root) && full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
            if (!allowed || !full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            for (var current = new FileInfo(full).Directory; current != null; current = current.Parent)
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return false;
            return HasTrustedSignature(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or ExternalException)
        { return false; }
    }

    private static bool HasTrustedSignature(string path)
    {
        var file = new WinTrustFile { Size = (uint)Marshal.SizeOf<WinTrustFile>(), Path = path };
        var pointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFile>());
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            var data = new WinTrustData {
                Size = (uint)Marshal.SizeOf<WinTrustData>(), UIChoice = 2, UnionChoice = 1,
                File = pointer, ProviderFlags = 0x1000, StateAction = 1
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            data.StateAction = 2;
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            return result == 0;
        }
        finally { Marshal.DestroyStructure<WinTrustFile>(pointer); Marshal.FreeCoTaskMem(pointer); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle; public IntPtr KnownSubject; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size; public IntPtr PolicyCallback; public IntPtr SIPClientData; public uint UIChoice;
        public uint RevocationChecks; public uint UnionChoice; public IntPtr File; public uint StateAction;
        public IntPtr StateData; public IntPtr URLReference; public uint ProviderFlags; public uint UIContext; public IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    public void Dispose(){_lifetime.Cancel();foreach(var process in _active.Values)TryKill(process);}
}

internal sealed record ReadCommandResult(bool Success, string Output, string Code, bool Truncated);
