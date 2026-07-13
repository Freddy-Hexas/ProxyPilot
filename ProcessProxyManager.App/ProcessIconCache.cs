using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProcessProxyManager.App;

public sealed class ProcessIconCache
{
    private readonly ConcurrentDictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ImageSource? _fallbackIcon;

    public ProcessIconCache()
    {
        _fallbackIcon = LoadPackImage("pack://application:,,,/Assets/ProxyPilot-48.png");
    }

    public ImageSource? GetIcon(string processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return _fallbackIcon;
        }

        return _icons.GetOrAdd(processPath, ExtractIcon) ?? _fallbackIcon;
    }

    private static ImageSource? ExtractIcon(string processPath)
    {
        if (!File.Exists(processPath))
        {
            return null;
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(processPath);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? LoadPackImage(string uri)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(uri, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 32;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
