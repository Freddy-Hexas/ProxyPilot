using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProcessProxyManager.Core;

namespace ProcessProxyManager.App;

public sealed class ProcessRowViewModel : INotifyPropertyChanged
{
    private int _currentConnectionCount;
    private string _fileDescription;
    private string _mainWindowTitle;
    private int _mihomoConnectionCount;
    private string _processName;
    private string _processPath;
    private string _productName;
    private ProxyAction _selectedAction;
    private string _lastMatchedRule = "-";
    private string _lastRouteChain = "-";
    private int _suspectedDirectConnectionCount;
    private int _upstreamConnectionCount;
    private long _workingSetBytes;

    public ProcessRowViewModel(ProcessSnapshot process, ProxyAction selectedAction)
    {
        ProcessId = process.ProcessId;
        StartTimeUtcTicks = process.StartTimeUtcTicks;
        _processName = process.ProcessName;
        _processPath = process.ProcessPath;
        _workingSetBytes = process.WorkingSetBytes;
        _fileDescription = process.FileDescription;
        _productName = process.ProductName;
        _mainWindowTitle = process.MainWindowTitle;
        _selectedAction = selectedAction;

    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int ProcessId { get; }

    public long StartTimeUtcTicks { get; }

    public string ProcessName => _processName;

    public string ProcessPath => _processPath;

    public string DisplayPath => string.IsNullOrWhiteSpace(_processPath) ? "-" : _processPath;

    public long WorkingSetBytes => _workingSetBytes;

    public string FileDescription => _fileDescription;

    public string ProductName => _productName;

    public string MainWindowTitle => _mainWindowTitle;

    public ProxyAction SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (_selectedAction == value)
            {
                return;
            }

            _selectedAction = value;
            OnPropertyChanged();
        }
    }

    public int CurrentConnectionCount
    {
        get => _currentConnectionCount;
        private set
        {
            if (_currentConnectionCount == value)
            {
                return;
            }

            _currentConnectionCount = value;
            OnPropertyChanged();
        }
    }

    public int MihomoConnectionCount
    {
        get => _mihomoConnectionCount;
        private set
        {
            if (_mihomoConnectionCount == value)
            {
                return;
            }

            _mihomoConnectionCount = value;
            OnPropertyChanged();
        }
    }

    public int SuspectedDirectConnectionCount
    {
        get => _suspectedDirectConnectionCount;
        private set
        {
            if (_suspectedDirectConnectionCount == value)
            {
                return;
            }

            _suspectedDirectConnectionCount = value;
            OnPropertyChanged();
        }
    }

    public int UpstreamConnectionCount
    {
        get => _upstreamConnectionCount;
        private set
        {
            if (_upstreamConnectionCount == value)
            {
                return;
            }

            _upstreamConnectionCount = value;
            OnPropertyChanged();
        }
    }

    public string LastMatchedRule
    {
        get => _lastMatchedRule;
        private set
        {
            if (_lastMatchedRule == value)
            {
                return;
            }

            _lastMatchedRule = value;
            OnPropertyChanged();
        }
    }

    public string LastRouteChain
    {
        get => _lastRouteChain;
        private set
        {
            if (_lastRouteChain == value)
            {
                return;
            }

            _lastRouteChain = value;
            OnPropertyChanged();
        }
    }

    public void UpdateConnectionCounts(int total, int mihomo, int suspectedDirect)
    {
        CurrentConnectionCount = total;
        MihomoConnectionCount = mihomo;
        SuspectedDirectConnectionCount = suspectedDirect;
    }

    public void UpdateUpstreamConnectionCount(int upstreamConnections)
    {
        UpstreamConnectionCount = upstreamConnections;
    }

    public void UpdateRouteSummary(string matchedRule, string routeChain)
    {
        LastMatchedRule = string.IsNullOrWhiteSpace(matchedRule) ? "-" : matchedRule;
        LastRouteChain = string.IsNullOrWhiteSpace(routeChain) ? "-" : routeChain;
    }

    public void UpdateSnapshot(ProcessSnapshot process)
    {
        if (!string.Equals(_processName, process.ProcessName, StringComparison.Ordinal))
        {
            _processName = process.ProcessName;
            OnPropertyChanged(nameof(ProcessName));
        }

        if (!string.Equals(_processPath, process.ProcessPath, StringComparison.Ordinal))
        {
            _processPath = process.ProcessPath;
            OnPropertyChanged(nameof(ProcessPath));
            OnPropertyChanged(nameof(DisplayPath));
        }

        if (_workingSetBytes != process.WorkingSetBytes)
        {
            _workingSetBytes = process.WorkingSetBytes;
            OnPropertyChanged(nameof(WorkingSetBytes));
        }

        if (!string.Equals(_fileDescription, process.FileDescription, StringComparison.Ordinal))
        {
            _fileDescription = process.FileDescription;
            OnPropertyChanged(nameof(FileDescription));
        }

        if (!string.Equals(_productName, process.ProductName, StringComparison.Ordinal))
        {
            _productName = process.ProductName;
            OnPropertyChanged(nameof(ProductName));
        }

        if (!string.Equals(_mainWindowTitle, process.MainWindowTitle, StringComparison.Ordinal))
        {
            _mainWindowTitle = process.MainWindowTitle;
            OnPropertyChanged(nameof(MainWindowTitle));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
