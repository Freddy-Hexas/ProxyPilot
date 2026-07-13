using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ProcessProxyManager.App;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\ProxyPilot.SingleInstance";
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterExceptionHandlers();

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
            // The process is already exiting; a mutex cleanup failure is not actionable.
        }

        base.OnExit(e);
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogException("DispatcherUnhandledException", args.Exception);
            System.Windows.MessageBox.Show(
                $"ProxyPilot failed to start or hit an unexpected error. Details were written to {AppPaths.Logs}\\crash.log.\n\n" +
                args.Exception.Message,
                "ProxyPilot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogException("UnhandledException", exception);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private static void LogException(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            var logPath = Path.Combine(AppPaths.Logs, "crash.log");
            var text = $"""
                [{DateTimeOffset.Now:O}] {source}
                {exception}

                """;
            File.AppendAllText(logPath, text);
        }
        catch
        {
            // Avoid recursive crashes while reporting the original exception.
        }
    }

    private static void ActivateExistingInstance()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            var currentPath = currentProcess.MainModule?.FileName;
            foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
            {
                using (process)
                {
                    if (process.Id == currentProcess.Id || !IsSameExecutable(process, currentPath))
                    {
                        continue;
                    }

                    var windowHandle = FindWindowForProcess(process.Id);
                    if (windowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    ShowWindow(windowHandle, ShowNormal);
                    SetForegroundWindow(windowHandle);
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            LogException("ActivateExistingInstance", exception);
        }
    }

    private static bool IsSameExecutable(Process process, string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return true;
        }

        try
        {
            return string.Equals(process.MainModule?.FileName, currentPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr FindWindowForProcess(int processId)
    {
        var result = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var windowProcessId);
            if (windowProcessId == processId)
            {
                result = handle;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    private const int ShowNormal = 1;

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);
}
