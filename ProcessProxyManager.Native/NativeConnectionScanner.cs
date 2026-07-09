using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace ProcessProxyManager.Native;

public sealed class NativeConnectionScanner
{
    public IReadOnlyList<NetworkConnectionSnapshot> GetConnections()
    {
        var tcpConnections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
        var netstatRows = ReadNetstatRows();
        var snapshots = new List<NetworkConnectionSnapshot>();

        foreach (var row in netstatRows)
        {
            var state = row.Protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase)
                ? FindTcpState(tcpConnections, row.LocalAddress, row.LocalPort, row.RemoteAddress, row.RemotePort)
                : string.Empty;

            snapshots.Add(new NetworkConnectionSnapshot(
                row.ProcessId,
                GetProcessName(row.ProcessId),
                row.Protocol,
                row.LocalAddress,
                row.LocalPort,
                row.RemoteAddress,
                row.RemotePort,
                state));
        }

        return snapshots;
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

            if (protocol == "TCP" && parts.Length >= 5)
            {
                var remote = ParseEndpoint(parts[2]);
                if (int.TryParse(parts[4], out var processId))
                {
                    rows.Add(new NetstatRow(protocol, local.Address, local.Port, remote.Address, remote.Port, processId));
                }
            }
            else if (protocol == "UDP" && int.TryParse(parts[^1], out var processId))
            {
                rows.Add(new NetstatRow(protocol, local.Address, local.Port, string.Empty, 0, processId));
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

    private static string FindTcpState(
        TcpConnectionInformation[] connections,
        string localAddress,
        int localPort,
        string remoteAddress,
        int remotePort)
    {
        foreach (var connection in connections)
        {
            if (EndpointMatches(connection.LocalEndPoint, localAddress, localPort) &&
                EndpointMatches(connection.RemoteEndPoint, remoteAddress, remotePort))
            {
                return connection.State.ToString();
            }
        }

        return string.Empty;
    }

    private static bool EndpointMatches(IPEndPoint endpoint, string address, int port)
    {
        return endpoint.Port == port &&
               (address == "*" ||
                address == "0.0.0.0" ||
                address == "::" ||
                string.Equals(endpoint.Address.ToString(), address, StringComparison.OrdinalIgnoreCase));
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

    private sealed record NetstatRow(
        string Protocol,
        string LocalAddress,
        int LocalPort,
        string RemoteAddress,
        int RemotePort,
        int ProcessId);

    private sealed record EndpointValue(string Address, int Port);
}
