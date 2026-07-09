using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ProcessProxyManager.Core;
using ProcessProxyManager.Mihomo;
using ProcessProxyManager.Native;

namespace ProcessProxyManager.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const string ChineseLanguage = "zh-CN";
    private const string EnglishLanguage = "en-US";

    private readonly AdministratorDetector _administratorDetector;
    private readonly MihomoApiClient _apiClient;
    private readonly MihomoConfigBuilder _configBuilder;
    private readonly ConnectionInspector _connectionInspector;
    private readonly LocalProxyDetector _localProxyDetector;
    private readonly MihomoProcessManager _mihomoProcessManager;
    private readonly MihomoRuleGenerator _ruleGenerator;
    private readonly ProcessScanner _processScanner;
    private readonly ProcessIconCache _processIconCache;
    private readonly RuleStore _ruleStore;
    private readonly JsonFileStore<AppSettings> _settingsStore;
    private readonly SystemProxyManager _systemProxyManager;
    private readonly UpstreamBypassDetector _upstreamBypassDetector;
    private readonly UpstreamHealthChecker _upstreamHealthChecker;
    private readonly UpstreamLoopbackDetector _upstreamLoopbackDetector;
    private readonly UpstreamTrafficDetector _upstreamTrafficDetector;
    private readonly ProcessConnectionTerminator _processConnectionTerminator;
    private readonly Dictionary<string, ProcessRule> _knownRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _processRefreshGate = new(1, 1);
    private AppSettings _settings = new();
    private DateTimeOffset _lastProcessRefreshUtc = DateTimeOffset.MinValue;
    private bool _syncingDuplicateRules;
    private ProcessGroupViewModel? _selectedProcess;
    private string _apiStatusKey = "ApiNotChecked";
    private object?[] _apiStatusArguments = [];
    private string _mihomoRuntimeStatusKey = "MihomoStopped";
    private object?[] _mihomoRuntimeStatusArguments = [];
    private string _ruleStatusKey = "RulesNotApplied";
    private object?[] _ruleStatusArguments = [];
    private string _statusKey = "StatusInitializing";
    private object?[] _statusArguments = [];
    private UpstreamHealthResult? _lastUpstreamHealthResult;
    private UpstreamLoopbackResult _lastLoopbackResult = new(false, 0, []);

    public MainWindowViewModel()
    {
        var appRoot = AppContext.BaseDirectory;
        var dataRoot = Path.Combine(appRoot, "data");
        var configRoot = Path.Combine(appRoot, "config");

        SettingsFilePath = Path.Combine(dataRoot, "settings.json");
        RulesFilePath = Path.Combine(dataRoot, "user-rules.json");
        RuleSnippetPath = Path.Combine(configRoot, "mihomo-generated.yaml");

        _ruleGenerator = new MihomoRuleGenerator();
        _configBuilder = new MihomoConfigBuilder(_ruleGenerator);
        _processScanner = new ProcessScanner();
        _processIconCache = new ProcessIconCache();
        _ruleStore = new RuleStore(RulesFilePath);
        _settingsStore = new JsonFileStore<AppSettings>(SettingsFilePath);
        _administratorDetector = new AdministratorDetector();
        _apiClient = new MihomoApiClient(new HttpClient { Timeout = TimeSpan.FromSeconds(3) });
        _mihomoProcessManager = new MihomoProcessManager();
        _connectionInspector = new ConnectionInspector(new NativeConnectionScanner());
        _localProxyDetector = new LocalProxyDetector();
        _systemProxyManager = new SystemProxyManager();
        _upstreamBypassDetector = new UpstreamBypassDetector();
        _upstreamHealthChecker = new UpstreamHealthChecker();
        _upstreamLoopbackDetector = new UpstreamLoopbackDetector(new NativeConnectionScanner());
        _upstreamTrafficDetector = new UpstreamTrafficDetector(new NativeConnectionScanner());
        _processConnectionTerminator = new ProcessConnectionTerminator();

        _mihomoProcessManager.Exited += (_, eventArgs) =>
        {
            if (eventArgs.Expected)
            {
                return;
            }

            SetMihomoRuntimeStatus("MihomoCrashed", eventArgs.ExitCode?.ToString() ?? "unknown");
            SetStatus("StatusMihomoExitedUnexpectedly");
        };

        RefreshProcessesCommand = new AsyncRelayCommand(() => RefreshProcessesAsync(showStatus: true, force: true));
        SaveRulesCommand = new AsyncRelayCommand(SaveRulesAsync);
        ApplyRulesCommand = new AsyncRelayCommand(ApplyRulesAsync);
        CheckApiCommand = new AsyncRelayCommand(CheckApiAsync);
        StartMihomoCommand = new AsyncRelayCommand(StartMihomoAsync);
        StopMihomoCommand = new RelayCommand(_ => StopMihomo());
        RestartMihomoCommand = new AsyncRelayCommand(RestartMihomoAsync);
        InspectConnectionsCommand = new AsyncRelayCommand(InspectConnectionsAsync);
        ToggleLanguageCommand = new AsyncRelayCommand(ToggleLanguageAsync);
        DetectUpstreamProxyCommand = new AsyncRelayCommand(DetectUpstreamProxyAsync);
        CheckUpstreamHealthCommand = new AsyncRelayCommand(CheckUpstreamHealthAsync);
        LocateMihomoCommand = new AsyncRelayCommand(LocateMihomoAsync);
        RestartSelectedProcessCommand = new AsyncRelayCommand(RestartSelectedProcessAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProcessRowViewModel> Processes { get; } = [];

    public ObservableCollection<ProcessGroupViewModel> ProcessGroups { get; } = [];

    public ICommand RefreshProcessesCommand { get; }

    public ICommand SaveRulesCommand { get; }

    public ICommand ApplyRulesCommand { get; }

    public ICommand CheckApiCommand { get; }

    public ICommand StartMihomoCommand { get; }

    public ICommand StopMihomoCommand { get; }

    public ICommand RestartMihomoCommand { get; }

    public ICommand InspectConnectionsCommand { get; }

    public ICommand ToggleLanguageCommand { get; }

    public ICommand DetectUpstreamProxyCommand { get; }

    public ICommand CheckUpstreamHealthCommand { get; }

    public ICommand LocateMihomoCommand { get; }

    public ICommand RestartSelectedProcessCommand { get; }

    public string SettingsFilePath { get; }

    public string RulesFilePath { get; }

    public string RuleSnippetPath { get; }

    public string GeneratedConfigPath => _settings.GeneratedConfigPath;

    public string VersionLabel => Format("VersionLabel");

    public string TemplateConfigPath
    {
        get => _settings.TemplateConfigPath;
        set
        {
            if (_settings.TemplateConfigPath == value)
            {
                return;
            }

            _settings.TemplateConfigPath = value;
            OnPropertyChanged();
        }
    }

    public string MihomoPath
    {
        get => _settings.MihomoPath;
        set
        {
            if (_settings.MihomoPath == value)
            {
                return;
            }

            _settings.MihomoPath = value;
            OnPropertyChanged();
        }
    }

    public string ApiUrl
    {
        get => _settings.ApiUrl;
        set
        {
            if (_settings.ApiUrl == value)
            {
                return;
            }

            _settings.ApiUrl = value;
            OnPropertyChanged();
        }
    }

    public string Secret
    {
        get => _settings.Secret;
        set
        {
            if (_settings.Secret == value)
            {
                return;
            }

            _settings.Secret = value;
            OnPropertyChanged();
        }
    }

    public bool EnableTun
    {
        get => _settings.EnableTun;
        set
        {
            if (_settings.EnableTun == value)
            {
                return;
            }

            _settings.EnableTun = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TunStatus));
        }
    }

    public bool CloseMihomoOnExit
    {
        get => _settings.CloseMihomoOnExit;
        set
        {
            if (_settings.CloseMihomoOnExit == value)
            {
                return;
            }

            _settings.CloseMihomoOnExit = value;
            OnPropertyChanged();
        }
    }

    public bool AutoDetectUpstreamProxy
    {
        get => _settings.AutoDetectUpstreamProxy;
        set
        {
            if (_settings.AutoDetectUpstreamProxy == value)
            {
                return;
            }

            _settings.AutoDetectUpstreamProxy = value;
            OnPropertyChanged();
        }
    }

    public string UpstreamProxyType
    {
        get => _settings.UpstreamProxyType;
        set
        {
            if (_settings.UpstreamProxyType == value)
            {
                return;
            }

            _settings.UpstreamProxyType = value;
            NotifyUpstreamChanged();
        }
    }

    public string UpstreamProxyHost
    {
        get => _settings.UpstreamProxyHost;
        set
        {
            if (_settings.UpstreamProxyHost == value)
            {
                return;
            }

            _settings.UpstreamProxyHost = value;
            NotifyUpstreamChanged();
        }
    }

    public int UpstreamProxyPort
    {
        get => _settings.UpstreamProxyPort;
        set
        {
            if (_settings.UpstreamProxyPort == value)
            {
                return;
            }

            _settings.UpstreamProxyPort = value;
            NotifyUpstreamChanged();
        }
    }

    public string UpstreamProxyStatus => UpstreamProxyPort > 0
        ? Format("UpstreamDetected", UpstreamProxyType, UpstreamProxyHost, UpstreamProxyPort, _settings.UpstreamProxySource)
        : Format("UpstreamNotDetected");

    public string UpstreamHealthStatus
    {
        get
        {
            if (UpstreamProxyPort <= 0)
            {
                return Format("UpstreamHealthNotConfigured");
            }

            if (_lastUpstreamHealthResult is null)
            {
                return Format("UpstreamHealthNotChecked");
            }

            return _lastUpstreamHealthResult.Success
                ? Format("UpstreamHealthOk")
                : Format("UpstreamHealthFailed");
        }
    }

    public string UpstreamHealthDetails => _lastUpstreamHealthResult is null
        ? string.Empty
        : string.Join(Environment.NewLine, _lastUpstreamHealthResult.Details);

    public string RouteChainStatus => UpstreamProxyPort > 0
        ? Format("RouteChainProxy", UpstreamProxyHost, UpstreamProxyPort)
        : Format("RouteChainNoUpstream");

    public string TunLoopbackStatus
    {
        get
        {
            if (_settings.TunProcessDirectNames.Count == 0 && _settings.TunRouteExcludeAddresses.Count == 0)
            {
                return Format("TunLoopbackNotProtected");
            }

            var protection = Format(
                "TunLoopbackProtection",
                _settings.TunProcessDirectNames.Count,
                _settings.TunRouteExcludeAddresses.Count);

            return _lastLoopbackResult.HasRisk
                ? $"{Format("TunLoopbackRisk", _lastLoopbackResult.ConnectionCount)}  {protection}"
                : $"{Format("TunLoopbackNoRisk")}  {protection}";
        }
    }

    public string ChromeReloadHint => Format("ChromeReloadHint");

    public string TrafficTakeoverStatus => _settings.SystemProxyTakeoverEnabled
        ? Format("TrafficTakeoverEnabled")
        : Format("TrafficTakeoverDisabled");

    public bool IsAdministrator { get; private set; }

    public string AdministratorStatus => IsAdministrator ? Format("AdminYes") : Format("AdminNo");

    public string TunStatus => EnableTun
        ? IsAdministrator ? Format("TunEnabled") : Format("TunNeedsAdmin")
        : Format("TunDisabled");

    public string ProcessCountText => Format("ProcessCount", ProcessGroups.Count, Processes.Count);

    public string ApiStatus => Format(_apiStatusKey, _apiStatusArguments);

    public string MihomoRuntimeStatus => Format(_mihomoRuntimeStatusKey, _mihomoRuntimeStatusArguments);

    public string RuleStatus => Format(_ruleStatusKey, _ruleStatusArguments);

    public string StatusText => Format(_statusKey, _statusArguments);

    public bool IsChinese => _settings.Language != EnglishLanguage;

    public string LanguageButtonText => IsChinese ? "English" : "Chinese";

    public string LanguageLabel => IsChinese ? "\u4e2d\u6587" : "English";

    public ProcessGroupViewModel? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (ReferenceEquals(_selectedProcess, value))
            {
                return;
            }

            _selectedProcess = value;
            OnPropertyChanged();
        }
    }

    public async Task InitializeAsync()
    {
        IsAdministrator = _administratorDetector.IsAdministrator();

        _settings = await _settingsStore.LoadAsync();
        NormalizeSettings();
        ResetLocalizedState();
        await _configBuilder.EnsureDefaultTemplateAsync(_settings.TemplateConfigPath);

        if (IsProxyPilotEndpoint(_settings.UpstreamProxyHost, _settings.UpstreamProxyPort))
        {
            _settings.UpstreamProxyPort = 0;
            _settings.UpstreamProxySource = string.Empty;
        }

        UpdateTunRouteExcludes();

        if (_settings.AutoDetectUpstreamProxy && _settings.UpstreamProxyPort <= 0)
        {
            DetectAndApplyBestUpstreamProxy();
        }

        LocateBundledMihomoIfPresent();
        await _settingsStore.SaveAsync(_settings);
        NotifySettingsChanged();
        NotifyLocalizationChanged();

        LoadKnownRules(await _ruleStore.LoadAsync());
        await StartMihomoOnLaunchAsync();
        await RefreshProcessesAsync(showStatus: true, force: true);
    }

    public async Task ShutdownAsync()
    {
        await SaveSettingsAsync();

        RestoreSystemProxyIfNeeded();
        await SaveSettingsAsync();

        if (CloseMihomoOnExit)
        {
            StopMihomo();
        }
    }

    public string T(string key)
    {
        return Format(key);
    }

    public string this[string key] => T(key);

    private async Task ToggleLanguageAsync()
    {
        _settings.Language = IsChinese ? EnglishLanguage : ChineseLanguage;
        await SaveSettingsAsync();
        NotifyLocalizationChanged();
        SetStatus("StatusLanguageChanged", LanguageLabel);
    }

    private async Task DetectUpstreamProxyAsync()
    {
        if (DetectAndApplyBestUpstreamProxy())
        {
            await SaveSettingsAsync();
            SetStatus("StatusUpstreamDetected", FormatUpstreamEndpoint());
            await CheckUpstreamHealthAsync();
            return;
        }

        SetStatus("StatusUpstreamNotDetected");
    }

    private async Task CheckUpstreamHealthAsync()
    {
        var result = await CheckUpstreamHealthCoreAsync();
        SetStatus(result.Success ? "StatusUpstreamHealthOk" : "StatusUpstreamHealthFailed", "Google/YouTube");
    }

    private async Task<UpstreamHealthResult> CheckUpstreamHealthCoreAsync()
    {
        if (UpstreamProxyPort <= 0)
        {
            var notConfigured = new UpstreamHealthResult(false, "Upstream proxy is not configured.", []);
            _lastUpstreamHealthResult = notConfigured;
            OnPropertyChanged(nameof(UpstreamHealthStatus));
            OnPropertyChanged(nameof(UpstreamHealthDetails));
            return notConfigured;
        }

        _lastUpstreamHealthResult = null;
        OnPropertyChanged(nameof(UpstreamHealthStatus));
        OnPropertyChanged(nameof(UpstreamHealthDetails));

        var result = await _upstreamHealthChecker.CheckAsync(
            UpstreamProxyType,
            UpstreamProxyHost,
            UpstreamProxyPort);

        _lastUpstreamHealthResult = result;
        OnPropertyChanged(nameof(UpstreamHealthStatus));
        OnPropertyChanged(nameof(UpstreamHealthDetails));
        return result;
    }

    private async Task LocateMihomoAsync()
    {
        if (LocateBundledMihomoIfPresent())
        {
            await SaveSettingsAsync();
            SetStatus("StatusMihomoLocated", MihomoPath);
            return;
        }

        SetStatus("StatusMihomoNotFound");
    }

    private async Task RestartSelectedProcessAsync()
    {
        if (SelectedProcess is null)
        {
            SetStatus("StatusNoSelectedProcess");
            return;
        }

        var killed = await Task.Run(() => _processConnectionTerminator.TerminateByProcessName(SelectedProcess.ProcessName));
        SetStatus("StatusProcessRestartRequested", SelectedProcess.DisplayName, killed);
        await RefreshProcessesAsync(showStatus: false, force: true);
    }

    public Task RefreshProcessesForForegroundAsync(bool force = false)
    {
        return RefreshProcessesAsync(showStatus: false, force: force);
    }

    private async Task RefreshProcessesAsync(bool showStatus, bool force)
    {
        if (!force && DateTimeOffset.UtcNow - _lastProcessRefreshUtc < TimeSpan.FromSeconds(8))
        {
            return;
        }

        if (!await _processRefreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (showStatus)
            {
                SetStatus("StatusScanning");
            }

            var savedRules = await _ruleStore.LoadAsync();
            LoadKnownRules(savedRules);
            var selectedGroupKey = SelectedProcess?.GroupKey;

            foreach (var process in Processes)
            {
                UpdateKnownRule(process);
                process.PropertyChanged -= ProcessRowPropertyChanged;
            }

            var snapshots = await Task.Run(_processScanner.GetRunningProcesses);
            Processes.Clear();
            ProcessGroups.Clear();

            foreach (var snapshot in snapshots)
            {
                _knownRules.TryGetValue(snapshot.ProcessName, out var knownRule);
                var action = knownRule?.Action ?? ProxyAction.None;
                var row = new ProcessRowViewModel(snapshot, action);
                row.PropertyChanged += ProcessRowPropertyChanged;
                Processes.Add(row);
            }

            BuildProcessGroups();
            SelectedProcess = string.IsNullOrWhiteSpace(selectedGroupKey)
                ? ProcessGroups.FirstOrDefault()
                : ProcessGroups.FirstOrDefault(group =>
                    string.Equals(group.GroupKey, selectedGroupKey, StringComparison.OrdinalIgnoreCase)) ?? ProcessGroups.FirstOrDefault();
            OnPropertyChanged(nameof(ProcessCountText));
            _lastProcessRefreshUtc = DateTimeOffset.UtcNow;

            if (showStatus)
            {
                SetStatus("StatusScanComplete", ProcessGroups.Count, Processes.Count);
            }
        }
        catch (Exception exception)
        {
            if (showStatus)
            {
                SetStatus("StatusScanFailed", exception.Message);
            }
        }
        finally
        {
            _processRefreshGate.Release();
        }
    }

    private async Task SaveRulesAsync()
    {
        try
        {
            await SaveRulesCoreAsync();
            await SaveSettingsAsync();
            SetRuleStatus("RulesSaved");
            SetStatus("StatusRulesSaved");
        }
        catch (Exception exception)
        {
            SetStatus("StatusSaveFailed", exception.Message);
        }
    }

    private async Task ApplyRulesAsync()
    {
        try
        {
            var document = await SaveRulesCoreAsync();

            PrepareTrafficTakeover();

            await SaveSettingsAsync();
            await _ruleGenerator.WriteRulesYamlAsync(document.Rules, RuleSnippetPath, _settings.TunProcessDirectNames);
            await _configBuilder.WriteGeneratedConfigAsync(_settings, document.Rules);

            var result = await _apiClient.ReloadConfigAsync(ApiUrl, Secret, _settings);
            SetApiStatus(result.Success ? "ApiConnected" : "ApiReloadFailed", result.StatusCode);
            SetRuleStatus(result.Success ? "RulesApplied" : "RulesGeneratedReloadFailed");

            if (result.Success)
            {
                ApplySystemProxyTakeover();
            }

            var health = await CheckUpstreamHealthCoreAsync();
            InspectTunLoopbackRisk();

            if (result.Success && !health.Success)
            {
                SetStatus("StatusRulesAppliedUpstreamFailed", "Google/YouTube");
            }
            else
            {
                SetStatus(result.Success ? "StatusRulesAppliedReloaded" : "StatusGeneratedReloadFailed", result.Message);
            }
        }
        catch (Exception exception)
        {
            SetRuleStatus("RulesApplyFailed");
            SetStatus("StatusApplyFailed", exception.Message);
        }
    }

    private async Task CheckApiAsync()
    {
        var result = await _apiClient.CheckAsync(ApiUrl, Secret);
        SetApiStatus(result.Success ? "ApiConnected" : "ApiUnavailable", result.StatusCode);
        SetStatus(result.Success ? "StatusApiConnected" : "StatusApiUnavailable", result.Message);
    }

    private async Task StartMihomoAsync()
    {
        try
        {
            await StartMihomoCoreAsync("StatusMihomoStarted");
        }
        catch (Exception exception)
        {
            SetMihomoRuntimeStatus("MihomoStartFailed");
            SetStatus("StatusStartFailed", exception.Message);
        }
    }

    private void StopMihomo()
    {
        _mihomoProcessManager.Stop();
        SetMihomoRuntimeStatus("MihomoStopped");
        SetStatus("StatusMihomoStopped");
    }

    private async Task RestartMihomoAsync()
    {
        try
        {
            await EnsureGeneratedConfigAsync();
            _mihomoProcessManager.Restart(_settings);
            SetMihomoRuntimeStatus("MihomoRunning", _mihomoProcessManager.ProcessId?.ToString() ?? string.Empty);
            SetStatus("StatusMihomoRestarted");
        }
        catch (Exception exception)
        {
            SetMihomoRuntimeStatus("MihomoRestartFailed");
            SetStatus("StatusRestartFailed", exception.Message);
        }
    }

    private async Task StartMihomoOnLaunchAsync()
    {
        if (EnableTun && !IsAdministrator)
        {
            SetStatus("StatusAutoStartNeedsAdmin");
            return;
        }

        try
        {
            await StartMihomoCoreAsync("StatusMihomoAutoStarted");
        }
        catch (Exception exception)
        {
            SetMihomoRuntimeStatus("MihomoStartFailed");
            SetStatus("StatusAutoStartFailed", exception.Message);
        }
    }

    private async Task StartMihomoCoreAsync(string successStatusKey)
    {
        await EnsureGeneratedConfigAsync();
        _mihomoProcessManager.Start(_settings);
        SetMihomoRuntimeStatus("MihomoRunning", _mihomoProcessManager.ProcessId?.ToString() ?? string.Empty);
        await RefreshApiStatusAfterStartAsync();
        ApplySystemProxyTakeover();
        await CheckUpstreamHealthCoreAsync();
        InspectTunLoopbackRisk();
        SetStatus(successStatusKey);
    }

    private async Task RefreshApiStatusAfterStartAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var result = await _apiClient.CheckAsync(ApiUrl, Secret);
            if (result.Success)
            {
                SetApiStatus("ApiConnected");
                return;
            }

            await Task.Delay(300);
        }

        SetApiStatus("ApiNotChecked");
    }

    private async Task InspectConnectionsAsync()
    {
        try
        {
            SetStatus("StatusInspecting");
            var mihomoConnections = await _apiClient.GetConnectionsAsync(ApiUrl, Secret);
            var results = _connectionInspector.Inspect(mihomoConnections);
            var upstreamConnections = _upstreamTrafficDetector.CountByProcessId(UpstreamProxyPort);
            InspectTunLoopbackRisk();

            foreach (var process in Processes)
            {
                if (results.TryGetValue(process.ProcessId, out var result))
                {
                    process.UpdateConnectionCounts(
                        result.TotalConnections,
                        result.MihomoConnections,
                        result.SuspectedDirectConnections);
                }
                else
                {
                    process.UpdateConnectionCounts(0, 0, 0);
                }

                upstreamConnections.TryGetValue(process.ProcessId, out var upstreamConnectionCount);
                process.UpdateUpstreamConnectionCount(upstreamConnectionCount);
                process.UpdateRouteSummary(
                    BuildMatchedRuleSummary(process),
                    BuildRouteChainSummary(process.SelectedAction, upstreamConnectionCount));
            }

            foreach (var group in ProcessGroups)
            {
                group.RefreshFromChildren();
            }

            SetStatus("StatusInspectionComplete");
        }
        catch (Exception exception)
        {
            SetStatus("StatusInspectionFailed", exception.Message);
        }
    }

    private async Task EnsureGeneratedConfigAsync()
    {
        var document = await SaveRulesCoreAsync();

            PrepareTrafficTakeover();

            await SaveSettingsAsync();
            await _ruleGenerator.WriteRulesYamlAsync(document.Rules, RuleSnippetPath, _settings.TunProcessDirectNames);
            await _configBuilder.WriteGeneratedConfigAsync(_settings, document.Rules);
        }

    private async Task<UserRulesDocument> SaveRulesCoreAsync()
    {
        foreach (var process in Processes)
        {
            UpdateKnownRule(process);
        }

        var document = new UserRulesDocument
        {
            Rules = _knownRules.Values
                .OrderBy(static rule => rule.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        await _ruleStore.SaveAsync(document);
        return document;
    }

    private async Task SaveSettingsAsync()
    {
        await _settingsStore.SaveAsync(_settings);
    }

    private void LoadKnownRules(UserRulesDocument document)
    {
        _knownRules.Clear();

        foreach (var rule in document.Rules.Where(static rule => rule.Action != ProxyAction.None))
        {
            _knownRules[rule.ProcessName] = rule;
        }
    }

    private void UpdateKnownRule(ProcessRowViewModel process)
    {
        if (process.SelectedAction == ProxyAction.None)
        {
            _knownRules.Remove(process.ProcessName);
            return;
        }

        _knownRules[process.ProcessName] = new ProcessRule
        {
            ProcessName = process.ProcessName,
            ProcessPath = process.ProcessPath,
            Action = process.SelectedAction
        };
    }

    private void ProcessRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingDuplicateRules ||
            e.PropertyName != nameof(ProcessRowViewModel.SelectedAction) ||
            sender is not ProcessRowViewModel changed)
        {
            return;
        }

        try
        {
            _syncingDuplicateRules = true;

            foreach (var process in Processes.Where(process =>
                         !ReferenceEquals(process, changed) &&
                         string.Equals(process.ProcessName, changed.ProcessName, StringComparison.OrdinalIgnoreCase)))
            {
                process.SelectedAction = changed.SelectedAction;
            }

            foreach (var group in ProcessGroups)
            {
                group.RefreshFromChildren();
            }
        }
        finally
        {
            _syncingDuplicateRules = false;
        }
    }

    private void BuildProcessGroups()
    {
        foreach (var group in ProcessGroups)
        {
            group.PropertyChanged -= ProcessGroupPropertyChanged;
        }

        ProcessGroups.Clear();

        var groups = Processes
            .GroupBy(static process => GetProcessGroupKey(process), StringComparer.OrdinalIgnoreCase)
            .Select(CreateProcessGroup)
            .OrderByDescending(static group => group.WorkingSetBytes)
            .ThenBy(static group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            group.PropertyChanged += ProcessGroupPropertyChanged;
            ProcessGroups.Add(group);
        }
    }

    private ProcessGroupViewModel CreateProcessGroup(IGrouping<string, ProcessRowViewModel> group)
    {
        var children = group
            .OrderByDescending(static process => process.WorkingSetBytes)
            .ThenBy(static process => process.ProcessId)
            .ToList();
        var primary = children[0];
        var displayName = GetDisplayName(primary);
        var selectedAction = children
            .Select(static process => process.SelectedAction)
            .FirstOrDefault(static action => action != ProxyAction.None);

        foreach (var child in children)
        {
            child.SelectedAction = selectedAction;
        }

        return new ProcessGroupViewModel(
            group.Key,
            displayName,
            primary.ProcessName,
            primary.ProcessPath,
            primary.FileDescription,
            primary.MainWindowTitle,
            children.Sum(static process => process.WorkingSetBytes),
            selectedAction,
            _processIconCache.GetIcon(primary.ProcessPath),
            children);
    }

    private static string GetProcessGroupKey(ProcessRowViewModel process)
    {
        return $"name:{process.ProcessName}";
    }

    private static string GetDisplayName(ProcessRowViewModel process)
    {
        if (!string.IsNullOrWhiteSpace(process.FileDescription))
        {
            return process.FileDescription;
        }

        if (!string.IsNullOrWhiteSpace(process.ProductName))
        {
            return process.ProductName;
        }

        return Path.GetFileNameWithoutExtension(process.ProcessName);
    }

    private void ProcessGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingDuplicateRules ||
            e.PropertyName != nameof(ProcessGroupViewModel.SelectedAction) ||
            sender is not ProcessGroupViewModel changed)
        {
            return;
        }

        try
        {
            _syncingDuplicateRules = true;

            foreach (var process in changed.Processes)
            {
                process.SelectedAction = changed.SelectedAction;
            }

            foreach (var process in Processes.Where(process =>
                         changed.Processes.Any(child =>
                             string.Equals(child.ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase))))
            {
                process.SelectedAction = changed.SelectedAction;
            }

            foreach (var group in ProcessGroups)
            {
                group.RefreshFromChildren();
            }
        }
        finally
        {
            _syncingDuplicateRules = false;
        }
    }

    private string BuildMatchedRuleSummary(ProcessRowViewModel process)
    {
        return process.SelectedAction == ProxyAction.None
            ? "MATCH,DIRECT"
            : $"PROCESS-NAME,{process.ProcessName},{process.SelectedAction}";
    }

    private string BuildRouteChainSummary(ProxyAction action, int upstreamConnectionCount)
    {
        return action switch
        {
            ProxyAction.PROXY when UpstreamProxyPort > 0 && upstreamConnectionCount > 0 =>
                $"ProxyPilot -> {_settings.UpstreamProxyName} {UpstreamProxyHost}:{UpstreamProxyPort}",
            ProxyAction.PROXY when UpstreamProxyPort > 0 =>
                $"ProxyPilot -> {_settings.UpstreamProxyName} {UpstreamProxyHost}:{UpstreamProxyPort} ({Format("NoActiveUpstreamConnection")})",
            ProxyAction.PROXY =>
                $"ProxyPilot -> {Format("UpstreamNotDetectedShort")}",
            ProxyAction.DIRECT => "ProxyPilot -> DIRECT",
            ProxyAction.REJECT => "ProxyPilot -> REJECT",
            _ => "ProxyPilot -> DIRECT"
        };
    }

    private void RefreshRouteSummariesForLanguage()
    {
        foreach (var process in Processes)
        {
            process.UpdateRouteSummary(
                BuildMatchedRuleSummary(process),
                BuildRouteChainSummary(process.SelectedAction, process.UpstreamConnectionCount));
        }

        foreach (var group in ProcessGroups)
        {
            group.RefreshFromChildren();
        }
    }

    private string FormatUpstreamEndpoint()
    {
        return UpstreamProxyPort > 0
            ? $"{UpstreamProxyType}://{UpstreamProxyHost}:{UpstreamProxyPort}"
            : "not detected";
    }

    private void NormalizeSettings()
    {
        var appRoot = AppContext.BaseDirectory;
        var configRoot = Path.Combine(appRoot, "config");

        if (string.IsNullOrWhiteSpace(_settings.MihomoPath))
        {
            _settings.MihomoPath = Path.Combine(appRoot, "resources", "mihomo.exe");
        }

        if (string.IsNullOrWhiteSpace(_settings.TemplateConfigPath))
        {
            _settings.TemplateConfigPath = Path.Combine(configRoot, "template.yaml");
        }

        if (string.IsNullOrWhiteSpace(_settings.GeneratedConfigPath))
        {
            _settings.GeneratedConfigPath = Path.Combine(configRoot, "config.process-manager.yaml");
        }

        if (string.IsNullOrWhiteSpace(_settings.RuleSnippetPath))
        {
            _settings.RuleSnippetPath = RuleSnippetPath;
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiUrl) || _settings.ApiUrl.EndsWith(":9090", StringComparison.Ordinal))
        {
            _settings.ApiUrl = "http://127.0.0.1:19090";
        }

        if (string.IsNullOrWhiteSpace(_settings.Secret))
        {
            _settings.Secret = "your_password";
        }

        if (_settings.Language is not ChineseLanguage and not EnglishLanguage)
        {
            _settings.Language = ChineseLanguage;
        }

        if (string.IsNullOrWhiteSpace(_settings.UpstreamProxyName))
        {
            _settings.UpstreamProxyName = "ProxyPilot Upstream";
        }

        if (string.IsNullOrWhiteSpace(_settings.UpstreamProxyType))
        {
            _settings.UpstreamProxyType = "http";
        }

        if (string.IsNullOrWhiteSpace(_settings.UpstreamProxyHost))
        {
            _settings.UpstreamProxyHost = "127.0.0.1";
        }
    }

    private bool DetectAndApplyBestUpstreamProxy()
    {
        var detected = _localProxyDetector.DetectBest();
        if (detected is null)
        {
            return false;
        }

        _settings.UpstreamProxyName = "ProxyPilot Upstream";
        _settings.UpstreamProxyType = detected.Type;
        _settings.UpstreamProxyHost = detected.Host;
        _settings.UpstreamProxyPort = detected.Port;
        _settings.UpstreamProxySource = detected.Source;
        NotifyUpstreamChanged();
        return true;
    }

    private void PrepareTrafficTakeover()
    {
        if (_settings.SystemProxyTakeoverEnabled)
        {
            CaptureCurrentSystemProxyAsUpstream();
            return;
        }

        if (_settings.AutoDetectUpstreamProxy)
        {
            DetectAndApplyBestUpstreamProxy();
        }
    }

    private void CaptureCurrentSystemProxyAsUpstream()
    {
        var snapshot = _systemProxyManager.GetSnapshot();
        var takeoverProxy = GetProxyPilotSystemProxyAddress();
        var alreadyUsingProxyPilot = snapshot.Enabled &&
            string.Equals(snapshot.ProxyServer, takeoverProxy, StringComparison.OrdinalIgnoreCase);

        if (snapshot.Enabled &&
            !alreadyUsingProxyPilot &&
            TryParseProxyServer(snapshot.ProxyServer, out var host, out var port, out var type))
        {
            _settings.UpstreamProxyName = "ProxyPilot Upstream";
            _settings.UpstreamProxyType = type;
            _settings.UpstreamProxyHost = host;
            _settings.UpstreamProxyPort = port;
            _settings.UpstreamProxySource = "Previous Windows system proxy";
            NotifyUpstreamChanged();
        }
        else if (_settings.AutoDetectUpstreamProxy && _settings.UpstreamProxyPort <= 0)
        {
            DetectAndApplyBestUpstreamProxy();
        }

        if (IsProxyPilotEndpoint(_settings.UpstreamProxyHost, _settings.UpstreamProxyPort))
        {
            _settings.UpstreamProxyPort = 0;
            _settings.UpstreamProxySource = string.Empty;
            DetectAndApplyBestUpstreamProxy();
        }

        UpdateTunRouteExcludes();

        if (!alreadyUsingProxyPilot)
        {
            _settings.PreviousSystemProxyEnabled = snapshot.Enabled;
            _settings.PreviousSystemProxyServer = snapshot.ProxyServer;
            _settings.PreviousSystemProxyOverride = snapshot.ProxyOverride;
            _settings.PreviousSystemProxyAutoConfigUrl = snapshot.AutoConfigUrl;
            _settings.SystemProxyRestoreAvailable = true;
        }
    }

    private void ApplySystemProxyTakeover()
    {
        if (!_settings.SystemProxyTakeoverEnabled)
        {
            return;
        }

        _systemProxyManager.ApplyProxy(GetProxyPilotSystemProxyAddress());
        OnPropertyChanged(nameof(TrafficTakeoverStatus));
    }

    private void RestoreSystemProxyIfNeeded()
    {
        if (!_settings.SystemProxyRestoreAvailable)
        {
            return;
        }

        _systemProxyManager.Restore(new SystemProxySnapshot(
            _settings.PreviousSystemProxyEnabled,
            _settings.PreviousSystemProxyServer,
            _settings.PreviousSystemProxyOverride,
            _settings.PreviousSystemProxyAutoConfigUrl));
        _settings.SystemProxyRestoreAvailable = false;
    }

    private string GetProxyPilotSystemProxyAddress()
    {
        return $"127.0.0.1:{GetMihomoMixedPort()}";
    }

    private int GetMihomoMixedPort()
    {
        return AppSettings.ProxyPilotMixedPort;
    }

    private void UpdateTunRouteExcludes()
    {
        _settings.TunRouteExcludeAddresses = _upstreamBypassDetector
            .DetectRouteExcludeCidrs(_settings.UpstreamProxyPort)
            .ToList();
        _settings.TunProcessDirectNames = _upstreamBypassDetector
            .DetectProcessDirectNames(_settings.UpstreamProxyPort)
            .ToList();
        OnPropertyChanged(nameof(TunLoopbackStatus));
    }

    private void InspectTunLoopbackRisk()
    {
        _lastLoopbackResult = _upstreamLoopbackDetector.Detect(UpstreamProxyPort);
        OnPropertyChanged(nameof(TunLoopbackStatus));
    }

    private static bool IsProxyPilotEndpoint(string host, int port)
    {
        return port is AppSettings.ProxyPilotMixedPort or AppSettings.ProxyPilotApiPort &&
            IsLoopbackHost(host);
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseProxyServer(string proxyServer, out string host, out int port, out string type)
    {
        host = "127.0.0.1";
        port = 0;
        type = "http";

        if (string.IsNullOrWhiteSpace(proxyServer))
        {
            return false;
        }

        var first = proxyServer
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part =>
                part.Contains("=", StringComparison.Ordinal)
                    ? !part.StartsWith("ftp=", StringComparison.OrdinalIgnoreCase)
                    : true);

        if (string.IsNullOrWhiteSpace(first))
        {
            return false;
        }

        var value = first;
        if (first.Contains('=', StringComparison.Ordinal))
        {
            var pieces = first.Split('=', 2, StringSplitOptions.TrimEntries);
            type = pieces[0].Equals("socks", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http";
            value = pieces[1];
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            port = uri.Port;
            type = uri.Scheme.Equals("socks", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http";
            return port > 0;
        }

        var separator = value.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(value[(separator + 1)..], out port))
        {
            return false;
        }

        host = value[..separator].Trim('[', ']');
        return !string.IsNullOrWhiteSpace(host) && port > 0;
    }

    private bool LocateBundledMihomoIfPresent()
    {
        var appRoot = AppContext.BaseDirectory;
        var bundled = Path.Combine(appRoot, "resources", "mihomo.exe");

        if (File.Exists(bundled))
        {
            _settings.MihomoPath = bundled;
            OnPropertyChanged(nameof(MihomoPath));
            return true;
        }

        var workspaceBundled = Path.GetFullPath(Path.Combine(appRoot, "..", "..", "..", "..", "resources", "mihomo.exe"));
        if (File.Exists(workspaceBundled))
        {
            _settings.MihomoPath = workspaceBundled;
            OnPropertyChanged(nameof(MihomoPath));
            return true;
        }

        return File.Exists(_settings.MihomoPath);
    }

    private void ResetLocalizedState()
    {
        SetApiStatus("ApiNotChecked");
        SetMihomoRuntimeStatus("MihomoStopped");
        SetRuleStatus("RulesNotApplied");
        SetStatus("StatusInitializing");
    }

    private void SetApiStatus(string key, params object?[] arguments)
    {
        _apiStatusKey = key;
        _apiStatusArguments = arguments;
        OnPropertyChanged(nameof(ApiStatus));
    }

    private void SetMihomoRuntimeStatus(string key, params object?[] arguments)
    {
        _mihomoRuntimeStatusKey = key;
        _mihomoRuntimeStatusArguments = arguments;
        OnPropertyChanged(nameof(MihomoRuntimeStatus));
    }

    private void SetRuleStatus(string key, params object?[] arguments)
    {
        _ruleStatusKey = key;
        _ruleStatusArguments = arguments;
        OnPropertyChanged(nameof(RuleStatus));
    }

    private void SetStatus(string key, params object?[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments;
        OnPropertyChanged(nameof(StatusText));
    }

    private void NotifySettingsChanged()
    {
        OnPropertyChanged(nameof(MihomoPath));
        OnPropertyChanged(nameof(TemplateConfigPath));
        OnPropertyChanged(nameof(GeneratedConfigPath));
        OnPropertyChanged(nameof(ApiUrl));
        OnPropertyChanged(nameof(Secret));
        OnPropertyChanged(nameof(EnableTun));
        OnPropertyChanged(nameof(CloseMihomoOnExit));
        OnPropertyChanged(nameof(AutoDetectUpstreamProxy));
        OnPropertyChanged(nameof(TrafficTakeoverStatus));
        OnPropertyChanged(nameof(UpstreamHealthStatus));
        OnPropertyChanged(nameof(UpstreamHealthDetails));
        OnPropertyChanged(nameof(RouteChainStatus));
        OnPropertyChanged(nameof(TunLoopbackStatus));
        OnPropertyChanged(nameof(ChromeReloadHint));
        NotifyUpstreamChanged();
        OnPropertyChanged(nameof(TunStatus));
        OnPropertyChanged(nameof(AdministratorStatus));
    }

    private void NotifyLocalizationChanged()
    {
        RefreshRouteSummariesForLanguage();
        OnPropertyChanged(nameof(IsChinese));
        OnPropertyChanged(nameof(LanguageButtonText));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(VersionLabel));
        OnPropertyChanged(nameof(AdministratorStatus));
        OnPropertyChanged(nameof(TunStatus));
        OnPropertyChanged(nameof(ProcessCountText));
        OnPropertyChanged(nameof(ApiStatus));
        OnPropertyChanged(nameof(MihomoRuntimeStatus));
        OnPropertyChanged(nameof(RuleStatus));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(UpstreamProxyStatus));
        OnPropertyChanged(nameof(TrafficTakeoverStatus));
        OnPropertyChanged(nameof(UpstreamHealthStatus));
        OnPropertyChanged(nameof(UpstreamHealthDetails));
        OnPropertyChanged(nameof(RouteChainStatus));
        OnPropertyChanged(nameof(TunLoopbackStatus));
        OnPropertyChanged(nameof(ChromeReloadHint));
        OnPropertyChanged(nameof(SelectedProcess));
        OnPropertyChanged("Item[]");
    }

    private void NotifyUpstreamChanged()
    {
        OnPropertyChanged(nameof(UpstreamProxyType));
        OnPropertyChanged(nameof(UpstreamProxyHost));
        OnPropertyChanged(nameof(UpstreamProxyPort));
        OnPropertyChanged(nameof(UpstreamProxyStatus));
        OnPropertyChanged(nameof(UpstreamHealthStatus));
        OnPropertyChanged(nameof(RouteChainStatus));
        OnPropertyChanged(nameof(TunLoopbackStatus));
    }

    private string Format(string key, params object?[] arguments)
    {
        return Localizer.Format(IsChinese, key, arguments);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
