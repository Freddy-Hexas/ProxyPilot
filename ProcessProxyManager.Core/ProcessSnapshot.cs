namespace ProcessProxyManager.Core;

public sealed class ProcessSnapshot
{
    public required int ProcessId { get; init; }

    public required long StartTimeUtcTicks { get; init; }

    public required string ProcessName { get; init; }

    public required string ProcessPath { get; init; }

    public required long WorkingSetBytes { get; init; }

    public string FileDescription { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public string MainWindowTitle { get; init; } = string.Empty;
}
