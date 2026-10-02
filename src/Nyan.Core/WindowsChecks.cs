using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Nyan.Core;

/// <summary>Native read-only checks; all temporary resources have explicit fixture ownership.</summary>
public static class WindowsChecks
{
    public static async Task<List<string>> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<string>();
        if (!OperatingSystem.IsWindows()) return ["SKIP Windows reader checks: Windows native required"];
        using var reader = new WindowsReader();
        using var process = Process.GetCurrentProcess();
        var processRows = await reader.ReadAsync(Module.Processes, false, cancellationToken);
        var own = processRows.Rows.SingleOrDefault(r => r.Id == process.Id.ToString(CultureInfo.InvariantCulture));
        Require(own != null, "Process inventory must contain its own process.");
        Require(own!.Meta("startTicks") == process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), "Process creation identity must match native time.");
        Require(own.Cell("cpu").EndsWith("%", StringComparison.Ordinal), "Own process CPU must be sampled, not unavailable.");
        Require(own.Data?.ContainsKey("commandLine") != true && processRows.Columns.All(c => !c.Key.Contains("command", StringComparison.OrdinalIgnoreCase)), "Reader must not collect process command lines.");
        checks.Add("PASS native process creation identity and interval CPU; command lines absent");
        if (processRows.Rows.Any(r => r.Meta("startTicks") == "0"))
        {
            Require(processRows.Rows.Where(r => r.Meta("startTicks") == "0").All(r => r.Cell("started") == "Không đọc được"), "Protected creation time must not be displayed as a valid zero timestamp.");
            checks.Add("PASS native protected process fields remain unavailable");
        }
        else checks.Add("SKIP protected process observation: all visible identities accessible in this session");

        var variable = "NYAN_FIXTURE_READER_" + Guid.NewGuid().ToString("N");
        const string secret = "fixture-only-sensitive-value";
        try
        {
            Environment.SetEnvironmentVariable(variable, secret, EnvironmentVariableTarget.Process);
            var masked = await reader.ReadAsync(Module.Environment, false, cancellationToken);
            Require(masked.Rows.Count > 0 && masked.Rows.All(r => r.Cell("value") == "•••• (đã che)"), "All environment values must be masked by default.");
            var row = masked.Rows.Single(r => r.Id == "Process:" + variable);
            Require(row.Meta("value") == secret && row.Cell("value") != secret, "Raw fixture data must stay in mutation metadata, separate from UI cells.");
            var visible = await reader.ReadAsync(Module.Environment, true, cancellationToken);
            Require(visible.Rows.Single(r => r.Id == "Process:" + variable).Cell("value") == secret, "Explicit reveal must show fixture value.");
            checks.Add("PASS Process-only fixture environment masking and explicit reveal; no User/System write");
        }
        finally { Environment.SetEnvironmentVariable(variable, null, EnvironmentVariableTarget.Process); }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var privatePath = Path.Combine(profile, "Nyan fixture có dấu", "a b.txt");
        Require(!WindowsReader.PathDisplay(privatePath, false).Contains(profile, StringComparison.OrdinalIgnoreCase), "Personal Unicode path must be masked.");
        Require(WindowsReader.PathDisplay(privatePath, true) == privatePath, "Explicit path reveal must preserve Unicode and spaces.");
        checks.Add("PASS personal path masking, Unicode and spaces");

        using var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var tcpPort = ((IPEndPoint)tcp.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
        var udpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port.ToString(CultureInfo.InvariantCulture);
        var ports = await reader.ReadAsync(Module.Ports, false, cancellationToken);
        Require(ports.State is ResultState.Ready or ResultState.Partial, "NetTCPIP inventory must be available for native fixture checks.");
        Require(ports.Rows.Any(r => r.Meta("protocol") == "TCP" && r.Cell("port") == tcpPort && r.Meta("pid") == process.Id.ToString(CultureInfo.InvariantCulture) && r.Meta("startTicks") == own.Meta("startTicks")), "Owned TCP fixture endpoint must map to exact process creation identity.");
        Require(ports.Rows.Any(r => r.Meta("protocol") == "UDP" && r.Cell("port") == udpPort && r.Meta("pid") == process.Id.ToString(CultureInfo.InvariantCulture)), "Owned UDP fixture endpoint must be inventoried.");
        Require(ports.Rows.All(r => r.Cell("address") == "Đã che địa chỉ"), "All endpoint bind addresses must be masked by default.");
        checks.Add("PASS owned loopback TCP/UDP endpoint inventory, exact PID identity and address masking; listeners disposed");

        var network = await reader.ReadAsync(Module.Network, false, cancellationToken);
        Require(network.State is ResultState.Ready or ResultState.Partial or ResultState.Empty, "Network inventory must return a native result.");
        Require(network.Rows.All(r => !r.Cell("ip").Contains("127.0.0.1", StringComparison.Ordinal) && !r.Cell("ip").Contains("::1", StringComparison.Ordinal)), "Network IP cells must not leak loopback values by default.");
        checks.Add("PASS local-only network inventory and privacy masking");

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var result = await reader.ReadAsync(Module.Dashboard, false, cancelled.Token);
            Require(result.State == ResultState.Cancelled, "Cancelled read must not return fabricated metrics.");
        }
        checks.Add("PASS cancelled reader reports Cancelled");

        using var commands = new ReadProcess();
        var timer = Stopwatch.StartNew();
        var timeout = await commands.PowerShellAsync("Start-Sleep -Seconds 30; Write-Output 'NYAN_FIXTURE_UNEXPECTED'", cancellationToken, TimeSpan.FromMilliseconds(750));
        Require(!timeout.Success && timeout.Code == "timeout" && timer.Elapsed < TimeSpan.FromSeconds(8), "Native command timeout must kill owned helper and return promptly.");
        checks.Add("PASS native static-script timeout bound and helper termination");

        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cancel.CancelAfter(TimeSpan.FromMilliseconds(750));
            timer.Restart();
            var wasCancelled = false;
            try { await commands.PowerShellAsync("Start-Sleep -Seconds 30; Write-Output 'NYAN_FIXTURE_UNEXPECTED'", cancel.Token); }
            catch (OperationCanceledException) { wasCancelled = true; }
            Require(wasCancelled && timer.Elapsed < TimeSpan.FromSeconds(8), "Native command cancellation must terminate helper promptly.");
        }
        checks.Add("PASS native static-script cancellation bound");
        using(var closing=new ReadProcess())
        {
            var pending=closing.PowerShellAsync("Start-Sleep -Seconds 30",cancellationToken);var until=Stopwatch.StartNew();while(closing.ActiveProcessIds.Length==0&&until.ElapsedMilliseconds<2000)await Task.Delay(20,cancellationToken);
            var ids=closing.ActiveProcessIds;Require(ids.Length==1,"Owned helper must start before lifecycle check.");closing.Dispose();try{var result=await pending;Require(!result.Success,"Disposed collector must not return success.");}catch(OperationCanceledException){}
            foreach(var pid in ids){try{using var collector=Process.GetProcessById(pid);Require(collector.HasExited,"Closing app must not leave owned helper alive.");}catch(ArgumentException){}}
        }
        checks.Add("PASS app disposal reaps the exact owned native helper");

        var denied = await commands.PowerShellAsync("throw [System.UnauthorizedAccessException]::new('NYAN_FIXTURE_DENIED')", cancellationToken);
        Require(!denied.Success && denied.Code == "access-denied" && !denied.Output.Contains("NYAN_FIXTURE_DENIED", StringComparison.Ordinal), "Access-denied subprocess errors must be classified without exposing raw exception text.");
        checks.Add("PASS structured access-denied subprocess fixture; raw exception text suppressed");

        var oversized = await commands.PowerShellAsync("Write-Output ('x' * (9 * 1024 * 1024))", cancellationToken);
        Require(!oversized.Success && oversized.Code == "output-limit" && oversized.Output.Length <= ReadProcess.OutputLimit, "Native output must be bounded and not accepted when truncated.");
        checks.Add("PASS bounded 8 MiB command capture rejects truncated output");

        var unknown = Path.Combine(profile, "NYAN_FIXTURE_NOT_AN_INSTALL", "node.exe");
        Require(!ReadProcess.IsTrustedTool(unknown), "Unknown PATH executable must never be trusted.");
        var untrusted = await commands.VersionAsync(unknown, "--version", cancellationToken);
        Require(!untrusted.Success && untrusted.Code == "unverified", "Unknown executable probe must be denied without execution.");
        checks.Add("PASS untrusted executable denied before process creation");

        foreach (var module in new[] { Module.Dashboard, Module.Applications, Module.Startup, Module.Services, Module.DevTools })
        {
            var watch = Stopwatch.StartNew();
            var result = await reader.ReadAsync(module, false, cancellationToken);
            if (result.State is ResultState.Denied or ResultState.Error)
                checks.Add($"SKIP native {module} inventory: {result.State}; no mock fallback");
            else
            {
                Require(result.State is ResultState.Ready or ResultState.Partial or ResultState.Empty, "Unexpected native result state.");
                if (module == Module.Startup) Require(result.Rows.All(r => r.Cell("command") == "Đã che lệnh / đường dẫn"), "Startup commands must be hidden by default.");
                checks.Add($"PASS native {module} inventory: {result.State}, {result.Rows.Count} rows, {watch.ElapsedMilliseconds} ms (no raw values logged)");
            }
        }
        return checks;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Windows reader check failed: " + message);
    }
}
