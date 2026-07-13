using System.Diagnostics;
using ProcessProxyManager.Core;

namespace ProcessProxyManager.Native;

public sealed class UpstreamLoopbackDetector
{
    private static readonly string[] KnownProxyProcessNameHints =
    [
        "clash",
        "clash verge",
        "clash-verge",
        "clash.meta",
        "mihomo",
        "nekoray",
        "v2rayn",
        "shadowsocks",
        "shadowsocksr",
        "ssr",
        "sing-box"
    ];

    public UpstreamLoopbackResult Detect(
        int upstreamPort,
        IReadOnlyList<NetworkConnectionSnapshot> connections)
    {
        if (upstreamPort <= 0)
        {
            return new UpstreamLoopbackResult(false, 0, []);
        }

        var processIds = connections
            .Where(connection =>
                connection.RemotePort == upstreamPort &&
                IsLoopback(connection.RemoteAddress) &&
                IsKnownProxyProcess(connection.ProcessName))
            .Select(static connection => connection.ProcessId)
            .Distinct()
            .ToList();

        return new UpstreamLoopbackResult(processIds.Count > 0, processIds.Count, processIds);
    }

    private static bool IsLoopback(string address)
    {
        return string.IsNullOrWhiteSpace(address) ||
            address is "127.0.0.1" or "::1" or "localhost" or "0.0.0.0" or "::" ||
            address.StartsWith("127.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownProxyProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        if (processName.Equals("ProxyPilot.exe", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("mihomo.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return KnownProxyProcessNameHints.Any(hint => processName.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record UpstreamLoopbackResult(bool HasRisk, int ConnectionCount, IReadOnlyList<int> ProcessIds);
