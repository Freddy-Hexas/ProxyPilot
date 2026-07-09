namespace ProcessProxyManager.Core;

public sealed class ProcessRule
{
    public string ProcessName { get; set; } = string.Empty;

    public string ProcessPath { get; set; } = string.Empty;

    public ProxyAction Action { get; set; } = ProxyAction.None;
}
