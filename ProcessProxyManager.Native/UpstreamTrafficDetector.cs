namespace ProcessProxyManager.Native;

public sealed class UpstreamTrafficDetector
{
    private readonly NativeConnectionScanner _connectionScanner;

    public UpstreamTrafficDetector(NativeConnectionScanner connectionScanner)
    {
        _connectionScanner = connectionScanner;
    }

    public IReadOnlyDictionary<int, int> CountByProcessId(int upstreamPort)
    {
        if (upstreamPort <= 0)
        {
            return new Dictionary<int, int>();
        }

        return _connectionScanner.GetConnections()
            .Where(connection => connection.RemotePort == upstreamPort && IsLoopback(connection.RemoteAddress))
            .GroupBy(static connection => connection.ProcessId)
            .ToDictionary(static group => group.Key, static group => group.Count());
    }

    private static bool IsLoopback(string address)
    {
        return string.IsNullOrWhiteSpace(address) ||
            address is "127.0.0.1" or "::1" or "localhost" or "0.0.0.0" or "::" ||
            address.StartsWith("127.", StringComparison.OrdinalIgnoreCase);
    }
}
