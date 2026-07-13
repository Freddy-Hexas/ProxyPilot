using ProcessProxyManager.Core;
using YamlDotNet.RepresentationModel;

namespace ProcessProxyManager.Mihomo;

public sealed class MihomoConfigBuilder
{
    private readonly MihomoRuleGenerator _ruleGenerator;

    public MihomoConfigBuilder(MihomoRuleGenerator ruleGenerator)
    {
        _ruleGenerator = ruleGenerator;
    }

    public async Task WriteGeneratedConfigAsync(
        AppSettings settings,
        IEnumerable<ProcessRule> rules,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = ResolveTemplatePath(settings);
        var yaml = await LoadTemplateAsync(sourcePath, cancellationToken);
        var root = GetOrCreateRoot(yaml);

        root.Children[new YamlScalarNode("external-controller")] = new YamlScalarNode(GetControllerValue(settings.ApiUrl));
        root.Children[new YamlScalarNode("secret")] = new YamlScalarNode(settings.Secret ?? string.Empty);
        root.Children[new YamlScalarNode("mixed-port")] = new YamlScalarNode(AppSettings.ProxyPilotMixedPort.ToString());
        root.Children[new YamlScalarNode("allow-lan")] = new YamlScalarNode("false");
        root.Children[new YamlScalarNode("find-process-mode")] = new YamlScalarNode("always");
        root.Children[new YamlScalarNode("mode")] = new YamlScalarNode("rule");
        root.Children[new YamlScalarNode("proxies")] = BuildProxiesNode(settings);
        root.Children[new YamlScalarNode("proxy-groups")] = BuildProxyGroupsNode(settings);
        root.Children[new YamlScalarNode("rules")] = BuildRulesNode(rules, settings.TunProcessDirectNames);

        if (settings.EnableTun)
        {
            root.Children[new YamlScalarNode("tun")] = BuildTunNode(settings);
            root.Children[new YamlScalarNode("dns")] = BuildDnsNode();
        }

        var targetPath = ResolveGeneratedConfigPath(settings);
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StringWriter();
        yaml.Save(writer, assignAnchors: false);
        await File.WriteAllTextAsync(targetPath, writer.ToString(), cancellationToken);
    }

    public async Task EnsureDefaultTemplateAsync(string templatePath, CancellationToken cancellationToken = default)
    {
        if (File.Exists(templatePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(templatePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        const string template = """
            mixed-port: 17990
            allow-lan: false
            find-process-mode: always
            mode: rule
            log-level: info
            external-controller: 127.0.0.1:19090
            secret: ""
            proxies: []
            proxy-groups:
              - name: PROXY
                type: select
                proxies:
                  - DIRECT
            rules:
              - MATCH,DIRECT
            """;

        await File.WriteAllTextAsync(templatePath, template.ReplaceLineEndings(Environment.NewLine), cancellationToken);
    }

    private async Task<YamlStream> LoadTemplateAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            await EnsureDefaultTemplateAsync(sourcePath, cancellationToken);
        }

        await using var stream = File.OpenRead(sourcePath);
        using var reader = new StreamReader(stream);
        var yaml = new YamlStream();
        yaml.Load(reader);
        return yaml;
    }

    private static YamlMappingNode GetOrCreateRoot(YamlStream yaml)
    {
        if (yaml.Documents.Count == 0)
        {
            var root = new YamlMappingNode();
            yaml.Documents.Add(new YamlDocument(root));
            return root;
        }

        if (yaml.Documents[0].RootNode is YamlMappingNode mapping)
        {
            return mapping;
        }

        var replacement = new YamlMappingNode();
        yaml.Documents[0] = new YamlDocument(replacement);
        return replacement;
    }

    private YamlSequenceNode BuildRulesNode(IEnumerable<ProcessRule> rules, IEnumerable<string>? processDirectNames = null)
    {
        var node = new YamlSequenceNode();

        foreach (var line in _ruleGenerator.GenerateRuleLines(rules, processDirectNames))
        {
            node.Children.Add(new YamlScalarNode(line));
        }

        return node;
    }

    private static YamlSequenceNode BuildProxiesNode(AppSettings settings)
    {
        var node = new YamlSequenceNode();

        if (!HasValidUpstream(settings))
        {
            return node;
        }

        var proxyType = NormalizeProxyType(settings.UpstreamProxyType);
        node.Children.Add(new YamlMappingNode
        {
            { "name", GetUpstreamName(settings) },
            { "type", proxyType },
            { "server", string.IsNullOrWhiteSpace(settings.UpstreamProxyHost) ? "127.0.0.1" : settings.UpstreamProxyHost },
            { "port", settings.UpstreamProxyPort.ToString() }
        });

        return node;
    }

    private static YamlSequenceNode BuildProxyGroupsNode(AppSettings settings)
    {
        var groupProxies = new YamlSequenceNode("DIRECT");

        if (HasValidUpstream(settings))
        {
            groupProxies.Children.Insert(0, new YamlScalarNode(GetUpstreamName(settings)));
        }

        return new YamlSequenceNode(
            new YamlMappingNode
            {
                { "name", "PROXY" },
                { "type", "select" },
                { "proxies", groupProxies }
            });
    }

    private static YamlMappingNode BuildTunNode(AppSettings settings)
    {
        var node = new YamlMappingNode
        {
            { "enable", "true" },
            { "stack", "mixed" },
            { "dns-hijack", new YamlSequenceNode("any:53") },
            { "auto-route", "true" },
            { "auto-detect-interface", "true" }
        };

        var routeExcludeAddresses = settings.TunRouteExcludeAddresses
            .Where(static address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (routeExcludeAddresses.Count > 0)
        {
            node.Children[new YamlScalarNode("route-exclude-address")] =
                new YamlSequenceNode(routeExcludeAddresses.Select(static address => new YamlScalarNode(address)));
        }

        return node;
    }

    private static YamlMappingNode BuildDnsNode()
    {
        return new YamlMappingNode
        {
            { "enable", "true" },
            { "enhanced-mode", "fake-ip" },
            { "nameserver", new YamlSequenceNode("223.5.5.5", "8.8.8.8") }
        };
    }

    private static string ResolveTemplatePath(AppSettings settings)
    {
        return Path.GetFullPath(settings.TemplateConfigPath);
    }

    private static string ResolveGeneratedConfigPath(AppSettings settings)
    {
        return Path.GetFullPath(settings.GeneratedConfigPath);
    }

    private static string GetControllerValue(string apiUrl)
    {
        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri))
        {
            return apiUrl.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .TrimEnd('/');
        }

        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    private static string GetUpstreamName(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.UpstreamProxyName)
            ? "ProxyPilot Upstream"
            : settings.UpstreamProxyName;
    }

    private static string NormalizeProxyType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "socks" or "socks5" => "socks5",
            "http" or "https" or "mixed" => "http",
            _ => "mixed"
        };
    }

    private static bool HasValidUpstream(AppSettings settings)
    {
        return settings.UpstreamProxyPort > 0 &&
            !IsProxyPilotEndpoint(settings.UpstreamProxyHost, settings.UpstreamProxyPort);
    }

    private static bool IsProxyPilotEndpoint(string host, int port)
    {
        return (port is AppSettings.ProxyPilotMixedPort or AppSettings.ProxyPilotApiPort) &&
            IsLoopbackHost(host);
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::", StringComparison.OrdinalIgnoreCase);
    }
}
