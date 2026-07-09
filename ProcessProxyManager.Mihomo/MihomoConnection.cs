namespace ProcessProxyManager.Mihomo;

public sealed record MihomoConnection(
    string Network,
    string Process,
    string ProcessPath,
    string SourceIp,
    int SourcePort,
    string DestinationIp,
    int DestinationPort,
    string Host,
    string RemoteDestination,
    string Rule,
    string RulePayload,
    IReadOnlyList<string> Chains);
