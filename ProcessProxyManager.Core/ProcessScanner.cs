using System.Diagnostics;

namespace ProcessProxyManager.Core;

public sealed class ProcessScanner
{
    public IReadOnlyList<ProcessSnapshot> GetRunningProcesses()
    {
        return Process.GetProcesses()
            .Select(TryCreateSnapshot)
            .Where(static process => process is not null)
            .Cast<ProcessSnapshot>()
            .OrderByDescending(static process => process.WorkingSetBytes)
            .ThenBy(static process => process.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static process => process.ProcessId)
            .ToList();
    }

    private static ProcessSnapshot? TryCreateSnapshot(Process process)
    {
        using (process)
        {
            var fallbackName = GetFallbackProcessName(process);
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

                return new ProcessSnapshot
                {
                    ProcessId = process.Id,
                    ProcessName = name,
                    ProcessPath = path,
                    WorkingSetBytes = workingSetBytes,
                    FileDescription = GetFileDescription(path),
                    ProductName = GetProductName(path),
                    MainWindowTitle = mainWindowTitle
                };
            }
            catch
            {
                return new ProcessSnapshot
                {
                    ProcessId = process.Id,
                    ProcessName = fallbackName,
                    ProcessPath = string.Empty,
                    WorkingSetBytes = workingSetBytes,
                    MainWindowTitle = mainWindowTitle
                };
            }
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

    private static string GetFileDescription(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileDescription ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetProductName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(path).ProductName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
