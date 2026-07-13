using ProcessProxyManager.Core;
using Xunit;

namespace ProcessProxyManager.Core.Tests;

public sealed class ProcessScannerTests
{
    [Fact]
    public void GetRunningProcesses_IncludesMemoryMetadata()
    {
        var scanner = new ProcessScanner();

        var processes = scanner.GetRunningProcesses();

        Assert.NotEmpty(processes);
        Assert.All(processes, process =>
        {
            Assert.True(process.ProcessId >= 0);
            Assert.False(string.IsNullOrWhiteSpace(process.ProcessName));
            Assert.True(process.WorkingSetBytes >= 0);
        });
    }
}
