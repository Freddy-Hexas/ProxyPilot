using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrawingIcon = System.Drawing.Icon;

namespace ProcessProxyManager.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private readonly DispatcherTimer _foregroundRefreshTimer;
    private bool _allowClose;
    private TrayIconManager? _trayIconManager;

    public MainWindow()
    {
        InitializeComponent();
        _foregroundRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _foregroundRefreshTimer.Tick += ForegroundRefreshTimerTick;
        LoadWindowIcon();
        DataContext = _viewModel;
        Loaded += MainWindowLoaded;
        Closing += MainWindowClosing;
        Activated += MainWindowActivated;
        IsVisibleChanged += MainWindowIsVisibleChanged;
    }

    private async void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindowLoaded;
        _trayIconManager = new TrayIconManager(this, _viewModel);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        await _viewModel.InitializeAsync();
        UpdateForegroundRefreshTimer();
    }

    public async Task ExitApplicationAsync()
    {
        _allowClose = true;
        await _viewModel.ShutdownAsync();
        _trayIconManager?.Dispose();
        _foregroundRefreshTimer.Stop();
        System.Windows.Application.Current.Shutdown();
    }

    private void MainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private async void MainWindowActivated(object? sender, EventArgs e)
    {
        UpdateForegroundRefreshTimer();
        await _viewModel.RefreshProcessesForForegroundAsync(force: true);
    }

    private void MainWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateForegroundRefreshTimer();
    }

    private async void ForegroundRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!IsForegroundRefreshAllowed())
        {
            UpdateForegroundRefreshTimer();
            return;
        }

        await _viewModel.RefreshProcessesForForegroundAsync();
    }

    private void UpdateForegroundRefreshTimer()
    {
        if (IsForegroundRefreshAllowed())
        {
            _foregroundRefreshTimer.Start();
        }
        else
        {
            _foregroundRefreshTimer.Stop();
        }
    }

    private bool IsForegroundRefreshAllowed()
    {
        return IsVisible && WindowState != WindowState.Minimized && IsActive;
    }

    private void LoadWindowIcon()
    {
        try
        {
            foreach (var iconPath in GetIconCandidates())
            {
                if (!File.Exists(iconPath))
                {
                    continue;
                }

                using var icon = new DrawingIcon(iconPath);
                Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                break;
            }

            var imageUri = new Uri("pack://application:,,,/Assets/ProxyPilot-48.png", UriKind.Absolute);
            AppIcon.Source = new BitmapImage(imageUri);
        }
        catch
        {
            // Missing or invalid icon assets should not prevent the app from opening.
        }
    }

    private static IEnumerable<string> GetIconCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "resources", "ProxyPilot.ico");
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "ProxyPilot.ico");
    }
}
