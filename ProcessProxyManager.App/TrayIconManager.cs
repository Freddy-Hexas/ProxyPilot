using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.IO;
using DrawingIcon = System.Drawing.Icon;
using SystemIcons = System.Drawing.SystemIcons;

namespace ProcessProxyManager.App;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainWindow _window;
    private readonly DrawingIcon? _ownedIcon;
    private bool _disposed;

    public TrayIconManager(MainWindow window, MainWindowViewModel viewModel)
    {
        _window = window;
        var icon = LoadTrayIcon();
        _ownedIcon = ReferenceEquals(icon, SystemIcons.Application) ? null : icon;
        _notifyIcon = new NotifyIcon
        {
            Text = "ProxyPilot",
            Icon = icon,
            Visible = true,
            ContextMenuStrip = BuildMenu(viewModel),
            Tag = viewModel
        };

        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "Item[]" or nameof(MainWindowViewModel.LanguageButtonText))
            {
                RebuildMenu(viewModel);
            }
        };
    }

    public void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _ownedIcon?.Dispose();
        _disposed = true;
    }

    private static DrawingIcon LoadTrayIcon()
    {
        foreach (var iconPath in GetIconCandidates())
        {
            if (!File.Exists(iconPath))
            {
                continue;
            }

            try
            {
                return new DrawingIcon(iconPath);
            }
            catch
            {
                // Try the next location before falling back to the system icon.
            }
        }

        try
        {
            var resourceInfo = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/ProxyPilot.ico", UriKind.Absolute));
            if (resourceInfo?.Stream is not null)
            {
                using var stream = resourceInfo.Stream;
                using var icon = new DrawingIcon(stream);
                return (DrawingIcon)icon.Clone();
            }
        }
        catch
        {
            // Missing or invalid icon assets should not prevent tray mode.
        }

        return SystemIcons.Application;
    }

    private static IEnumerable<string> GetIconCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "resources", "ProxyPilot.ico");
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "ProxyPilot.ico");
    }

    private ContextMenuStrip BuildMenu(MainWindowViewModel viewModel)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(viewModel.T("TrayOpen"), null, (_, _) => ShowWindow());
        menu.Items.Add(viewModel.T("TrayApplyRules"), null, (_, _) => Execute(viewModel.ApplyRulesCommand));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(viewModel.T("TrayStartMihomo"), null, (_, _) => Execute(viewModel.StartMihomoCommand));
        menu.Items.Add(viewModel.T("TrayStopMihomo"), null, (_, _) => Execute(viewModel.StopMihomoCommand));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(viewModel.LanguageButtonText, null, (_, _) => Execute(viewModel.ToggleLanguageCommand));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(viewModel.T("TrayExit"), null, async (_, _) => await _window.ExitApplicationAsync());
        return menu;
    }

    private void RebuildMenu(MainWindowViewModel viewModel)
    {
        var previousMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = BuildMenu(viewModel);
        previousMenu?.Dispose();
    }

    private static void Execute(ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
