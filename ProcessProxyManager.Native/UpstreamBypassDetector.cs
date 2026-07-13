using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace ProcessProxyManager.Native;

public sealed partial class UpstreamBypassDetector
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

    private static readonly string[] SsrProcessNames =
    [
        "ShadowsocksR-dotnet4.0",
        "ShadowsocksR-dotnet2.0",
        "ShadowsocksR"
    ];

    public IReadOnlyList<string> DetectRouteExcludeCidrs(int upstreamPort)
    {
        if (upstreamPort <= 0)
        {
            return [];
        }

        return DetectSsrRouteExcludeCidrs(upstreamPort)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> DetectProcessDirectNames(
        int upstreamPort,
        IReadOnlyList<NetworkConnectionSnapshot> connections)
    {
        if (upstreamPort <= 0)
        {
            return [];
        }

        var names = new List<string>();
        names.AddRange(DetectListeningProxyProcessNames(upstreamPort, connections));

        if (DetectSsrEndpoints(upstreamPort).Any())
        {
            names.AddRange(SsrProcessNames.Select(static name => $"{name}.exe"));
        }

        return names
            .Select(NormalizeProcessName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<UpstreamBypassEndpoint> DetectEndpoints(int upstreamPort)
    {
        if (upstreamPort <= 0)
        {
            return [];
        }

        return DetectSsrEndpoints(upstreamPort)
            .Distinct()
            .ToList();
    }

    private static IEnumerable<string> DetectSsrRouteExcludeCidrs(int upstreamPort)
    {
        foreach (var endpoint in DetectSsrEndpoints(upstreamPort))
        {
            foreach (var cidr in ResolveHostToCidrs(endpoint.Host))
            {
                yield return cidr;
            }
        }
    }

    private static IEnumerable<UpstreamBypassEndpoint> DetectSsrEndpoints(int upstreamPort)
    {
        foreach (var configPath in GetSsrConfigCandidates())
        {
            SsrEndpoint? endpoint;
            try
            {
                endpoint = ReadSsrEndpoint(configPath);
            }
            catch
            {
                continue;
            }

            if (endpoint is null || endpoint.LocalPort != upstreamPort || string.IsNullOrWhiteSpace(endpoint.Server))
            {
                continue;
            }

            yield return new UpstreamBypassEndpoint("ShadowsocksR", endpoint.Server, configPath);
        }
    }

    private static IEnumerable<string> DetectListeningProxyProcessNames(
        int upstreamPort,
        IReadOnlyList<NetworkConnectionSnapshot> connections)
    {
        foreach (var row in connections)
        {
            if (row.Protocol != "TCP" ||
                row.State != "LISTENING" ||
                row.LocalPort != upstreamPort ||
                !IsLoopback(row.LocalAddress))
            {
                continue;
            }

            var processName = row.ProcessName;
            if (string.IsNullOrWhiteSpace(processName) ||
                !KnownProxyProcessNameHints.Any(hint => processName.Contains(hint, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return processName;
        }
    }

    private static IEnumerable<string> GetSsrConfigCandidates()
    {
        var paths = new List<string>();

        foreach (var processName in SsrProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var executablePath = process.MainModule?.FileName;
                        var directory = string.IsNullOrWhiteSpace(executablePath)
                            ? string.Empty
                            : Path.GetDirectoryName(executablePath);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            paths.Add(Path.Combine(directory, "gui-config.json"));
                        }
                    }
                    catch
                    {
                        // Some process paths are not accessible; skip them.
                    }
                }
            }
        }

        return paths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsLoopback(string address)
    {
        if (address is "127.0.0.1" or "0.0.0.0" or "::1" or "::" or "*")
        {
            return true;
        }

        return IPAddress.TryParse(address, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
    }

    private static string NormalizeProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : $"{processName}.exe";
    }

    private static SsrEndpoint? ReadSsrEndpoint(string configPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = document.RootElement;

        if (!root.TryGetProperty("localPort", out var localPortElement) ||
            !root.TryGetProperty("index", out var indexElement) ||
            !root.TryGetProperty("configs", out var configsElement) ||
            configsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var localPort = localPortElement.GetInt32();
        var index = indexElement.GetInt32();
        if (index < 0 || index >= configsElement.GetArrayLength())
        {
            return null;
        }

        var serverElement = configsElement[index];
        if (!serverElement.TryGetProperty("server", out var serverProperty))
        {
            return null;
        }

        return new SsrEndpoint(localPort, serverProperty.GetString() ?? string.Empty);
    }

    private static IEnumerable<string> ResolveHostToCidrs(string host)
    {
        if (IPAddress.TryParse(host, out var literalAddress))
        {
            yield return ToCidr(literalAddress);
            yield break;
        }

        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(host);
        }
        catch
        {
            yield break;
        }

        foreach (var address in addresses.Where(static address => !IsFakeIp(address)))
        {
            yield return ToCidr(address);
        }
    }

    private static string ToCidr(IPAddress address)
    {
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"{address}/128"
            : $"{address}/32";
    }

    private static bool IsFakeIp(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 198 && bytes[1] is 18 or 19;
    }

    private sealed record SsrEndpoint(int LocalPort, string Server);
}

public sealed record UpstreamBypassEndpoint(string ProcessFamily, string Host, string Source);
