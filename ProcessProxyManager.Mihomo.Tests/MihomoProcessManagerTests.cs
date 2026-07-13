using ProcessProxyManager.Core;
using ProcessProxyManager.Mihomo;
using Xunit;

namespace ProcessProxyManager.Mihomo.Tests;

public sealed class MihomoProcessManagerTests
{
    [Fact]
    public void CreateStartInfo_UsesGeneratedConfigDirectoryAsHomeAndSafePath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ProxyPilotTests", Guid.NewGuid().ToString("N"));
        var resources = Path.Combine(tempRoot, "resources");
        var config = Path.Combine(tempRoot, "config");
        Directory.CreateDirectory(resources);
        Directory.CreateDirectory(config);

        var mihomoPath = Path.Combine(resources, "mihomo.exe");
        var generatedPath = Path.Combine(config, "config.process-manager.yaml");
        File.WriteAllText(mihomoPath, string.Empty);
        File.WriteAllText(generatedPath, "rules: []");

        var startInfo = MihomoProcessManager.CreateStartInfo(new AppSettings
        {
            MihomoPath = mihomoPath,
            GeneratedConfigPath = generatedPath
        });

        Assert.Equal(config, startInfo.WorkingDirectory);
        Assert.Contains($"-d \"{config}\"", startInfo.Arguments);
        Assert.Contains("-f \"config.process-manager.yaml\"", startInfo.Arguments);
        Assert.Contains(config, startInfo.Environment["SAFE_PATHS"]);
    }
}
