using ProcessProxyManager.Mihomo;
using ProcessProxyManager.Native;
using Xunit;

namespace ProcessProxyManager.Native.Tests;

public sealed class ConnectionAnalysisTests
{
    private static readonly NetworkConnectionSnapshot[] NativeConnections =
    [
        new(10, "chrome.exe", "TCP", "127.0.0.1", 50100, "127.0.0.1", 17990, "ESTABLISHED"),
        new(10, "chrome.exe", "TCP", "10.0.0.2", 50101, "8.8.8.8", 443, "ESTABLISHED"),
        new(20, "mihomo.exe", "TCP", "127.0.0.1", 50102, "127.0.0.1", 20088, "ESTABLISHED")
    ];

    [Fact]
    public void Inspect_UsesProvidedSnapshotWithoutRescanning()
    {
        var inspector = new ConnectionInspector();
        var mihomoConnections = new[]
        {
            new MihomoConnection(
                "tcp",
                "chrome.exe",
                string.Empty,
                "127.0.0.1",
                50100,
                "1.1.1.1",
                443,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                [])
        };

        var results = inspector.Inspect(mihomoConnections, NativeConnections);

        Assert.Equal(new ConnectionInspectionResult(2, 1, 1), results[10]);
    }

    [Fact]
    public void UpstreamAnalyzers_ShareTheSameSnapshot()
    {
        var traffic = new UpstreamTrafficDetector();
        var loopback = new UpstreamLoopbackDetector();

        var counts = traffic.CountByProcessId(20088, NativeConnections);
        var loopbackResult = loopback.Detect(20088, NativeConnections);

        Assert.Equal(1, counts[20]);
        Assert.False(loopbackResult.HasRisk);
    }
}
