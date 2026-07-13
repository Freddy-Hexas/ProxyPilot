using System.Text.Json;
using ProcessProxyManager.Core;
using ProcessProxyManager.Mihomo;
using Xunit;

namespace ProcessProxyManager.Mihomo.Tests;

public sealed class MihomoApiClientTests
{
    [Fact]
    public async Task CreateReloadContent_WithSettings_SendsAbsolutePath()
    {
        var configPath = Path.Combine(Path.GetTempPath(), "ProxyPilotTests", "config", "config.process-manager.yaml");
        var settings = new AppSettings { GeneratedConfigPath = configPath };

        using var content = MihomoApiClient.CreateReloadContent(settings);
        var json = await content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(Path.GetFullPath(configPath), document.RootElement.GetProperty("path").GetString());
    }
}
