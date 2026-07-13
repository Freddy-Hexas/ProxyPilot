using System.IO;

namespace ProcessProxyManager.App;

internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProxyPilot");

    public static string Data { get; } = Path.Combine(Root, "data");

    public static string Config { get; } = Path.Combine(Root, "config");

    public static string Logs { get; } = Path.Combine(Root, "logs");

    public static void MigrateLegacyState(string appRoot)
    {
        CopyIfMissing(
            Path.Combine(appRoot, "data", "settings.json"),
            Path.Combine(Data, "settings.json"));
        CopyIfMissing(
            Path.Combine(appRoot, "data", "user-rules.json"),
            Path.Combine(Data, "user-rules.json"));
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(source, destination);
    }
}
