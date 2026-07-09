namespace ProcessProxyManager.Native;

public sealed record ConnectionInspectionResult(
    int TotalConnections,
    int MihomoConnections,
    int SuspectedDirectConnections);
