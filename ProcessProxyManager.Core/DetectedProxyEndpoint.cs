namespace ProcessProxyManager.Core;

public sealed record DetectedProxyEndpoint(
    string Name,
    string Type,
    string Host,
    int Port,
    string Source,
    int Confidence);
