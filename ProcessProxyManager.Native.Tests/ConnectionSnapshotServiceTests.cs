using ProcessProxyManager.Native;
using Xunit;

namespace ProcessProxyManager.Native.Tests;

public sealed class ConnectionSnapshotServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_ReturnsIpHelperConnections()
    {
        var service = new ConnectionSnapshotService();

        var snapshot = await service.GetSnapshotAsync(TimeSpan.Zero);

        Assert.NotEqual(default, snapshot.CapturedAt);
        Assert.NotNull(snapshot.Connections);
        Assert.All(snapshot.Connections, connection =>
        {
            Assert.True(connection.ProcessId >= 0);
            Assert.Contains(connection.Protocol, new[] { "TCP", "UDP" });
            Assert.InRange(connection.LocalPort, 0, 65535);
            Assert.InRange(connection.RemotePort, 0, 65535);
        });
    }
}
