using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using ProcessProxyManager.Core;

namespace ProcessProxyManager.App;

public sealed class ProcessGroupViewModel : INotifyPropertyChanged
{
    private int _currentConnectionCount;
    private bool _isExpanded;
    private int _mihomoConnectionCount;
    private ProxyAction _selectedAction;
    private string _lastMatchedRule = "-";
    private string _lastRouteChain = "-";
    private int _suspectedDirectConnectionCount;
    private int _upstreamConnectionCount;

    public ProcessGroupViewModel(
        string groupKey,
        string displayName,
        string processName,
        string processPath,
        string fileDescription,
        string mainWindowTitle,
        long workingSetBytes,
        ProxyAction selectedAction,
        ImageSource? icon,
        IReadOnlyList<ProcessRowViewModel> processes)
    {
        GroupKey = groupKey;
        DisplayName = displayName;
        ProcessName = processName;
        ProcessPath = processPath;
        FileDescription = fileDescription;
        MainWindowTitle = mainWindowTitle;
        WorkingSetBytes = workingSetBytes;
        _selectedAction = selectedAction;
        Icon = icon;
        Processes = new ObservableCollection<ProcessRowViewModel>(processes);

        SetProxyCommand = new RelayCommand(_ => SelectedAction = ProxyAction.PROXY);
        SetDirectCommand = new RelayCommand(_ => SelectedAction = ProxyAction.DIRECT);
        SetRejectCommand = new RelayCommand(_ => SelectedAction = ProxyAction.REJECT);
        ClearRuleCommand = new RelayCommand(_ => SelectedAction = ProxyAction.None);
        ToggleExpandedCommand = new RelayCommand(_ =>
        {
            if (CanExpand)
            {
                IsExpanded = !IsExpanded;
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string GroupKey { get; }

    public string DisplayName { get; }

    public string ProcessName { get; }

    public string ProcessPath { get; }

    public string FileDescription { get; }

    public string MainWindowTitle { get; }

    public long WorkingSetBytes { get; }

    public ImageSource? Icon { get; }

    public ObservableCollection<ProcessRowViewModel> Processes { get; }

    public int ProcessCount => Processes.Count;

    public string CountBadge => ProcessCount > 1 ? $"({ProcessCount})" : string.Empty;

    public bool CanExpand => ProcessCount > 1;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }

    public string ExpandGlyph => IsExpanded ? "v" : ">";

    public string PrimaryProcessId => Processes.Count == 0 ? "-" : Processes[0].ProcessId.ToString();

    public string ProcessIdSummary => ProcessCount == 1
        ? PrimaryProcessId
        : string.Join(", ", Processes.Select(static process => process.ProcessId));

    public string Subtitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(MainWindowTitle))
            {
                return MainWindowTitle;
            }

            if (!string.IsNullOrWhiteSpace(FileDescription) &&
                !string.Equals(FileDescription, DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return FileDescription;
            }

            return ProcessName;
        }
    }

    public string DisplayPath => string.IsNullOrWhiteSpace(ProcessPath) ? "-" : ProcessPath;

    public string RuleDescription => SelectedAction switch
    {
        ProxyAction.PROXY => "PROXY -> ProxyPilot Upstream",
        ProxyAction.DIRECT => "DIRECT",
        ProxyAction.REJECT => "REJECT",
        _ => "No explicit rule"
    };

    public ICommand SetProxyCommand { get; }

    public ICommand SetDirectCommand { get; }

    public ICommand SetRejectCommand { get; }

    public ICommand ClearRuleCommand { get; }

    public ICommand ToggleExpandedCommand { get; }

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

            foreach (var process in Processes)
            {
                process.SelectedAction = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(RuleDescription));
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

    public void RefreshFromChildren()
    {
        CurrentConnectionCount = Processes.Sum(static process => process.CurrentConnectionCount);
        MihomoConnectionCount = Processes.Sum(static process => process.MihomoConnectionCount);
        SuspectedDirectConnectionCount = Processes.Sum(static process => process.SuspectedDirectConnectionCount);
        UpstreamConnectionCount = Processes.Sum(static process => process.UpstreamConnectionCount);
        LastMatchedRule = Processes.Select(static process => process.LastMatchedRule).FirstOrDefault(static value => value != "-") ?? "-";
        LastRouteChain = Processes.Select(static process => process.LastRouteChain).FirstOrDefault(static value => value != "-") ?? "-";

        if (Processes.Count > 0 && Processes.All(process => process.SelectedAction == Processes[0].SelectedAction))
        {
            _selectedAction = Processes[0].SelectedAction;
            OnPropertyChanged(nameof(SelectedAction));
            OnPropertyChanged(nameof(RuleDescription));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
