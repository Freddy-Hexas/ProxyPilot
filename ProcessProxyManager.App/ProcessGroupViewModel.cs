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
    private string _displayName;
    private string _fileDescription;
    private bool _isExpanded;
    private string _mainWindowTitle;
    private int _mihomoConnectionCount;
    private string _processName;
    private string _processPath;
    private ProxyAction _selectedAction;
    private string _lastMatchedRule = "-";
    private string _lastRouteChain = "-";
    private int _suspectedDirectConnectionCount;
    private int _upstreamConnectionCount;
    private long _workingSetBytes;

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
        _displayName = displayName;
        _processName = processName;
        _processPath = processPath;
        _fileDescription = fileDescription;
        _mainWindowTitle = mainWindowTitle;
        _workingSetBytes = workingSetBytes;
        _selectedAction = selectedAction;
        Icon = icon;
        Processes = new ObservableCollection<ProcessRowViewModel>(processes);

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

    public string DisplayName => _displayName;

    public string ProcessName => _processName;

    public string ProcessPath => _processPath;

    public string FileDescription => _fileDescription;

    public string MainWindowTitle => _mainWindowTitle;

    public long WorkingSetBytes => _workingSetBytes;

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

    public void ReplaceProcesses(IReadOnlyList<ProcessRowViewModel> processes)
    {
        var wasExpanded = IsExpanded;
        var currentIndex = 0;
        while (currentIndex < processes.Count)
        {
            var desired = processes[currentIndex];
            if (currentIndex < Processes.Count && ReferenceEquals(Processes[currentIndex], desired))
            {
                currentIndex++;
                continue;
            }

            var existingIndex = Processes.IndexOf(desired);
            if (existingIndex >= 0)
            {
                Processes.Move(existingIndex, currentIndex);
            }
            else
            {
                Processes.Insert(currentIndex, desired);
            }

            currentIndex++;
        }

        while (Processes.Count > processes.Count)
        {
            Processes.RemoveAt(Processes.Count - 1);
        }

        IsExpanded = wasExpanded && CanExpand;
        OnPropertyChanged(nameof(ProcessCount));
        OnPropertyChanged(nameof(CountBadge));
        OnPropertyChanged(nameof(CanExpand));
        OnPropertyChanged(nameof(PrimaryProcessId));
        OnPropertyChanged(nameof(ProcessIdSummary));
        RefreshFromChildren();
    }

    public void UpdatePresentation(
        string displayName,
        string processName,
        string processPath,
        string fileDescription,
        string mainWindowTitle,
        long workingSetBytes)
    {
        _displayName = displayName;
        _processName = processName;
        _processPath = processPath;
        _fileDescription = fileDescription;
        _mainWindowTitle = mainWindowTitle;
        _workingSetBytes = workingSetBytes;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ProcessName));
        OnPropertyChanged(nameof(ProcessPath));
        OnPropertyChanged(nameof(FileDescription));
        OnPropertyChanged(nameof(MainWindowTitle));
        OnPropertyChanged(nameof(WorkingSetBytes));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(DisplayPath));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
