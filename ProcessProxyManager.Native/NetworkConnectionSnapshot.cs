namespace ProcessProxyManager.Native;

public sealed record NetworkConnectionSnapshot(
    int ProcessId,
    string ProcessName,
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State);
