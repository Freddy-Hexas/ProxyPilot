namespace ProcessProxyManager.Native;

public sealed record SystemProxySnapshot(
    bool Enabled,
    string ProxyServer,
    string ProxyOverride,
    string AutoConfigUrl);
