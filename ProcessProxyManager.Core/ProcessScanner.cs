using System.Diagnostics;
using System.Collections.Concurrent;

namespace ProcessProxyManager.Core;

public sealed class ProcessScanner
{
    private readonly ConcurrentDictionary<string, ProcessMetadata> _metadataCache =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ProcessSnapshot> GetRunningProcesses()
    {
        var snapshots = Process.GetProcesses()
            .Select(TryCreateSnapshot)
            .Where(static process => process is not null)
            .Cast<ProcessSnapshot>()
            .OrderByDescending(static process => process.WorkingSetBytes)
            .ThenBy(static process => process.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static process => process.ProcessId)
            .ToList();

        var activePaths = snapshots
            .Select(static process => process.ProcessPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var cachedPath in _metadataCache.Keys)
        {
            if (!activePaths.Contains(cachedPath))
            {
                _metadataCache.TryRemove(cachedPath, out _);
            }
        }

        return snapshots;
    }

    private ProcessSnapshot? TryCreateSnapshot(Process process)
    {
        using (process)
        {
            var fallbackName = GetFallbackProcessName(process);
            var startTimeUtcTicks = GetStartTimeUtcTicks(process);
            var workingSetBytes = GetWorkingSetBytes(process);
            var mainWindowTitle = GetMainWindowTitle(process);

            try
            {
                var path = process.MainModule?.FileName ?? string.Empty;
                var name = Path.GetFileName(path);

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = fallbackName;
                }

                var metadata = GetMetadata(path);
                return new ProcessSnapshot
                {
                    ProcessId = process.Id,
                    StartTimeUtcTicks = startTimeUtcTicks,
                    ProcessName = name,
                    ProcessPath = path,
                    WorkingSetBytes = workingSetBytes,
                    FileDescription = metadata.FileDescription,
                    ProductName = metadata.ProductName,
                    MainWindowTitle = mainWindowTitle
                };
            }
            catch
            {
                return new ProcessSnapshot
                {
                    ProcessId = process.Id,
                    StartTimeUtcTicks = startTimeUtcTicks,
                    ProcessName = fallbackName,
                    ProcessPath = string.Empty,
                    WorkingSetBytes = workingSetBytes,
                    MainWindowTitle = mainWindowTitle
                };
            }
        }
    }

    private static long GetStartTimeUtcTicks(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetFallbackProcessName(Process process)
    {
        try
        {
            var processName = process.ProcessName;
            return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName
                : $"{processName}.exe";
        }
        catch
        {
            return "unknown.exe";
        }
    }

    private static long GetWorkingSetBytes(Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch
        {
            return string.Empty;
        }
    }

    private ProcessMetadata GetMetadata(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ProcessMetadata.Empty;
        }

        return _metadataCache.GetOrAdd(path, ReadMetadata);
    }

    private static ProcessMetadata ReadMetadata(string path)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            return new ProcessMetadata(
                versionInfo.FileDescription ?? string.Empty,
                versionInfo.ProductName ?? string.Empty);
        }
        catch
        {
            return ProcessMetadata.Empty;
        }
    }

    private sealed record ProcessMetadata(string FileDescription, string ProductName)
    {
        public static ProcessMetadata Empty { get; } = new(string.Empty, string.Empty);
    }
}
