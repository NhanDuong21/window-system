using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nyan.Core;

internal sealed record NativeEndpoint(string Protocol, string LocalAddress, string LocalPort,
    string RemoteAddress, string RemotePort, string State, string Pid);
internal sealed record NativeServiceEntry(string Name, string DisplayName, string State, string StartMode,
    string Path, string ServiceType, List<string>? Dependencies);
internal sealed record NativeInventory<T>(List<T> Items, int FailedSources, bool Denied = false);

/// <summary>Local read-only IP Helper / Service Control Manager inventory with no WMI or elevation.</summary>
internal static class WindowsNativeInventory
{
    private const int PortBufferLimit = 8 * 1024 * 1024;
    private const int ServiceBufferLimit = 256 * 1024;

    internal static NativeInventory<NativeEndpoint> ReadEndpoints(CancellationToken token)
    {
        var rows = new List<NativeEndpoint>();
        var failures = 0; var denied = false;
        foreach (var family in new[] { 2 /* AF_INET */, 23 /* AF_INET6 */ })
        foreach (var tcp in new[] { true, false })
        {
            token.ThrowIfCancellationRequested();
            try { ReadEndpointTable(rows, family, tcp, token); }
            catch (Win32Exception e) { failures++; denied |= e.NativeErrorCode == 5; }
            catch (InvalidDataException) { failures++; }
        }
        return new(rows, failures, denied);
    }

    private static void ReadEndpointTable(List<NativeEndpoint> rows, int family, bool tcp, CancellationToken token)
    {
        uint needed = 0;
        var initial = EndpointTable(IntPtr.Zero, ref needed, family, tcp);
        if (initial is not (0 or 122)) throw new Win32Exception((int)initial);
        if (needed == 0 && initial == 0) return;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (needed < 4 || needed > PortBufferLimit - 4096) throw new InvalidDataException("Endpoint inventory exceeds native buffer bound.");
            var capacity = checked((int)needed + 4096);
            var buffer = Marshal.AllocHGlobal(capacity);
            try
            {
                var size = (uint)capacity;
                var result = EndpointTable(buffer, ref size, family, tcp);
                if (result == 122) { needed = size; continue; }
                if (result != 0) throw new Win32Exception((int)result);
                var count = UInt32(buffer, 0);
                var stride = family == 2 ? tcp ? 24 : 12 : tcp ? 56 : 28;
                if ((long)count * stride + 4 > capacity) throw new InvalidDataException("Endpoint table is truncated.");
                for (var index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = buffer + 4 + index * stride;
                    string local; string remote = ""; uint localPort; uint remotePort = 0; uint state = 0; uint pid;
                    if (family == 2 && tcp)
                    {
                        state = UInt32(row, 0); local = IPv4(row, 4); localPort = UInt32(row, 8);
                        remote = IPv4(row, 12); remotePort = UInt32(row, 16); pid = UInt32(row, 20);
                    }
                    else if (family == 2)
                    { local = IPv4(row, 0); localPort = UInt32(row, 4); pid = UInt32(row, 8); }
                    else if (tcp)
                    {
                        local = IPv6(row, 0, UInt32(row, 16)); localPort = UInt32(row, 20);
                        remote = IPv6(row, 24, UInt32(row, 40)); remotePort = UInt32(row, 44);
                        state = UInt32(row, 48); pid = UInt32(row, 52);
                    }
                    else
                    { local = IPv6(row, 0, UInt32(row, 16)); localPort = UInt32(row, 20); pid = UInt32(row, 24); }
                    rows.Add(new(tcp ? "TCP" : "UDP", local, Port(localPort), remote,
                        tcp ? Port(remotePort) : "", tcp ? TcpState(state) : "", pid.ToString(CultureInfo.InvariantCulture)));
                }
                return;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidDataException("Endpoint table changed repeatedly during read.");
    }

    private static uint EndpointTable(IntPtr buffer, ref uint size, int family, bool tcp) => tcp
        ? GetExtendedTcpTable(buffer, ref size, false, family, 5 /* TCP_TABLE_OWNER_PID_ALL */, 0)
        : GetExtendedUdpTable(buffer, ref size, false, family, 1 /* UDP_TABLE_OWNER_PID */, 0);
    private static uint UInt32(IntPtr pointer, int offset) => unchecked((uint)Marshal.ReadInt32(pointer, offset));
    private static string Port(uint value) => ((ushort)IPAddress.NetworkToHostOrder(unchecked((short)(value & 0xffff)))).ToString(CultureInfo.InvariantCulture);
    private static string IPv4(IntPtr pointer, int offset)
    {
        var bytes = new byte[4]; Marshal.Copy(pointer + offset, bytes, 0, bytes.Length); return new IPAddress(bytes).ToString();
    }
    private static string IPv6(IntPtr pointer, int offset, uint networkScope)
    {
        var bytes = new byte[16]; Marshal.Copy(pointer + offset, bytes, 0, bytes.Length);
        var scope = unchecked((uint)IPAddress.NetworkToHostOrder(unchecked((int)networkScope)));
        return new IPAddress(bytes, scope).ToString();
    }
    private static string TcpState(uint state) => state switch
    {
        1 => "Closed", 2 => "Listen", 3 => "SynSent", 4 => "SynReceived", 5 => "Established", 6 => "FinWait1",
        7 => "FinWait2", 8 => "CloseWait", 9 => "Closing", 10 => "LastAck", 11 => "TimeWait", 12 => "DeleteTCB", _ => "Không rõ"
    };

    internal static NativeInventory<NativeServiceEntry> ReadServices(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var manager = OpenSCManager(null, null, 0x0001 | 0x0004 /* CONNECT | ENUMERATE_SERVICE */);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var rows = new List<NativeServiceEntry>();
        var failures = 0; var denied = false; uint resume = 0;
        var buffer = Marshal.AllocHGlobal(ServiceBufferLimit);
        try
        {
            for (var page = 0; page < 64; page++)
            {
                token.ThrowIfCancellationRequested();
                var success = EnumServicesStatusEx(manager, 0, 0x30 /* SERVICE_WIN32 */, 3 /* SERVICE_STATE_ALL */,
                    buffer, ServiceBufferLimit, out _, out var count, ref resume, null);
                var error = success ? 0 : Marshal.GetLastWin32Error();
                if (error is not (0 or 234 /* ERROR_MORE_DATA */))
                {
                    if (rows.Count == 0) throw new Win32Exception(error);
                    failures++; denied |= error == 5; break;
                }
                var stride = Marshal.SizeOf<ServiceStatusEntry>();
                if ((long)count * stride > ServiceBufferLimit) throw new InvalidDataException("Service table is truncated.");
                for (var index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var item = Marshal.PtrToStructure<ServiceStatusEntry>(buffer + index * stride);
                    var name = BufferText(item.Name, buffer, ServiceBufferLimit);
                    var display = BufferText(item.DisplayName, buffer, ServiceBufferLimit);
                    var mode = ""; var path = ""; List<string>? dependencies = null;
                    try
                    {
                        var config = ReadConfig(manager, name);
                        mode = StartMode(config.StartType); path = config.Path; dependencies = config.Dependencies;
                    }
                    catch (Win32Exception e) { failures++; denied |= e.NativeErrorCode == 5; }
                    catch (InvalidDataException) { failures++; }
                    rows.Add(new(name, display, ServiceState(item.State), mode, path,
                        item.ServiceType.ToString(CultureInfo.InvariantCulture), dependencies));
                }
                if (success) return new(rows, failures, denied);
                if (count == 0 || resume == 0) { failures++; break; }
            }
            return new(rows, failures + (resume != 0 ? 1 : 0), denied);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static (uint StartType, string Path, List<string> Dependencies) ReadConfig(ServiceHandle manager, string name)
    {
        using var service = OpenService(manager, name, 0x0001 /* SERVICE_QUERY_CONFIG only */);
        if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        QueryServiceConfig(service, IntPtr.Zero, 0, out var needed);
        var error = Marshal.GetLastWin32Error();
        if (error != 122) throw new Win32Exception(error);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (needed < Marshal.SizeOf<ServiceConfig>() || needed > 8192) throw new InvalidDataException("Service config exceeds documented native bound.");
            var capacity = (int)needed;
            var buffer = Marshal.AllocHGlobal(capacity);
            try
            {
                if (!QueryServiceConfig(service, buffer, (uint)capacity, out needed))
                {
                    error = Marshal.GetLastWin32Error(); if (error == 122) continue; throw new Win32Exception(error);
                }
                var config = Marshal.PtrToStructure<ServiceConfig>(buffer);
                return (config.StartType, BufferText(config.BinaryPath, buffer, capacity), BufferMultiString(config.Dependencies, buffer, capacity));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidDataException("Service config changed repeatedly during read.");
    }

    private static string BufferText(IntPtr pointer, IntPtr start, int length)
    {
        if (pointer == IntPtr.Zero) return "";
        var offset = pointer.ToInt64() - start.ToInt64();
        if (offset < 0 || offset >= length || (offset & 1) != 0) throw new InvalidDataException("Native text pointer outside buffer.");
        var available = (length - (int)offset) / 2;
        for (var chars = 0; chars < available; chars++)
            if (Marshal.ReadInt16(pointer, chars * 2) == 0) return Marshal.PtrToStringUni(pointer, chars) ?? "";
        throw new InvalidDataException("Native text is not terminated.");
    }
    private static List<string> BufferMultiString(IntPtr pointer, IntPtr start, int length)
    {
        var values = new List<string>();
        if (pointer == IntPtr.Zero) return values;
        var offset = pointer.ToInt64() - start.ToInt64();
        if (offset < 0 || offset >= length) throw new InvalidDataException("Native dependency pointer outside buffer.");
        while (offset < length - 1)
        {
            if (Marshal.ReadInt16(pointer) == 0) return values;
            var value = BufferText(pointer, start, length); values.Add(value);
            var advance = (value.Length + 1) * 2; pointer += advance; offset += advance;
        }
        throw new InvalidDataException("Native dependencies are not terminated.");
    }
    private static string ServiceState(uint state) => state switch
    { 1 => "Stopped", 2 => "Start Pending", 3 => "Stop Pending", 4 => "Running", 5 => "Continue Pending", 6 => "Pause Pending", 7 => "Paused", _ => "Không rõ" };
    private static string StartMode(uint mode) => mode switch
    { 0 => "Boot", 1 => "System", 2 => "Auto", 3 => "Manual", 4 => "Disabled", _ => "Không rõ" };

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusEntry
    {
        public IntPtr Name, DisplayName;
        public uint ServiceType, State, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceConfig
    {
        public uint ServiceType, StartType, ErrorControl;
        public IntPtr BinaryPath, LoadOrderGroup; public uint TagId; public IntPtr Dependencies, StartName, DisplayName;
    }
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusEx(ServiceHandle manager, int level, uint type, uint state,
        IntPtr services, int capacity, out int needed, out uint count, ref uint resume, string? group);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfig(ServiceHandle service, IntPtr config, uint capacity, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
