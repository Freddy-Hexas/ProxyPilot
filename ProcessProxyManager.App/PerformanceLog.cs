using System.Diagnostics;
using System.IO;

namespace ProcessProxyManager.App;

internal static class PerformanceLog
{
    private static readonly object Sync = new();

    public static IDisposable Measure(string operation)
    {
        return new Measurement(operation);
    }

    public static void Write(string operation, TimeSpan elapsed, string? details = null)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $" {details}";
            var line = $"[{DateTimeOffset.Now:O}] {operation} {elapsed.TotalMilliseconds:F0}ms{suffix}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(Path.Combine(AppPaths.Logs, "performance.log"), line);
            }
        }
        catch
        {
            // Performance logging must never affect application behavior.
        }
    }

    private sealed class Measurement : IDisposable
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly string _operation;

        public Measurement(string operation)
        {
            _operation = operation;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            Write(_operation, _stopwatch.Elapsed);
        }
    }
}
