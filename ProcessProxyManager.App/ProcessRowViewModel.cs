using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ProcessProxyManager.Core;

namespace ProcessProxyManager.App;

public sealed class ProcessRowViewModel : INotifyPropertyChanged
{
    private int _currentConnectionCount;
    private int _mihomoConnectionCount;
    private ProxyAction _selectedAction;
    private string _lastMatchedRule = "-";
    private string _lastRouteChain = "-";
    private int _suspectedDirectConnectionCount;
    private int _upstreamConnectionCount;

    public ProcessRowViewModel(ProcessSnapshot process, ProxyAction selectedAction)
    {
        ProcessId = process.ProcessId;
        ProcessName = process.ProcessName;
        ProcessPath = process.ProcessPath;
        WorkingSetBytes = process.WorkingSetBytes;
        FileDescription = process.FileDescription;
        ProductName = process.ProductName;
        MainWindowTitle = process.MainWindowTitle;
        _selectedAction = selectedAction;

        SetProxyCommand = new RelayCommand(_ => SelectedAction = ProxyAction.PROXY);
        SetDirectCommand = new RelayCommand(_ => SelectedAction = ProxyAction.DIRECT);
        SetRejectCommand = new RelayCommand(_ => SelectedAction = ProxyAction.REJECT);
        ClearRuleCommand = new RelayCommand(_ => SelectedAction = ProxyAction.None);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int ProcessId { get; }

    public string ProcessName { get; }

    public string ProcessPath { get; }

    public string DisplayPath => string.IsNullOrWhiteSpace(ProcessPath) ? "-" : ProcessPath;

    public long WorkingSetBytes { get; }

    public string FileDescription { get; }

    public string ProductName { get; }

    public string MainWindowTitle { get; }

    public ICommand SetProxyCommand { get; }

    public ICommand SetDirectCommand { get; }

    public ICommand SetRejectCommand { get; }

    public ICommand ClearRuleCommand { get; }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
