using ProcessProxyManager.Mihomo;

namespace ProcessProxyManager.Native;

public sealed class ConnectionInspector
{

    public IReadOnlyDictionary<int, ConnectionInspectionResult> Inspect(
        IReadOnlyList<MihomoConnection> mihomoConnections,
        IReadOnlyList<NetworkConnectionSnapshot> nativeConnections)
    {
        var mihomoKeys = mihomoConnections
            .SelectMany(BuildMihomoKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return nativeConnections
            .GroupBy(static connection => connection.ProcessId)
            .ToDictionary(
                static group => group.Key,
                group =>
                {
                    var total = group.Count();
                    var handled = group.Count(connection => BuildNativeKeys(connection).Any(mihomoKeys.Contains));
                    return new ConnectionInspectionResult(total, handled, Math.Max(0, total - handled));
                });
    }

    private static IEnumerable<string> BuildNativeKeys(NetworkConnectionSnapshot connection)
    {
        if (connection.LocalPort > 0)
        {
            yield return $"{connection.Protocol}:{connection.LocalAddress}:{connection.LocalPort}";
            yield return $"{connection.Protocol}:*:{connection.LocalPort}";
        }
    }

    private static IEnumerable<string> BuildMihomoKeys(MihomoConnection connection)
    {
        var protocol = string.IsNullOrWhiteSpace(connection.Network) ? "TCP" : connection.Network.ToUpperInvariant();

        if (connection.SourcePort > 0)
        {
            yield return $"{protocol}:{connection.SourceIp}:{connection.SourcePort}";
            yield return $"{protocol}:*:{connection.SourcePort}";
        }
    }
}
