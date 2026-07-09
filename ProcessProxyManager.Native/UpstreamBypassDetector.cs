using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    public IReadOnlyList<string> DetectProcessDirectNames(int upstreamPort)
    {
        if (upstreamPort <= 0)
        {
            return [];
        }

        var names = new List<string>();
        names.AddRange(DetectListeningProxyProcessNames(upstreamPort));

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

    private static IEnumerable<string> DetectListeningProxyProcessNames(int upstreamPort)
    {
        foreach (var row in ReadNetstatRows())
        {
            if (row.Protocol != "TCP" ||
                row.State != "LISTENING" ||
                row.LocalPort != upstreamPort ||
                !IsLoopback(row.LocalAddress))
            {
                continue;
            }

            var processName = GetProcessName(row.ProcessId);
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

    private static IReadOnlyList<NetstatRow> ReadNetstatRows()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netstat.exe",
            Arguments = "-ano",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return [];
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return ParseNetstat(output);
    }

    private static IReadOnlyList<NetstatRow> ParseNetstat(string output)
    {
        var rows = new List<NetstatRow>();

        foreach (var rawLine in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("UDP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                continue;
            }

            var protocol = parts[0].ToUpperInvariant();
            var local = ParseEndpoint(parts[1]);

            if (protocol == "TCP" && parts.Length >= 5 && int.TryParse(parts[^1], out var tcpProcessId))
            {
                var state = parts.Length >= 5 ? parts[3] : string.Empty;
                rows.Add(new NetstatRow(protocol, local.Address, local.Port, state, tcpProcessId));
            }
            else if (protocol == "UDP" && int.TryParse(parts[^1], out var udpProcessId))
            {
                rows.Add(new NetstatRow(protocol, local.Address, local.Port, string.Empty, udpProcessId));
            }
        }

        return rows;
    }

    private static EndpointValue ParseEndpoint(string value)
    {
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var endBracket = value.LastIndexOf(']');
            if (endBracket >= 0)
            {
                var address = value[1..endBracket];
                var portText = value[(endBracket + 1)..].TrimStart(':');
                return new EndpointValue(address, int.TryParse(portText, out var port) ? port : 0);
            }
        }

        var separator = value.LastIndexOf(':');
        if (separator <= 0)
        {
            return new EndpointValue(value, 0);
        }

        var endpointAddress = value[..separator];
        var endpointPort = int.TryParse(value[(separator + 1)..], out var parsedPort) ? parsedPort : 0;
        return new EndpointValue(endpointAddress, endpointPort);
    }

    private static bool IsLoopback(string address)
    {
        if (address is "127.0.0.1" or "0.0.0.0" or "::1" or "::" or "*")
        {
            return true;
        }

        return IPAddress.TryParse(address, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? process.ProcessName
                : $"{process.ProcessName}.exe";
        }
        catch
        {
            return string.Empty;
        }
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

        foreach (var dnsServer in new[] { "223.5.5.5", "8.8.8.8" })
        {
            foreach (var address in ResolveWithNslookup(host, dnsServer))
            {
                yield return ToCidr(address);
            }
        }
    }

    private static IEnumerable<IPAddress> ResolveWithNslookup(string host, string dnsServer)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nslookup.exe",
                Arguments = $"{host} {dnsServer}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return IpAddressRegex()
                .Matches(output)
                .Select(match => match.Value)
                .Where(value => !string.Equals(value, dnsServer, StringComparison.OrdinalIgnoreCase))
                .Select(value => IPAddress.TryParse(value, out var address) ? address : null)
                .Where(static address => address is not null && !IsFakeIp(address))
                .Cast<IPAddress>()
                .ToList();
        }
        catch
        {
            return [];
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

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex IpAddressRegex();

    private sealed record NetstatRow(string Protocol, string LocalAddress, int LocalPort, string State, int ProcessId);

    private sealed record EndpointValue(string Address, int Port);

    private sealed record SsrEndpoint(int LocalPort, string Server);
}

public sealed record UpstreamBypassEndpoint(string ProcessFamily, string Host, string Source);
