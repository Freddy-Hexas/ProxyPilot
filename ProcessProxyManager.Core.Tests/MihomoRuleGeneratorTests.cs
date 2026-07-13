using ProcessProxyManager.Core;
using Xunit;

namespace ProcessProxyManager.Core.Tests;

public sealed class MihomoRuleGeneratorTests
{
    [Fact]
    public void GenerateRulesYaml_EmitsProcessNameRulesAndMatchFallback()
    {
        var generator = new MihomoRuleGenerator();
        var rules = new[]
        {
            new ProcessRule { ProcessName = "WeChat.exe", ProcessPath = @"C:\Apps\WeChat.exe", Action = ProxyAction.DIRECT },
            new ProcessRule { ProcessName = "BitComet.exe", ProcessPath = @"C:\Apps\BitComet.exe", Action = ProxyAction.PROXY },
            new ProcessRule { ProcessName = "Ignored.exe", ProcessPath = @"C:\Apps\Ignored.exe", Action = ProxyAction.None }
        };

        var yaml = generator.GenerateRulesYaml(rules);

        Assert.Equal(
            """
            rules:
              - PROCESS-NAME,BitComet.exe,PROXY
              - PROCESS-NAME,WeChat.exe,DIRECT
              - MATCH,DIRECT

            """.ReplaceLineEndings(Environment.NewLine),
            yaml);
    }

    [Fact]
    public void GenerateRulesYaml_EmitsProcessDirectRulesFirst()
    {
        var generator = new MihomoRuleGenerator();
        var rules = new[]
        {
            new ProcessRule { ProcessName = "chrome.exe", Action = ProxyAction.PROXY }
        };

        var yaml = generator.GenerateRulesYaml(rules, ["ShadowsocksR.exe"]);

        Assert.Equal(
            """
            rules:
              - PROCESS-NAME,ShadowsocksR.exe,DIRECT
              - PROCESS-NAME,chrome.exe,PROXY
              - MATCH,DIRECT

            """.ReplaceLineEndings(Environment.NewLine),
            yaml);
    }
}
