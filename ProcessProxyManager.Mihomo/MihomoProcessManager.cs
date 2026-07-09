using System.Diagnostics;
using ProcessProxyManager.Core;

namespace ProcessProxyManager.Mihomo;

public sealed class MihomoProcessManager
{
    private Process? _process;
    private bool _expectedStop;

    public event EventHandler<MihomoProcessExitedEventArgs>? Exited;

    public bool IsRunning => _process is { HasExited: false };

    public int? ProcessId => IsRunning ? _process?.Id : null;

    public void Start(AppSettings settings)
    {
        if (IsRunning)
        {
            return;
        }

        var startInfo = CreateStartInfo(settings);

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        _expectedStop = false;

        _process.Exited += (_, _) =>
        {
            var exitCode = TryGetExitCode(_process);
            Exited?.Invoke(this, new MihomoProcessExitedEventArgs(exitCode, _expectedStop));
        };

        _process.Start();
    }

    public static ProcessStartInfo CreateStartInfo(AppSettings settings)
    {
        var mihomoPath = Path.GetFullPath(settings.MihomoPath);
        if (!File.Exists(mihomoPath))
        {
            throw new FileNotFoundException("mihomo.exe not found.", mihomoPath);
        }

        var configPath = Path.GetFullPath(settings.GeneratedConfigPath);
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("generated config not found.", configPath);
        }

        var configDirectory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
        var startInfo = new ProcessStartInfo
        {
            FileName = mihomoPath,
            Arguments = $"-d {Quote(configDirectory)} -f {Quote(Path.GetFileName(configPath))}",
            WorkingDirectory = configDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        AddSafePath(startInfo, configDirectory);
        return startInfo;
    }

    public void Stop()
    {
        if (!IsRunning || _process is null)
        {
            return;
        }

        try
        {
            _expectedStop = true;
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public void Restart(AppSettings settings)
    {
        Stop();
        Start(settings);
    }

    private static int? TryGetExitCode(Process? process)
    {
        try
        {
            return process?.ExitCode;
        }
        catch
        {
            return null;
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void AddSafePath(ProcessStartInfo startInfo, string path)
    {
        var safePath = Path.GetFullPath(path);
        startInfo.Environment.TryGetValue("SAFE_PATHS", out var existing);

        var parts = (existing ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(safePath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        startInfo.Environment["SAFE_PATHS"] = string.Join(Path.PathSeparator, parts);
    }
}
