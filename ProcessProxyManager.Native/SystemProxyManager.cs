using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ProcessProxyManager.Native;

public sealed class SystemProxyManager
{
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public SystemProxySnapshot GetSnapshot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath);
        return new SystemProxySnapshot(
            Convert.ToInt32(key?.GetValue("ProxyEnable") ?? 0) == 1,
            key?.GetValue("ProxyServer")?.ToString() ?? string.Empty,
            key?.GetValue("ProxyOverride")?.ToString() ?? string.Empty,
            key?.GetValue("AutoConfigURL")?.ToString() ?? string.Empty);
    }

    public void ApplyProxy(string proxyServer)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(InternetSettingsPath, writable: true);

        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
        key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
        key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
        NotifyChanged();
    }

    public void Restore(SystemProxySnapshot snapshot)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(InternetSettingsPath, writable: true);

        key.SetValue("ProxyEnable", snapshot.Enabled ? 1 : 0, RegistryValueKind.DWord);
        SetOrDelete(key, "ProxyServer", snapshot.ProxyServer);
        SetOrDelete(key, "ProxyOverride", snapshot.ProxyOverride);
        SetOrDelete(key, "AutoConfigURL", snapshot.AutoConfigUrl);
        NotifyChanged();
    }

    private static void SetOrDelete(RegistryKey key, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            return;
        }

        key.SetValue(name, value, RegistryValueKind.String);
    }

    private static void NotifyChanged()
    {
        InternetSetOption(nint.Zero, InternetOptionSettingsChanged, nint.Zero, 0);
        InternetSetOption(nint.Zero, InternetOptionRefresh, nint.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(nint internet, int option, nint buffer, int bufferLength);
}
