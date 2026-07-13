namespace ProcessProxyManager.Mihomo;

public sealed record MihomoProcessExitedEventArgs(int? ExitCode, bool Expected);
