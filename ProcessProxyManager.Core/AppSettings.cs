namespace ProcessProxyManager.Core;

public sealed class AppSettings
{
    public const int ProxyPilotMixedPort = 17990;

    public const int ProxyPilotApiPort = 19090;

    public string MihomoPath { get; set; } = string.Empty;

    public string TemplateConfigPath { get; set; } = string.Empty;

    public string GeneratedConfigPath { get; set; } = string.Empty;

    public string RuleSnippetPath { get; set; } = string.Empty;

    public string ApiUrl { get; set; } = "http://127.0.0.1:19090";

    public string Secret { get; set; } = string.Empty;

    public bool EnableTun { get; set; } = true;

    public bool CloseMihomoOnExit { get; set; } = true;

    public string Language { get; set; } = "zh-CN";

    public bool AutoDetectUpstreamProxy { get; set; } = true;

    public string UpstreamProxyName { get; set; } = "ProxyPilot Upstream";

    public string UpstreamProxyType { get; set; } = "http";

    public string UpstreamProxyHost { get; set; } = "127.0.0.1";

    public int UpstreamProxyPort { get; set; }

    public string UpstreamProxySource { get; set; } = string.Empty;

    public List<string> TunRouteExcludeAddresses { get; set; } = [];

    public List<string> TunProcessDirectNames { get; set; } = [];

    public bool SystemProxyTakeoverEnabled { get; set; } = true;

    public bool SystemProxyRestoreAvailable { get; set; }

    public bool PreviousSystemProxyEnabled { get; set; }

    public string PreviousSystemProxyServer { get; set; } = string.Empty;

    public string PreviousSystemProxyOverride { get; set; } = string.Empty;

    public string PreviousSystemProxyAutoConfigUrl { get; set; } = string.Empty;
}
