using ProcessProxyManager.Core;
using ProcessProxyManager.Mihomo;
using Xunit;

namespace ProcessProxyManager.Mihomo.Tests;

public sealed class MihomoConfigBuilderTests
{
    [Fact]
    public async Task WriteGeneratedConfigAsync_ReplacesRulesAndAddsTun()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProxyPilotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var templatePath = Path.Combine(tempRoot, "template.yaml");
        var generatedPath = Path.Combine(tempRoot, "generated.yaml");
        await File.WriteAllTextAsync(
            templatePath,
            """
            mixed-port: 7890
            rules:
              - MATCH,DIRECT
            """);

        var settings = new AppSettings
        {
            TemplateConfigPath = templatePath,
            GeneratedConfigPath = generatedPath,
            ApiUrl = "http://127.0.0.1:9090",
            Secret = "secret",
            EnableTun = true,
            UpstreamProxyName = "ProxyPilot Upstream",
            UpstreamProxyType = "http",
            UpstreamProxyHost = "127.0.0.1",
            UpstreamProxyPort = 7890
        };

        var builder = new MihomoConfigBuilder(new MihomoRuleGenerator());
        await builder.WriteGeneratedConfigAsync(
            settings,
            new[]
            {
                new ProcessRule { ProcessName = "BitComet.exe", Action = ProxyAction.PROXY },
                new ProcessRule { ProcessName = "WeChat.exe", Action = ProxyAction.DIRECT }
            });

        var generated = await File.ReadAllTextAsync(generatedPath);

        Assert.Contains("PROCESS-NAME,BitComet.exe,PROXY", generated);
        Assert.Contains("PROCESS-NAME,WeChat.exe,DIRECT", generated);
        Assert.Contains("external-controller: 127.0.0.1:9090", generated);
        Assert.Contains("secret: secret", generated);
        Assert.Contains("name: ProxyPilot Upstream", generated);
        Assert.Contains("server: 127.0.0.1", generated);
        Assert.Contains("port: 7890", generated);
        Assert.Contains("mixed-port: 17990", generated);
        Assert.Contains("find-process-mode: always", generated);
        Assert.Contains("tun:", generated);
        Assert.Contains("auto-route: true", generated);
    }

    [Fact]
    public async Task WriteGeneratedConfigAsync_DoesNotUseProxyPilotPortAsItsOwnUpstream()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProxyPilotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var settings = new AppSettings
        {
            TemplateConfigPath = Path.Combine(tempRoot, "template.yaml"),
            GeneratedConfigPath = Path.Combine(tempRoot, "generated.yaml"),
            ApiUrl = "http://127.0.0.1:19090",
            Secret = "secret",
            EnableTun = true,
            UpstreamProxyName = "ProxyPilot Upstream",
            UpstreamProxyType = "http",
            UpstreamProxyHost = "127.0.0.1",
            UpstreamProxyPort = AppSettings.ProxyPilotMixedPort
        };

        var builder = new MihomoConfigBuilder(new MihomoRuleGenerator());
        await builder.WriteGeneratedConfigAsync(
            settings,
            new[]
            {
                new ProcessRule { ProcessName = "chrome.exe", Action = ProxyAction.PROXY }
            });

        var generated = await File.ReadAllTextAsync(settings.GeneratedConfigPath);

        Assert.Contains("PROCESS-NAME,chrome.exe,PROXY", generated);
        Assert.DoesNotContain("name: ProxyPilot Upstream", generated);
        Assert.DoesNotContain("server: 127.0.0.1\r\n  port: 17990", generated);
        Assert.Contains("- DIRECT", generated);
    }

    [Fact]
    public async Task WriteGeneratedConfigAsync_WritesTunRouteExcludeAddresses()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProxyPilotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var settings = new AppSettings
        {
            TemplateConfigPath = Path.Combine(tempRoot, "template.yaml"),
            GeneratedConfigPath = Path.Combine(tempRoot, "generated.yaml"),
            EnableTun = true,
            TunRouteExcludeAddresses = ["13.212.80.185/32"]
        };

        var builder = new MihomoConfigBuilder(new MihomoRuleGenerator());
        await builder.WriteGeneratedConfigAsync(settings, []);

        var generated = await File.ReadAllTextAsync(settings.GeneratedConfigPath);

        Assert.Contains("route-exclude-address:", generated);
        Assert.Contains("- 13.212.80.185/32", generated);
    }

    [Fact]
    public async Task WriteGeneratedConfigAsync_AddsUpstreamProcessDirectRulesBeforeUserRules()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProxyPilotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var settings = new AppSettings
        {
            TemplateConfigPath = Path.Combine(tempRoot, "template.yaml"),
            GeneratedConfigPath = Path.Combine(tempRoot, "generated.yaml"),
            EnableTun = true,
            TunProcessDirectNames = ["ShadowsocksR-dotnet4.0.exe"]
        };

        var builder = new MihomoConfigBuilder(new MihomoRuleGenerator());
        await builder.WriteGeneratedConfigAsync(
            settings,
            [
                new ProcessRule { ProcessName = "chrome.exe", Action = ProxyAction.PROXY }
            ]);

        var generated = await File.ReadAllTextAsync(settings.GeneratedConfigPath);

        Assert.Contains("PROCESS-NAME,ShadowsocksR-dotnet4.0.exe,DIRECT", generated);
        Assert.True(
            generated.IndexOf("PROCESS-NAME,ShadowsocksR-dotnet4.0.exe,DIRECT", StringComparison.Ordinal) <
            generated.IndexOf("PROCESS-NAME,chrome.exe,PROXY", StringComparison.Ordinal));
    }
}
