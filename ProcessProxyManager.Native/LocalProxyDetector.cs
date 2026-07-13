using System.Net;
using Microsoft.Win32;
using ProcessProxyManager.Core;

namespace ProcessProxyManager.Native;

public sealed class LocalProxyDetector
{
    private static readonly string[] KnownProxyProcesses =
    [
        "clash",
        "clash verge",
        "clash-verge",
        "clash verge rev",
        "clash.meta",
        "mihomo",
        "nekoray",
        "v2rayn",
        "shadowsocks",
        "shadowsocksr",
        "ssr",
        "sing-box"
    ];

    private static readonly int[] CommonMixedPorts = [7890, 7897, 10809, 10808, 1080, 2080, 20170, 1087, 1086];
    private static readonly int[] CommonHttpPorts = [7890, 7897, 10809, 10808, 1080, 2080, 20171, 8118];
    private static readonly int[] CommonSocksPorts = [7891, 7898, 10808, 1080, 1086, 1087, 20170];

    public IReadOnlyList<DetectedProxyEndpoint> Detect(
        IReadOnlyList<NetworkConnectionSnapshot> connections)
    {
        var candidates = new List<DetectedProxyEndpoint>();
        AddSystemProxyCandidate(candidates);
        AddConfigFileCandidates(candidates);
        AddListeningPortCandidates(candidates, connections);

        return candidates
            .Where(static candidate => candidate.Port > 0 && candidate.Port <= 65535)
            .Where(static candidate => !IsProxyPilotEndpoint(candidate.Host, candidate.Port))
            .GroupBy(static candidate => $"{candidate.Type}:{candidate.Host}:{candidate.Port}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(candidate => candidate.Confidence).First())
            .OrderByDescending(static candidate => candidate.Confidence)
            .ThenBy(static candidate => candidate.Port)
            .ToList();
    }

    public DetectedProxyEndpoint? DetectBest(
        IReadOnlyList<NetworkConnectionSnapshot> connections)
    {
        return Detect(connections).FirstOrDefault();
    }

    private static void AddSystemProxyCandidate(List<DetectedProxyEndpoint> candidates)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (key?.GetValue("ProxyEnable") is not 1)
            {
                return;
            }

            var proxyServer = key.GetValue("ProxyServer")?.ToString();
            if (string.IsNullOrWhiteSpace(proxyServer))
            {
                return;
            }

            foreach (var endpoint in ParseProxyServer(proxyServer))
            {
                candidates.Add(endpoint with { Source = "Windows system proxy", Confidence = endpoint.Confidence + 35 });
            }
        }
        catch
        {
            // Registry proxy settings are best-effort hints only.
        }
    }

    private static void AddConfigFileCandidates(List<DetectedProxyEndpoint> candidates)
    {
        foreach (var path in GetLikelyConfigPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var text = File.ReadAllText(path);
                AddYamlPort(candidates, text, "mixed-port", "mixed", path, 34);
                AddYamlPort(candidates, text, "port", "http", path, 30);
                AddYamlPort(candidates, text, "socks-port", "socks5", path, 30);
            }
            catch
            {
                // User config files can be locked or malformed; skip them.
            }
        }
    }

    private static void AddListeningPortCandidates(
        List<DetectedProxyEndpoint> candidates,
        IReadOnlyList<NetworkConnectionSnapshot> connections)
    {
        foreach (var row in connections)
        {
            if (row.Protocol != "TCP" ||
                row.State != "LISTENING" ||
                !IsLoopback(row.LocalAddress) ||
                row.LocalPort <= 0)
            {
                continue;
            }

            if (IsKnownProxyProcess(row.ProcessName))
            {
                var processName = row.ProcessName;
                candidates.Add(new DetectedProxyEndpoint(
                    $"{processName} {row.LocalPort}",
                    GuessProxyType(row.LocalPort, processName),
                    "127.0.0.1",
                    row.LocalPort,
                    $"{processName} listening port",
                    28));
                continue;
            }

            if (CommonMixedPorts.Contains(row.LocalPort) ||
                CommonHttpPorts.Contains(row.LocalPort) ||
                CommonSocksPorts.Contains(row.LocalPort))
            {
                candidates.Add(new DetectedProxyEndpoint(
                    $"Local port {row.LocalPort}",
                    GuessProxyType(row.LocalPort, string.Empty),
                    "127.0.0.1",
                    row.LocalPort,
                    "Common local proxy port",
                    16));
            }
        }
    }

    private static IEnumerable<DetectedProxyEndpoint> ParseProxyServer(string proxyServer)
    {
        foreach (var part in proxyServer.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = part;
            var type = "http";

            if (part.Contains('=', StringComparison.Ordinal))
            {
                var pieces = part.Split('=', 2, StringSplitOptions.TrimEntries);
                type = NormalizeType(pieces[0]);
                value = pieces[1];
            }

            if (TryParseHostPort(value, out var host, out var port))
            {
                yield return new DetectedProxyEndpoint("Windows proxy", type, NormalizeHost(host), port, "Windows system proxy", 0);
            }
        }
    }

    private static void AddYamlPort(List<DetectedProxyEndpoint> candidates, string text, string key, string type, string path, int confidence)
    {
        foreach (var line in text.Split(Environment.NewLine))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = trimmed[(key.Length + 1)..].Trim().Trim('"', '\'');
            if (int.TryParse(value, out var port))
            {
                candidates.Add(new DetectedProxyEndpoint(
                    $"{Path.GetFileName(path)} {key}",
                    type,
                    "127.0.0.1",
                    port,
                    path,
                    confidence));
            }
        }
    }

    private static IReadOnlyList<string> GetLikelyConfigPaths()
    {
        var paths = new List<string>();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Add(paths, userProfile, ".config", "mihomo", "config.yaml");
        Add(paths, userProfile, ".config", "clash", "config.yaml");
        Add(paths, userProfile, ".config", "clash-verge", "config.yaml");
        Add(paths, appData, "clash", "config.yaml");
        Add(paths, appData, "Clash Verge", "config.yaml");
        Add(paths, appData, "clash-verge", "config.yaml");
        Add(paths, appData, "io.github.clash-verge-rev.clash-verge-rev", "config.yaml");
        Add(paths, localAppData, "Clash Verge", "config.yaml");
        Add(paths, localAppData, "clash-verge", "config.yaml");
        Add(paths, localAppData, "Programs", "Clash Verge", "resources", "config.yaml");

        return paths;
    }

    private static void Add(List<string> paths, params string[] parts)
    {
        if (parts.Any(string.IsNullOrWhiteSpace))
        {
            return;
        }

        paths.Add(Path.Combine(parts));
    }

    private static bool IsKnownProxyProcess(string processName)
    {
        return KnownProxyProcesses.Any(hint =>
            processName.Contains(hint, StringComparison.OrdinalIgnoreCase));
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

    private static bool TryParseHostPort(string value, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            port = uri.Port;
            return port > 0;
        }

        var endpoint = ParseEndpoint(value);
        host = endpoint.Address;
        port = endpoint.Port;
        return !string.IsNullOrWhiteSpace(host) && port > 0;
    }

    private static string NormalizeType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "socks" or "socks5" => "socks5",
            "https" or "http" => "http",
            _ => "http"
        };
    }

    private static string GuessProxyType(int port, string processName)
    {
        var normalizedProcessName = processName.ToLowerInvariant();

        if (normalizedProcessName.Contains("clash", StringComparison.Ordinal) ||
            normalizedProcessName.Contains("mihomo", StringComparison.Ordinal))
        {
            return port is 7890 or 7897 ? "http" : "socks5";
        }

        if (port is 1080 or 10808 or 1086 or 1087 or 20170)
        {
            return "socks5";
        }

        return "http";
    }

    private static string NormalizeHost(string host)
    {
        return host is "localhost" or "0.0.0.0" or "::" or "*" ? "127.0.0.1" : host;
    }

    private static bool IsLoopback(string address)
    {
        if (address is "127.0.0.1" or "0.0.0.0" or "::1" or "::" or "*")
        {
            return true;
        }

        return IPAddress.TryParse(address, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
    }

    private static bool IsProxyPilotEndpoint(string host, int port)
    {
        return port is AppSettings.ProxyPilotMixedPort or AppSettings.ProxyPilotApiPort &&
            IsLoopback(host);
    }

    private sealed record EndpointValue(string Address, int Port);
}
