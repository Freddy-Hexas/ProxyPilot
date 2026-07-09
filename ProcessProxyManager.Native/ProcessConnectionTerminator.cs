using System.Diagnostics;

namespace ProcessProxyManager.Native;

public sealed class ProcessConnectionTerminator
{
    public int TerminateByProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return 0;
        }

        var normalizedName = Path.GetFileNameWithoutExtension(processName);
        var killed = 0;

        foreach (var process in Process.GetProcessesByName(normalizedName))
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    killed++;
                }
                catch
                {
                    // Some processes may have already exited or require higher privileges.
                }
            }
        }

        return killed;
    }
}
