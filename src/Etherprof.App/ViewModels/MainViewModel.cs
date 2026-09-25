using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Core;
using Etherprof.StreamTest.Models;
using Microsoft.Extensions.Logging;

namespace Etherprof.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly INetworkAdapterProvider _adapterProvider;
    private readonly INetworkConfigurationService _configService;
    private readonly ProfileManager _profileManager;
    private readonly TestSetManager _testSetManager;
    private readonly ITestRunner _testRunner;
    private readonly ISettingsRepository _settingsRepo;
    private readonly AppSettings _settings;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    public WifiPanelViewModel WifiPanel { get; }
    public SpeedTestPanelViewModel SpeedTestPanel { get; }
    public StreamPanelViewModel StreamPanel { get; }
    public StreamServerPanelViewModel StreamServerPanel { get; }

    public AppSettings Settings => _settings;
    public ISettingsRepository SettingsRepo => _settingsRepo;

    private NetworkAdapterState? _adapterState;
    private bool _isRefreshing;
    private TestSet? _activeRunningSet;

    // Dialog open actions - set from code-behind
    public Action<NetworkProfile?>? OpenProfileEditor { get; set; }
    public Action<TestSet?>? OpenTestSetEditor { get; set; }
    public Action? OpenCaptureProfile { get; set; }
    public Action? OpenAdapterSelector { get; set; }
    public Action<string, string?>? OpenAliasEditorDialog { get; set; }
    public Action? RequestShowMiniHud { get; set; }
    public Action? RequestHideMiniHud { get; set; }
    public Action? RestoreMainWindow { get; set; }
    public Action<string>? OpenAdHocPingDialog { get; set; }
    public Action<string>? OpenPingPrefixDialog { get; set; }

    // Adapter info
    private string _adapterName = "No adapter selected";
    public string AdapterName { get => _adapterName; set { _adapterName = value; OnPropertyChanged(); } }

    private string _linkStatus = "";
    public string LinkStatus { get => _linkStatus; set { _linkStatus = value; OnPropertyChanged(); } }

    private string _linkSpeed = "";
    public string LinkSpeed { get => _linkSpeed; set { _linkSpeed = value; OnPropertyChanged(); } }

    private string _currentIp = "";
    public string CurrentIp { get => _currentIp; set { _currentIp = value; OnPropertyChanged(); } }

    private string _dhcpStatus = "";
    public string DhcpStatus { get => _dhcpStatus; set { _dhcpStatus = value; OnPropertyChanged(); } }

    private bool _adapterAvailable;
    public bool AdapterAvailable { get => _adapterAvailable; set { _adapterAvailable = value; OnPropertyChanged(); } }

    private bool _hasSelectedAdapter;
    public bool HasSelectedAdapter { get => _hasSelectedAdapter; set { _hasSelectedAdapter = value; OnPropertyChanged(); } }

    // Quick IP
    private string _quickIpText = "";
    public string QuickIpText { get => _quickIpText; set { _quickIpText = value; OnPropertyChanged(); QuickIpError = ""; } }

    private string _quickIpError = "";
    public string QuickIpError { get => _quickIpError; set { _quickIpError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasQuickIpError)); } }
    public bool HasQuickIpError => !string.IsNullOrEmpty(_quickIpError);

    private bool _isApplyingQuickIp;
    public bool IsApplyingQuickIp { get => _isApplyingQuickIp; set { _isApplyingQuickIp = value; OnPropertyChanged(); } }

    // Test results visibility
    private bool _testResultsVisible;
    public bool TestResultsVisible { get => _testResultsVisible; set { _testResultsVisible = value; OnPropertyChanged(); } }

    private string _activeTestSetName = "";
    public string ActiveTestSetName { get => _activeTestSetName; set { _activeTestSetName = value; OnPropertyChanged(); } }

    // Collections
    public ObservableCollection<ProfileButtonViewModel> ProfileButtons { get; } = new();
    public ObservableCollection<TestSetButtonViewModel> TestSetButtons { get; } = new();
    public ObservableCollection<TestResultViewModel> TestResults { get; } = new();

    // Commands
    public ICommand ApplyProfileCommand { get; }
    public ICommand ToggleTestCommand { get; }
    public ICommand ApplyQuickIpCommand { get; }
    public ICommand AddProfileCommand { get; }
    public ICommand AddTestSetCommand { get; }
    public ICommand EditProfileCommand { get; }
    public ICommand DuplicateProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand EditTestSetCommand { get; }
    public ICommand DuplicateTestSetCommand { get; }
    public ICommand DeleteTestSetCommand { get; }
    public ICommand SelectAdapterCommand { get; }
    public ICommand CaptureCurrentCommand { get; }
    public ICommand MimicDhcpCommand { get; }
    public ICommand AddAdditionalIpCommand { get; }
    public ICommand OpenAdapterStatusCommand { get; }
    public ICommand ToggleMiniHudCommand { get; }
    public ICommand CopyProfileSettingsCommand { get; }
    public ICommand PasteQuickIpCommand { get; }
    public ICommand CopyCurrentIpCommand { get; }
    public ICommand AdHocPingCommand { get; }
    public ICommand PingPrefixCommand { get; }
    public ICommand OpenNcpaCommand { get; }
    public ICommand DiagnoseConnectionCommand { get; }
    public ICommand ToggleAdapterCommand { get; }
    public ICommand ToggleWirelessCommand { get; }
    public ICommand RemovePingTargetCommand { get; }
    public ICommand ToggleInternetCheckCommand { get; }
    public string AdapterToggleLabel => (_adapterState?.IsConnected ?? false) ? "Disable" : "Enable";

    private bool _complementAdHocPing;
    public bool ComplementAdHocPing
    {
        get => _complementAdHocPing;
        set
        {
            if (_complementAdHocPing != value)
            {
                _complementAdHocPing = value;
                OnPropertyChanged();
                _settings.ComplementAdHocPing = value;
                _ = _settingsRepo.SaveAsync(_settings);
            }
        }
    }

    // Version & Compile Date
    public string AppVersion { get; } = "v0.4.2";
    public string BuildDate { get; }
    public string BuildInfo { get; }

    // Media Link Status & Adapter Card Section Color Theme
    private bool? _lastLoggedConnectedState;
    public bool IsMediaConnected => _adapterState?.IsConnected == true;
    public string MediaLinkText => IsMediaConnected ? "LINK UP" : "LINK DOWN";
    public Brush MediaLinkBrush => IsMediaConnected
        ? new SolidColorBrush(Color.FromRgb(22, 163, 74))   // #16A34A (green)
        : new SolidColorBrush(Color.FromRgb(220, 38, 38));  // #DC2626 (red)

    public Brush AdapterCardBackgroundBrush => !HasSelectedAdapter || !AdapterAvailable
        ? new SolidColorBrush(Color.FromRgb(248, 250, 252)) // #F8FAFC neutral
        : IsMediaConnected
            ? new SolidColorBrush(Color.FromRgb(240, 253, 244)) // #F0FDF4 rich soft green
            : new SolidColorBrush(Color.FromRgb(254, 242, 242)); // #FEF2F2 vivid soft red

    public Brush AdapterCardBorderBrush => !HasSelectedAdapter || !AdapterAvailable
        ? new SolidColorBrush(Color.FromRgb(226, 232, 240)) // #E2E8F0
        : IsMediaConnected
            ? new SolidColorBrush(Color.FromRgb(134, 239, 172)) // #86EFAC green
            : new SolidColorBrush(Color.FromRgb(252, 165, 165)); // #FCA5A5 red

    public Brush AdapterCardForegroundBrush => !HasSelectedAdapter || !AdapterAvailable
        ? new SolidColorBrush(Color.FromRgb(71, 85, 105))  // #475569
        : IsMediaConnected
            ? new SolidColorBrush(Color.FromRgb(21, 128, 61))  // #15803D
            : new SolidColorBrush(Color.FromRgb(185, 28, 28)); // #B91C1C

    // Activity Log Topic Filter
    public ICollectionView FilteredActivityLog { get; }
    private string _selectedLogFilter = "ALL";
    public string SelectedLogFilter
    {
        get => _selectedLogFilter;
        set
        {
            if (_selectedLogFilter != value)
            {
                _selectedLogFilter = value;
                OnPropertyChanged();
                FilteredActivityLog.Refresh();
            }
        }
    }
    public ICommand SetLogFilterCommand { get; }
    public ICommand SwitchToNextAdapterCommand { get; }

    public MainViewModel(
        INetworkAdapterProvider adapterProvider,
        INetworkConfigurationService configService,
        ProfileManager profileManager,
        TestSetManager testSetManager,
        ITestRunner testRunner,
        ISettingsRepository settingsRepo,
        AppSettings settings,
        WifiPanelViewModel wifiPanel,
        SpeedTestPanelViewModel speedTestPanel,
        StreamPanelViewModel streamPanel,
        StreamServerPanelViewModel streamServerPanel,
        ILogger<MainViewModel> logger)
    {
        _adapterProvider = adapterProvider;
        _configService = configService;
        _profileManager = profileManager;
        _testSetManager = testSetManager;
        _testRunner = testRunner;
        _settingsRepo = settingsRepo;
        _settings = settings;
        WifiPanel = wifiPanel;
        SpeedTestPanel = speedTestPanel;
        StreamPanel = streamPanel;
        StreamServerPanel = streamServerPanel;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

        WifiPanel.OpenAliasEditor = (bssid, alias) => OpenAliasEditorDialog?.Invoke(bssid, alias);

        // Wire commands
        ApplyProfileCommand = new RelayCommand(o => _ = ApplyProfileAsync(o as ProfileButtonViewModel));
        ToggleTestCommand = new RelayCommand(o => _ = ToggleTestAsync(o as TestSetButtonViewModel));
        ApplyQuickIpCommand = new RelayCommand(_ => _ = ApplyQuickIpAsync());
        AddProfileCommand = new RelayCommand(() => OpenProfileEditor?.Invoke(null));
        AddTestSetCommand = new RelayCommand(() => OpenTestSetEditor?.Invoke(null));
        EditProfileCommand = new RelayCommand(o => { if (o is ProfileButtonViewModel vm) OpenProfileEditor?.Invoke(vm.Profile); });
        DuplicateProfileCommand = new RelayCommand(o => _ = DuplicateProfileAsync(o as ProfileButtonViewModel));
        DeleteProfileCommand = new RelayCommand(o => _ = DeleteProfileAsync(o as ProfileButtonViewModel));
        EditTestSetCommand = new RelayCommand(o => { if (o is TestSetButtonViewModel vm) OpenTestSetEditor?.Invoke(vm.TestSet); });
        DuplicateTestSetCommand = new RelayCommand(o => _ = DuplicateTestSetAsync(o as TestSetButtonViewModel));
        DeleteTestSetCommand = new RelayCommand(o => _ = DeleteTestSetAsync(o as TestSetButtonViewModel));
        SelectAdapterCommand = new RelayCommand(() => OpenAdapterSelector?.Invoke());
        CaptureCurrentCommand = new RelayCommand(() => OpenCaptureProfile?.Invoke());
        ClearActivityLogCommand = new RelayCommand(() => ActivityLog.Clear());
        MimicDhcpCommand = new RelayCommand(o => _ = LearnDhcpProfileAsync());
        AddAdditionalIpCommand = new RelayCommand(o => _ = AddAdditionalIpAsync());
        OpenAdapterStatusCommand = new RelayCommand(o => OpenActiveAdapterStatusWindow());
        ToggleMiniHudCommand = new RelayCommand(() => IsMiniHudVisible = !IsMiniHudVisible);
        AdHocPingCommand = new RelayCommand(() => OpenAdHocPingDialog?.Invoke(GetDefaultAdHocIp()));
        PingPrefixCommand = new RelayCommand(() => OpenPingPrefixDialog?.Invoke(GetDefaultSubnetPrefix()));
        OpenNcpaCommand = new RelayCommand(() =>
        {
            try { Process.Start(new ProcessStartInfo("ncpa.cpl") { UseShellExecute = true }); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to open ncpa.cpl"); }
        });
        DiagnoseConnectionCommand = new RelayCommand(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("msdt.exe", "-id NetworkDiagnosticsNetworkAdapter") { UseShellExecute = true });
                AddActivityLog("ADAPTER", "Launched Windows Network Diagnostics.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to launch Windows Network Diagnostics");
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c start ms-settings:troubleshoot") { UseShellExecute = true, CreateNoWindow = true });
                }
                catch { }
            }
        });
        ToggleAdapterCommand = new RelayCommand(_ => _ = ToggleAdapterAsync());
        ToggleWirelessCommand = new RelayCommand(_ => _ = ToggleWirelessAsync());
        RemovePingTargetCommand = new RelayCommand(o => RemovePingTarget(o as TestResultViewModel));
        ToggleInternetCheckCommand = new RelayCommand(() =>
        {
            InternetCheckEnabled = !InternetCheckEnabled;
        });
        PasteQuickIpCommand = new RelayCommand(_ =>
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText().Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    QuickIpText = text;
                }
            }
        });
        CopyCurrentIpCommand = new RelayCommand(_ =>
        {
            string ipToCopy = !string.IsNullOrEmpty(QuickIpText) ? QuickIpText : (_adapterState?.IPv4Address ?? CurrentIp);
            if (!string.IsNullOrEmpty(ipToCopy))
            {
                Clipboard.SetText(ipToCopy);
                AddActivityLog("IP", $"Copied IP to clipboard: {ipToCopy}");
            }
        });
        CopyProfileSettingsCommand = new RelayCommand(o =>
        {
            if (o is ProfileButtonViewModel vm)
            {
                string summary = vm.GetClipboardSummary();
                Clipboard.SetText(summary);
                AddActivityLog("PROFILE", $"Copied profile settings to clipboard: {vm.Profile.Name}");
            }
        });

        // Version and compile timestamp
        DateTime buildDate;
        try
        {
            string? procPath = Environment.ProcessPath;
            if (procPath != null && File.Exists(procPath))
            {
                buildDate = File.GetLastWriteTime(procPath);
            }
            else
            {
                string baseDir = AppContext.BaseDirectory;
                string exePath = Path.Combine(baseDir, "Etherprof.exe");
                buildDate = File.Exists(exePath) ? File.GetLastWriteTime(exePath) : DateTime.Now;
            }
        }
        catch
        {
            buildDate = DateTime.Now;
        }
        BuildDate = buildDate.ToString("yyyy-MM-dd HH:mm");
        BuildInfo = $"v0.4.2 · Built {BuildDate}";

        // Filtered activity log with category topic filtering
        FilteredActivityLog = CollectionViewSource.GetDefaultView(ActivityLog);
        FilteredActivityLog.Filter = o =>
        {
            if (string.IsNullOrEmpty(_selectedLogFilter) || _selectedLogFilter == "ALL") return true;
            if (o is ActivityLogEntryViewModel entry)
            {
                return string.Equals(entry.Category, _selectedLogFilter, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        };

        SetLogFilterCommand = new RelayCommand(o =>
        {
            if (o is string filter) SelectedLogFilter = filter;
        });

        SwitchToNextAdapterCommand = new RelayCommand(_ => _ = SwitchToNextAdapterAsync());

        // Subscribe to adapter changes and panel events for activity logging
        _adapterProvider.AdapterChanged += OnAdapterChanged;
        _testRunner.ResultUpdated += OnTestResultUpdated;

        WifiPanel.EventLogged += OnWifiEventLogged;
        SpeedTestPanel.TestCompleted += OnSpeedTestCompleted;
        StreamPanel.StateChanged += OnStreamStateChanged;

        // Initialize expander states from persisted settings
        _quickIpExpanded = _settings.QuickIpExpanded;
        _wifiExpanded = _settings.WifiExpanded;
        _speedTestExpanded = _settings.SpeedTestExpanded;
        _streamClientExpanded = _settings.StreamClientExpanded;
        _streamServerExpanded = _settings.StreamServerExpanded;
        _activityLogExpanded = _settings.ActivityLogExpanded;
        _testResultsExpanded = _settings.TestResultsExpanded;

        // Initialize Quick Ping & Complement settings
        _isQuickPing = _settings.IsQuickPing;
        _testRunner.IntervalMs = _isQuickPing ? 200 : 1000;
        _complementAdHocPing = _settings.ComplementAdHocPing;

        // Initialize section visibility from persisted settings
        _quickIpVisible = _settings.QuickIpVisible;
        _wifiVisible = _settings.WifiVisible;
        _speedTestVisible = _settings.SpeedTestVisible;
        _streamClientVisible = _settings.StreamClientVisible;
        _streamServerVisible = _settings.StreamServerVisible;
        _activityLogVisible = _settings.ActivityLogVisible;
        _testResultsSectionVisible = _settings.TestResultsVisible;

        // Initialize internet check
        _internetCheckEnabled = _settings.InternetCheckEnabled;
        if (_internetCheckEnabled) StartInternetCheck();
    }

    // --- Expander States (Persisted) ---
    private bool _quickIpExpanded;
    public bool QuickIpExpanded
    {
        get => _quickIpExpanded;
        set { if (_quickIpExpanded != value) { _quickIpExpanded = value; OnPropertyChanged(); SaveExpanderStates(); } }
    }

    private bool _wifiExpanded;
    public bool WifiExpanded
    {
        get => _wifiExpanded;
        set { if (_wifiExpanded != value) { _wifiExpanded = value; OnPropertyChanged(); SaveExpanderStates(); } }
    }

    private bool _speedTestExpanded;
    public bool SpeedTestExpanded
    {
        get => _speedTestExpanded;
        set { if (_speedTestExpanded != value) { _speedTestExpanded = value; OnPropertyChanged(); SaveExpanderStates(); } }
    }

    private bool _streamClientExpanded;
    public bool StreamClientExpanded
    {
        get => _streamClientExpanded;
        set { if (_streamClientExpanded != value) { _streamClientExpanded = value; OnPropertyChanged(); SaveExpanderStates(); } }
    }

    private bool _streamServerExpanded;
    public bool StreamServerExpanded
    {
        get => _streamServerExpanded;
        set { if (_streamServerExpanded != value) { _streamServerExpanded = value; OnPropertyChanged(); SaveExpanderStates(); } }
    }

    private bool _activityLogExpanded;
    public bool ActivityLogExpanded
    {
        get => _activityLogExpanded;
        set { if (_activityLogExpanded != value) { _activityLogExpanded = value; OnPropertyChanged(); SaveExpanderStates(); } }
    }

    private bool _testResultsExpanded;
    public bool TestResultsExpanded
    {
        get => _testResultsExpanded;
        set { if (_testResultsExpanded != value) { _testResultsExpanded = value; OnPropertyChanged(); SaveExpanderStates(); } }
    }

    private void SaveExpanderStates()
    {
        _settings.QuickIpExpanded = QuickIpExpanded;
        _settings.WifiExpanded = WifiExpanded;
        _settings.SpeedTestExpanded = SpeedTestExpanded;
        _settings.StreamClientExpanded = StreamClientExpanded;
        _settings.StreamServerExpanded = StreamServerExpanded;
        _settings.ActivityLogExpanded = ActivityLogExpanded;
        _settings.TestResultsExpanded = TestResultsExpanded;
        _ = _settingsRepo.SaveAsync(_settings);
    }

    // --- Section Visibility & Order (persisted hide/show in activation log order) ---
    public event Action<string, bool>? SectionVisibilityChanged;

    private void UpdateSectionVisibility(string sectionKey, bool isVisible)
    {
        if (isVisible)
        {
            if (!_settings.SectionOrder.Contains(sectionKey))
            {
                _settings.SectionOrder.Add(sectionKey);
            }
        }
        else
        {
            _settings.SectionOrder.Remove(sectionKey);
        }
        SaveSectionVisibility();
        SectionVisibilityChanged?.Invoke(sectionKey, isVisible);
    }

    private bool _quickIpVisible = true;
    public bool QuickIpVisible
    {
        get => _quickIpVisible;
        set
        {
            if (_quickIpVisible != value)
            {
                _quickIpVisible = value;
                OnPropertyChanged();
                UpdateSectionVisibility("QuickIp", value);
            }
        }
    }

    private bool _wifiVisible = true;
    public bool WifiVisible
    {
        get => _wifiVisible;
        set
        {
            if (_wifiVisible != value)
            {
                _wifiVisible = value;
                OnPropertyChanged();
                UpdateSectionVisibility("Wifi", value);
            }
        }
    }

    private bool _speedTestVisible = true;
    public bool SpeedTestVisible
    {
        get => _speedTestVisible;
        set
        {
            if (_speedTestVisible != value)
            {
                _speedTestVisible = value;
                OnPropertyChanged();
                UpdateSectionVisibility("SpeedTest", value);
            }
        }
    }

    private bool _streamClientVisible = true;
    public bool StreamClientVisible
    {
        get => _streamClientVisible;
        set
        {
            if (_streamClientVisible != value)
            {
                _streamClientVisible = value;
                OnPropertyChanged();
                UpdateSectionVisibility("StreamClient", value);
            }
        }
    }

    private bool _streamServerVisible = true;
    public bool StreamServerVisible
    {
        get => _streamServerVisible;
        set
        {
            if (_streamServerVisible != value)
            {
                _streamServerVisible = value;
                OnPropertyChanged();
                UpdateSectionVisibility("StreamServer", value);
            }
        }
    }

    private bool _activityLogVisible = true;
    public bool ActivityLogVisible
    {
        get => _activityLogVisible;
        set
        {
            if (_activityLogVisible != value)
            {
                _activityLogVisible = value;
                OnPropertyChanged();
                UpdateSectionVisibility("ActivityLog", value);
            }
        }
    }

    private bool _testResultsSectionVisible = true;
    public bool TestResultsSectionVisible
    {
        get => _testResultsSectionVisible;
        set
        {
            if (_testResultsSectionVisible != value)
            {
                _testResultsSectionVisible = value;
                OnPropertyChanged();
                UpdateSectionVisibility("TestResults", value);
            }
        }
    }

    private void SaveSectionVisibility()
    {
        _settings.QuickIpVisible = QuickIpVisible;
        _settings.WifiVisible = WifiVisible;
        _settings.SpeedTestVisible = SpeedTestVisible;
        _settings.StreamClientVisible = StreamClientVisible;
        _settings.StreamServerVisible = StreamServerVisible;
        _settings.ActivityLogVisible = ActivityLogVisible;
        _settings.TestResultsVisible = TestResultsSectionVisible;
        _ = _settingsRepo.SaveAsync(_settings);
    }

    // --- Internet Check (8.8.8.8 ping → window title ONLINE/OFFLINE) ---
    private System.Threading.Timer? _internetCheckTimer;
    private bool _internetCheckEnabled = true;
    public bool InternetCheckEnabled
    {
        get => _internetCheckEnabled;
        set
        {
            if (_internetCheckEnabled != value)
            {
                _internetCheckEnabled = value;
                OnPropertyChanged();
                _settings.InternetCheckEnabled = value;
                _ = _settingsRepo.SaveAsync(_settings);
                if (value) StartInternetCheck(); else StopInternetCheck();
            }
        }
    }

    private bool _isOnline;
    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline != value)
            {
                _isOnline = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(InternetStatusText));
                OnPropertyChanged(nameof(InternetStatusBrush));
            }
        }
    }

    public string WindowTitle => _internetCheckEnabled
        ? (IsOnline ? "ONLINE Etherprof" : "OFFLINE Etherprof")
        : "Etherprof";

    public string InternetStatusText => IsOnline ? "ONLINE" : "OFFLINE";
    public string InternetStatusBrush => IsOnline ? "#16A34A" : "#DC2626";

    private void StartInternetCheck()
    {
        StopInternetCheck();
        _internetCheckTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                _ = _dispatcher.BeginInvoke(() => IsOnline = reply.Status == System.Net.NetworkInformation.IPStatus.Success);
            }
            catch
            {
                _ = _dispatcher.BeginInvoke(() => IsOnline = false);
            }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void StopInternetCheck()
    {
        _internetCheckTimer?.Dispose();
        _internetCheckTimer = null;
        OnPropertyChanged(nameof(WindowTitle));
    }

    // --- Adapter Toggle (Disable/Enable via netsh) ---
    private async Task ToggleAdapterAsync()
    {
        if (string.IsNullOrEmpty(_settings.SelectedAdapterId)) return;
        string friendlyName = GetAdapterFriendlyName();
        if (string.IsNullOrEmpty(friendlyName)) return;

        bool isUp = _adapterState?.IsConnected ?? false;
        string adminState = isUp ? "DISABLED" : "ENABLED";
        string actionText = isUp ? "disabling" : "enabling";
        AddActivityLog("ADAPTER", $"{char.ToUpper(actionText[0])}{actionText[1..]} adapter '{friendlyName}'...");

        try
        {
            var psi = new ProcessStartInfo("netsh", $"interface set interface name=\"{friendlyName}\" admin={adminState}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
            await Task.Delay(1500);
            await RefreshAdapterStateAsync();
            OnPropertyChanged(nameof(AdapterToggleLabel));
            AddActivityLog("ADAPTER", $"Adapter '{friendlyName}' set to {adminState}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to toggle adapter");
            AddActivityLog("ADAPTER", $"Failed to {actionText} adapter: {ex.Message}");
        }
    }

    private async Task ToggleWirelessAsync()
    {
        AddActivityLog("WIFI", "Toggling Wi-Fi wireless connectivity (Win+A radio)...");

        try
        {
            // Execute Windows.Devices.Radios.Radio state toggle via PowerShell encoded script
            string script = @"
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Devices.Radios.Radio, Windows.System.Devices, ContentType = WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1 -and $_.GetGenericArguments().Count -eq 1 } | Select-Object -First 1
$op = [Windows.Devices.Radios.Radio]::GetRadiosAsync()
$radios = $asTask.MakeGenericMethod([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]]).Invoke($null, @($op)).GetAwaiter().GetResult()
$wifi = $radios | Where-Object { $_.Kind -eq [Windows.Devices.Radios.RadioKind]::WiFi } | Select-Object -First 1
if ($wifi) {
    $target = if ($wifi.State -eq [Windows.Devices.Radios.RadioState]::On) { [Windows.Devices.Radios.RadioState]::Off } else { [Windows.Devices.Radios.RadioState]::On }
    $setOp = $wifi.SetStateAsync($target)
    $null = $asTask.MakeGenericMethod([Windows.Devices.Radios.RadioAccessStatus]).Invoke($null, @($setOp)).GetAwaiter().GetResult()
    Write-Output ""RADIO_STATE:$target""
} else {
    Write-Output ""NO_RADIO""
}";
            string base64 = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {base64}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi);
            if (p != null)
            {
                string output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                if (output.Contains("RADIO_STATE:Off"))
                {
                    AddActivityLog("WIFI", "Wi-Fi radio switched OFF (wireless connectivity disabled).");
                }
                else if (output.Contains("RADIO_STATE:On"))
                {
                    AddActivityLog("WIFI", "Wi-Fi radio switched ON (wireless connectivity enabled).");
                }
                else
                {
                    _logger.LogWarning("WinRT Wi-Fi radio not found in output: {Output}", output);
                    AddActivityLog("WIFI", "WinRT radio not detected; attempting interface fallback...");
                    await FallbackToggleInterfaceAsync();
                    return;
                }
            }

            await Task.Delay(1500);
            await RefreshAdapterStateAsync();
            if (!string.IsNullOrEmpty(_settings.SelectedAdapterId))
            {
                await WifiPanel.UpdateSelectedAdapterAsync(_settings.SelectedAdapterId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to toggle Wi-Fi radio");
            AddActivityLog("WIFI", $"Failed to toggle Wi-Fi radio: {ex.Message}");
        }
    }

    private async Task FallbackToggleInterfaceAsync()
    {
        string? wifiName = GetWifiInterfaceName();
        if (string.IsNullOrEmpty(wifiName))
        {
            AddActivityLog("ADAPTER", "No Wi-Fi interface detected on this machine.");
            return;
        }

        bool isCurrentlyEnabled = IsWifiInterfaceEnabled(wifiName);
        string targetAdmin = isCurrentlyEnabled ? "DISABLED" : "ENABLED";
        string actionText = isCurrentlyEnabled ? "turning OFF" : "turning ON";

        try
        {
            var psi = new ProcessStartInfo("netsh", $"interface set interface name=\"{wifiName}\" admin={targetAdmin}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();

            await Task.Delay(2000);
            await RefreshAdapterStateAsync();
            if (!string.IsNullOrEmpty(_settings.SelectedAdapterId))
            {
                await WifiPanel.UpdateSelectedAdapterAsync(_settings.SelectedAdapterId);
            }

            AddActivityLog("ADAPTER", $"Wi-Fi interface '{wifiName}' set to {targetAdmin}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to toggle Wi-Fi interface");
            AddActivityLog("ADAPTER", $"Failed to {actionText} Wi-Fi interface: {ex.Message}");
        }
    }

    private string? GetWifiInterfaceName()
    {
        if (_adapterState != null)
        {
            var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Id == _adapterState.AdapterId);
            if (nic != null && nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
            {
                return nic.Name;
            }
        }

        var wifiNic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211);
        if (wifiNic != null) return wifiNic.Name;

        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                var match = System.Text.RegularExpressions.Regex.Match(output, @"Name\s*:\s*(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
                if (match.Success) return match.Groups[1].Value.Trim();
            }
        }
        catch { }

        return "Wl.intel";
    }

    private bool IsWifiInterfaceEnabled(string wifiName)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", $"interface show interface name=\"{wifiName}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                if (output.Contains("Disabled", StringComparison.OrdinalIgnoreCase)) return false;
                if (output.Contains("Enabled", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch { }

        var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Name.Equals(wifiName, StringComparison.OrdinalIgnoreCase));
        return nic != null && nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Down;
    }

    private string GetAdapterFriendlyName()
    {
        string friendlyName = AdapterName;
        if (string.IsNullOrEmpty(friendlyName) || friendlyName == "No adapter selected" || friendlyName.StartsWith("{"))
        {
            var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Id == _settings.SelectedAdapterId);
            if (nic != null) friendlyName = nic.Name;
        }
        return friendlyName;
    }

    // --- Remove Ping Target on-the-fly ---
    private void RemovePingTarget(TestResultViewModel? resultVm)
    {
        if (resultVm is null) return;
        TestResults.Remove(resultVm);
        OnPropertyChanged(nameof(PrimaryPingResult));

        // Stop pinging this target in the background runner by removing it from active target list
        if (_testRunner.ActiveTestSetId.HasValue)
        {
            var activeSet = _testSetManager.TestSets.FirstOrDefault(t => t.Id == _testRunner.ActiveTestSetId.Value);
            if (activeSet != null)
            {
                var target = activeSet.Targets.FirstOrDefault(t => t.Id == resultVm.TargetId);
                if (target != null) activeSet.Targets.Remove(target);
            }
        }
        if (_activeRunningSet != null)
        {
            var target = _activeRunningSet.Targets.FirstOrDefault(t => t.Id == resultVm.TargetId);
            if (target != null) _activeRunningSet.Targets.Remove(target);
        }

        if (TestResults.Count == 0)
        {
            _testRunner.Stop();
            _activeRunningSet = null;
            TestResultsVisible = false;
            ActiveTestSetName = "";
            ResetTaskbarState();
            OnPropertyChanged(nameof(IsPingRunning));
        }
    }

    // --- Quick Ping (200ms) ---
    private bool _isQuickPing;
    public bool IsQuickPing
    {
        get => _isQuickPing;
        set
        {
            if (_isQuickPing != value)
            {
                _isQuickPing = value;
                OnPropertyChanged();
                _testRunner.IntervalMs = value ? 200 : 1000;
                _settings.IsQuickPing = value;
                _ = _settingsRepo.SaveAsync(_settings);
            }
        }
    }

    public bool CanLearnDhcp => HasSelectedAdapter;
    public bool CanMimicDhcp => HasSelectedAdapter;
    public bool IsStaticIpSet => HasSelectedAdapter;

    // --- Taskbar & Mini-HUD Integration ---
    private static readonly ImageSource SuccessOverlay = CreateStatusBadge(true);
    private static readonly ImageSource FailureOverlay = CreateStatusBadge(false);

    private static ImageSource CreateStatusBadge(bool success)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(success ? "#16A34A" : "#DC2626"));
            brush.Freeze();
            var pen = new Pen(Brushes.White, 1.8);
            pen.Freeze();
            dc.DrawEllipse(brush, pen, new System.Windows.Point(8, 8), 6.5, 6.5);
        }
        group.Freeze();
        var img = new DrawingImage(group);
        img.Freeze();
        return img;
    }

    private TaskbarItemProgressState _taskbarProgressState = TaskbarItemProgressState.None;
    public TaskbarItemProgressState TaskbarProgressState
    {
        get => _taskbarProgressState;
        set { if (_taskbarProgressState != value) { _taskbarProgressState = value; OnPropertyChanged(); } }
    }

    private double _taskbarProgressValue = 0.0;
    public double TaskbarProgressValue
    {
        get => _taskbarProgressValue;
        set { if (_taskbarProgressValue != value) { _taskbarProgressValue = value; OnPropertyChanged(); } }
    }

    private ImageSource? _taskbarOverlay;
    public ImageSource? TaskbarOverlay
    {
        get => _taskbarOverlay;
        set { if (_taskbarOverlay != value) { _taskbarOverlay = value; OnPropertyChanged(); } }
    }

    private string _taskbarDescription = "Etherprof";
    public string TaskbarDescription
    {
        get => _taskbarDescription;
        set { if (_taskbarDescription != value) { _taskbarDescription = value; OnPropertyChanged(); } }
    }

    public bool IsMiniHudVisible
    {
        get => _settings.IsMiniHudVisible;
        set
        {
            if (_settings.IsMiniHudVisible != value)
            {
                _settings.IsMiniHudVisible = value;
                OnPropertyChanged();
                _ = _settingsRepo.SaveAsync(_settings);
                if (value) RequestShowMiniHud?.Invoke();
                else RequestHideMiniHud?.Invoke();
            }
        }
    }

    public bool AutoShowHudOnMinimize
    {
        get => _settings.AutoShowHudOnMinimize;
        set
        {
            if (_settings.AutoShowHudOnMinimize != value)
            {
                _settings.AutoShowHudOnMinimize = value;
                OnPropertyChanged();
                _ = _settingsRepo.SaveAsync(_settings);
            }
        }
    }

    public bool IsPingRunning => _testRunner.IsRunning;

    public TestResultViewModel? PrimaryPingResult => TestResults.FirstOrDefault();

    public void UpdateTaskbarState(TestResult result, string targetName)
    {
        if (!_testRunner.IsRunning)
        {
            ResetTaskbarState();
            return;
        }

        if (result.Status == TestStatus.Success)
        {
            TaskbarProgressState = TaskbarItemProgressState.Normal;
            TaskbarProgressValue = 1.0;
            TaskbarOverlay = SuccessOverlay;
            double ms = result.Latency?.TotalMilliseconds ?? 0;
            TaskbarDescription = $"Etherprof — Ping {targetName}: OK ({ms:0} ms)";
        }
        else if (result.Status == TestStatus.Failure)
        {
            TaskbarProgressState = TaskbarItemProgressState.Error;
            TaskbarProgressValue = 1.0;
            TaskbarOverlay = FailureOverlay;
            string err = !string.IsNullOrEmpty(result.Error) ? result.Error : "Timeout";
            TaskbarDescription = $"Etherprof — Ping {targetName}: FAIL ({err})";
        }
    }

    public void ResetTaskbarState()
    {
        TaskbarProgressState = TaskbarItemProgressState.None;
        TaskbarProgressValue = 0.0;
        TaskbarOverlay = null;
        TaskbarDescription = "Etherprof";
    }

    public ObservableCollection<ActivityLogEntryViewModel> ActivityLog { get; } = new();
    public ICommand ClearActivityLogCommand { get; }

    public void AddActivityLog(string category, string message)
    {
        _dispatcher.BeginInvoke(() =>
        {
            ActivityLog.Insert(0, new ActivityLogEntryViewModel(category, message));
            while (ActivityLog.Count > 200)
            {
                ActivityLog.RemoveAt(ActivityLog.Count - 1);
            }
        });
    }

    private void OnWifiEventLogged(object? sender, WifiEventViewModel e)
    {
        AddActivityLog("WLAN", $"{e.TypeText}: {e.DetailsText}");
    }

    private void OnSpeedTestCompleted(object? sender, SpeedTestResultViewModel e)
    {
        string ctx = !string.IsNullOrEmpty(e.ContextText) ? $" ({e.ContextText})" : "";
        if (e.DirectionSymbol == "↓")
        {
            AddActivityLog("DOWNLOAD", $"↓ Download {e.SizeText}: {e.MbpsText}{ctx}");
        }
        else
        {
            AddActivityLog("UPLOAD", $"↑ Upload {e.SizeText}: {e.MbpsText}{ctx}");
        }
    }

    private void OnStreamStateChanged(object? sender, StreamTestStateChangedEventArgs e)
    {
        string msg = !string.IsNullOrEmpty(e.Message) ? $"{e.NewState}: {e.Message}" : $"{e.NewState}";
        AddActivityLog("STREAM", msg);
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("MainViewModel initializing");

        // Initialize Wi-Fi Panel cache
        await WifiPanel.InitializeAsync();

        // Load profiles into buttons
        RefreshProfileButtons();
        RefreshTestSetButtons();

        // Restore selected adapter or pick active connected adapter
        if (!string.IsNullOrEmpty(_settings.SelectedAdapterId))
        {
            HasSelectedAdapter = true;
            await RefreshAdapterStateAsync();
            await WifiPanel.UpdateSelectedAdapterAsync(_settings.SelectedAdapterId);
            SpeedTestPanel.SetActiveAdapterId(_settings.SelectedAdapterId);
        }
        else
        {
            try
            {
                var defaultNic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                                && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                    .FirstOrDefault();

                if (defaultNic != null)
                {
                    await SelectAdapterAsync(defaultNic.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-select default active adapter");
            }
        }
    }

    // --- Adapter ---

    private void OnAdapterChanged(object? sender, NetworkAdapterChangedEventArgs e)
    {
        if (!HasSelectedAdapter) return;
        _dispatcher.BeginInvoke(async () => await RefreshAdapterStateAsync());
    }

    public async Task RefreshAdapterStateAsync()
    {
        if (_isRefreshing || string.IsNullOrEmpty(_settings.SelectedAdapterId)) return;
        _isRefreshing = true;

        try
        {
            _adapterState = await _adapterProvider.GetStateAsync(_settings.SelectedAdapterId);
            UpdateAdapterDisplay();
            UpdateActiveProfile();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh adapter state");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void UpdateAdapterDisplay()
    {
        if (_adapterState is null || !_adapterState.IsAvailable)
        {
            AdapterAvailable = false;
            if (_adapterState is null)
            {
                AdapterName = "No adapter selected";
                LinkStatus = "";
            }
            else
            {
                // Adapter was selected but is now unavailable
                AdapterName = _settings.SelectedAdapterId ?? "Unknown";
                LinkStatus = "Unavailable";
            }
            LinkSpeed = "";
            CurrentIp = "";
            DhcpStatus = "";
            OnPropertyChanged(nameof(CanLearnDhcp));
            OnPropertyChanged(nameof(CanMimicDhcp));
            OnPropertyChanged(nameof(IsStaticIpSet));
            OnPropertyChanged(nameof(IsMediaConnected));
            OnPropertyChanged(nameof(MediaLinkText));
            OnPropertyChanged(nameof(MediaLinkBrush));
            OnPropertyChanged(nameof(AdapterCardBackgroundBrush));
            OnPropertyChanged(nameof(AdapterCardBorderBrush));
            OnPropertyChanged(nameof(AdapterCardForegroundBrush));
            return;
        }

        AdapterAvailable = true;

        // Immediately resolve friendly name from NetworkInterface if available
        var localNic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Id == _adapterState.AdapterId);
        if (localNic != null && !string.IsNullOrEmpty(localNic.Name))
        {
            AdapterName = localNic.Name;
        }

        // Try to get a friendly name from adapters list
        _ = Task.Run(async () =>
        {
            var adapters = await _adapterProvider.GetAdaptersAsync();
            var adapter = adapters.FirstOrDefault(a => a.Id == _adapterState.AdapterId);
            if (adapter is not null)
            {
                _ = _dispatcher.BeginInvoke(() => AdapterName = adapter.Name);
            }
        });

        bool connected = _adapterState.IsConnected;
        LinkStatus = connected ? "●" : "○";
        LinkSpeed = connected ? (_adapterState.LinkSpeed ?? "Connected") : "Media Disconnected";

        if (_adapterState.IPv4Address is not null)
        {
            if (_adapterState.AdditionalIPv4Addresses.Count > 0)
                CurrentIp = $"{_adapterState.IPv4Address}/{_adapterState.PrefixLength ?? 0} (+{string.Join(", ", _adapterState.AdditionalIPv4Addresses)})";
            else
                CurrentIp = $"{_adapterState.IPv4Address}/{_adapterState.PrefixLength ?? 0}";
        }
        else
            CurrentIp = "No IPv4";

        DhcpStatus = _adapterState.IsDhcpEnabled ? "DHCP" : "Static";
        OnPropertyChanged(nameof(CanLearnDhcp));
        OnPropertyChanged(nameof(CanMimicDhcp));
        OnPropertyChanged(nameof(IsStaticIpSet));
        OnPropertyChanged(nameof(IsMediaConnected));
        OnPropertyChanged(nameof(MediaLinkText));
        OnPropertyChanged(nameof(MediaLinkBrush));
        OnPropertyChanged(nameof(AdapterCardBackgroundBrush));
        OnPropertyChanged(nameof(AdapterCardBorderBrush));
        OnPropertyChanged(nameof(AdapterCardForegroundBrush));

        if (_lastLoggedConnectedState != null && _lastLoggedConnectedState != connected)
        {
            string stateStr = connected ? "LINK UP (Media connected)" : "LINK DOWN (Media disconnected)";
            AddActivityLog("ADAPTER", $"Media link changed on '{AdapterName}': {stateStr}");
        }
        _lastLoggedConnectedState = connected;

        StreamPanel.SetAdapterContext(_adapterState?.IPv4Address);
    }

    private void UpdateActiveProfile()
    {
        if (_adapterState is null) return;

        foreach (var btn in ProfileButtons)
        {
            if (btn.State == ProfileState.Applying) continue; // don't overwrite applying state

            btn.State = ProfileMatcher.IsMatch(_adapterState, btn.Profile)
                ? ProfileState.Active
                : ProfileState.Idle;
        }
    }

    public async Task SelectAdapterAsync(string adapterId)
    {
        _settings.SelectedAdapterId = adapterId;
        HasSelectedAdapter = true;
        await _settingsRepo.SaveAsync(_settings);
        await RefreshAdapterStateAsync();
        await WifiPanel.UpdateSelectedAdapterAsync(adapterId);
        SpeedTestPanel.SetActiveAdapterId(adapterId);
    }

    public async Task SwitchToNextAdapterAsync()
    {
        try
        {
            var adapters = await _adapterProvider.GetAdaptersAsync();
            if (adapters.Count == 0) return;

            // Determine qualifying list:
            // 1) Explicit user preferences if available
            // 2) Real physical interfaces (Ethernet & Wi-Fi without virtual / hyper-v / vpn / bridge drivers)
            List<NetworkAdapter> qualifying;
            var visiblePrefs = _settings.AdapterPreferences.Where(p => p.IsVisible).Select(p => p.AdapterId).ToHashSet();
            if (visiblePrefs.Count >= 2)
            {
                qualifying = adapters.Where(a => visiblePrefs.Contains(a.Id)).ToList();
            }
            else
            {
                qualifying = adapters.Where(a =>
                {
                    string type = a.InterfaceType ?? "";
                    string desc = (a.Description ?? "").ToLowerInvariant();
                    string name = (a.Name ?? "").ToLowerInvariant();

                    bool isPhysicalType = type.Equals("Ethernet", StringComparison.OrdinalIgnoreCase) ||
                                          type.Equals("Wireless80211", StringComparison.OrdinalIgnoreCase);

                    bool isVirtual = desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v") ||
                                     desc.Contains("vethernet") || desc.Contains("tap") || desc.Contains("vpn") ||
                                     desc.Contains("bluetooth") || desc.Contains("pcap") || desc.Contains("wfp") ||
                                     desc.Contains("loopback") || name.Contains("vethernet");

                    return isPhysicalType && !isVirtual;
                }).ToList();

                if (qualifying.Count < 2)
                {
                    qualifying = adapters.Where(a => !a.InterfaceType.Equals("Loopback", StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            if (qualifying.Count == 0) return;

            int currentIndex = qualifying.FindIndex(a => a.Id == _settings.SelectedAdapterId);
            int nextIndex = (currentIndex + 1) % qualifying.Count;
            var nextAdapter = qualifying[nextIndex];

            await SelectAdapterAsync(nextAdapter.Id);
            AddActivityLog("ADAPTER", $"Switched active adapter to '{nextAdapter.Name}' ({nextIndex + 1}/{qualifying.Count})");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to switch adapter");
            AddActivityLog("ADAPTER", $"Failed to switch adapter: {ex.Message}");
        }
    }

    // --- Profiles ---

    public void RefreshProfileButtons()
    {
        ProfileButtons.Clear();
        foreach (var p in _profileManager.Profiles)
            ProfileButtons.Add(new ProfileButtonViewModel(p));

        if (_adapterState is not null)
            UpdateActiveProfile();
    }

    private async Task ApplyProfileAsync(ProfileButtonViewModel? vm)
    {
        if (vm is null || string.IsNullOrEmpty(_settings.SelectedAdapterId)) return;

        vm.State = ProfileState.Applying;
        _logger.LogInformation("Applying profile {Name}", vm.Profile.Name);

        try
        {
            var result = await _configService.ApplyProfileAsync(
                _settings.SelectedAdapterId, vm.Profile);

            await RefreshAdapterStateAsync();

            if (result.Success && result.VerificationPassed)
            {
                vm.State = ProfileState.Active;
            }
            else
            {
                vm.State = ProfileState.Failed;
                _logger.LogWarning("Profile apply failed: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            vm.State = ProfileState.Failed;
            _logger.LogError(ex, "Profile apply exception");
        }
    }

    public async Task AddOrUpdateProfileAsync(NetworkProfile profile, bool isNew)
    {
        if (isNew)
            await _profileManager.AddAsync(profile);
        else
            await _profileManager.UpdateAsync(profile);

        RefreshProfileButtons();
    }

    private async Task DuplicateProfileAsync(ProfileButtonViewModel? vm)
    {
        if (vm is null) return;
        await _profileManager.DuplicateAsync(vm.Profile.Id);
        RefreshProfileButtons();
    }

    private async Task DeleteProfileAsync(ProfileButtonViewModel? vm)
    {
        if (vm is null) return;
        await _profileManager.DeleteAsync(vm.Profile.Id);
        RefreshProfileButtons();
    }

    // --- Quick IP ---

    private async Task ApplyQuickIpAsync()
    {
        if (string.IsNullOrEmpty(_settings.SelectedAdapterId))
        {
            QuickIpError = "No adapter selected";
            return;
        }

        var validation = IPv4Validator.ParseCidr(QuickIpText);
        if (!validation.IsValid)
        {
            QuickIpError = $"✕ {validation.ErrorMessage}";
            return;
        }

        IsApplyingQuickIp = true;
        QuickIpError = "";

        try
        {
            var config = new IPv4Configuration
            {
                Address = validation.Address!,
                PrefixLength = validation.PrefixLength!.Value,
                Gateway = null // never modify gateway
            };

            var result = await _configService.ApplyTemporaryAddressAsync(
                _settings.SelectedAdapterId, config);

            if (result.Success && result.VerificationPassed)
            {
                QuickIpText = ""; // clear on success for reuse
                await RefreshAdapterStateAsync();
            }
            else
            {
                QuickIpError = $"✕ {result.ErrorMessage ?? "Verification failed"}";
            }
        }
        catch (Exception ex)
        {
            QuickIpError = $"✕ {ex.Message}";
            _logger.LogError(ex, "Quick IP apply failed");
        }
        finally
        {
            IsApplyingQuickIp = false;
        }
    }

    private async Task LearnDhcpProfileAsync()
    {
        if (string.IsNullOrEmpty(_settings.SelectedAdapterId))
        {
            AddActivityLog("IP", "No network adapter selected to learn DHCP settings from.");
            return;
        }

        var state = _adapterState;
        if (state is null || string.IsNullOrEmpty(state.IPv4Address))
        {
            state = await _adapterProvider.GetStateAsync(_settings.SelectedAdapterId);
        }

        string? ip = state?.IPv4Address;
        byte prefix = state?.PrefixLength ?? 24;
        string? gw = state?.Gateway;
        var dns = state?.DnsServers?.ToList() ?? new List<string>();

        // Synchronous fallback via NetworkInterface if state did not return an IP
        if (string.IsNullOrEmpty(ip))
        {
            try
            {
                var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.Id == _settings.SelectedAdapterId);
                if (nic != null)
                {
                    var ipProps = nic.GetIPProperties();
                    var unicast = ipProps.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (unicast != null)
                    {
                        ip = unicast.Address.ToString();
                        if (unicast.IPv4Mask != null)
                        {
                            var maskBytes = unicast.IPv4Mask.GetAddressBytes();
                            prefix = (byte)Convert.ToString(BitConverter.ToInt32(maskBytes.Reverse().ToArray(), 0), 2).Count(c => c == '1');
                        }
                        var gwAddr = ipProps.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                        if (gwAddr != null) gw = gwAddr.Address.ToString();
                        var dnsAddrs = ipProps.DnsAddresses.Where(d => d.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(d => d.ToString());
                        dns.AddRange(dnsAddrs);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to inspect NetworkInterface fallback for DHCP learning");
            }
        }

        if (string.IsNullOrEmpty(ip))
        {
            AddActivityLog("IP", "No active IPv4 address found on adapter to save into profile.");
            return;
        }

        string baseName = $"DHCP {ip}";
        string profileName = baseName;
        int counter = 2;
        while (_profileManager.Profiles.Any(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)))
        {
            profileName = $"{baseName} ({counter++})";
        }

        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Name = profileName,
            Type = NetworkProfileType.Static,
            IPv4 = new IPv4Configuration
            {
                Address = ip,
                PrefixLength = prefix,
                Gateway = gw
            },
            ApplyDns = dns.Count > 0,
            Dns = dns.Count > 0
                ? new DnsConfiguration { Mode = DnsMode.Static, Servers = dns }
                : new DnsConfiguration { Mode = DnsMode.Automatic }
        };

        try
        {
            await _profileManager.AddAsync(profile);
            RefreshProfileButtons();
            AddActivityLog("IP", $"Learned DHCP lease into static profile '{profile.Name}': {ip}/{prefix}, GW: {gw ?? "(none)"}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save learned DHCP profile");
            AddActivityLog("IP", $"Failed to save profile: {ex.Message}");
        }
    }

    public void OpenActiveAdapterStatusWindow()
    {
        if (string.IsNullOrEmpty(_settings.SelectedAdapterId))
        {
            AddActivityLog("ADAPTER", "Cannot open status: No adapter selected.");
            return;
        }

        string friendlyName = AdapterName;
        if (string.IsNullOrEmpty(friendlyName) || friendlyName == "No adapter selected" || friendlyName == "Unknown" || friendlyName.StartsWith("{"))
        {
            var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Id == _settings.SelectedAdapterId);
            if (nic != null)
            {
                friendlyName = nic.Name;
                AdapterName = nic.Name;
            }
        }

        AddActivityLog("ADAPTER", $"Opening Windows status for adapter '{friendlyName}'...");

        bool opened = false;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                dynamic? folder = shell?.Namespace("shell:ConnectionsFolder");
                if (folder != null)
                {
                    foreach (dynamic item in folder.Items())
                    {
                        string itemName = item.Name;
                        if (string.Equals(itemName, friendlyName, StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (dynamic verb in item.Verbs())
                            {
                                string verbName = ((string)verb.Name).Replace("&", "");
                                if (verbName.Equals("Status", StringComparison.OrdinalIgnoreCase) ||
                                    verbName.Contains("Status", StringComparison.OrdinalIgnoreCase) ||
                                    verbName.Contains("Stan", StringComparison.OrdinalIgnoreCase))
                                {
                                    verb.DoIt();
                                    opened = true;
                                    break;
                                }
                            }

                            if (!opened)
                            {
                                item.InvokeVerb("status");
                                opened = true;
                            }
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invoke Shell status verb directly");
        }

        if (!opened)
        {
            try
            {
                Process.Start(new ProcessStartInfo("control.exe", "netconnections") { UseShellExecute = true });
                AddActivityLog("ADAPTER", "Opened Network Connections control panel.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open control netconnections");
                try
                {
                    Process.Start(new ProcessStartInfo("ncpa.cpl") { UseShellExecute = true });
                }
                catch { }
            }
        }
    }

    private async Task AddAdditionalIpAsync()
    {
        if (string.IsNullOrEmpty(_settings.SelectedAdapterId))
        {
            QuickIpError = "No adapter selected";
            return;
        }

        var validation = IPv4Validator.ParseCidr(QuickIpText);
        if (!validation.IsValid)
        {
            QuickIpError = $"✕ {validation.ErrorMessage}";
            return;
        }

        IsApplyingQuickIp = true;
        QuickIpError = "";

        try
        {
            var config = new IPv4Configuration
            {
                Address = validation.Address!,
                PrefixLength = validation.PrefixLength!.Value
            };

            var result = await _configService.AddAdditionalAddressAsync(_settings.SelectedAdapterId, config);

            if (result.Success && result.VerificationPassed)
            {
                AddActivityLog("IP", $"Added secondary static IP {config.Address}/{config.PrefixLength} to adapter");
                QuickIpText = "";
                await RefreshAdapterStateAsync();
            }
            else
            {
                QuickIpError = $"✕ {result.ErrorMessage ?? "Verification failed"}";
            }
        }
        catch (Exception ex)
        {
            QuickIpError = $"✕ {ex.Message}";
            _logger.LogError(ex, "Add additional IP failed");
        }
        finally
        {
            IsApplyingQuickIp = false;
        }
    }

    // --- Tests ---

    public void RefreshTestSetButtons()
    {
        TestSetButtons.Clear();
        foreach (var ts in _testSetManager.TestSets)
            TestSetButtons.Add(new TestSetButtonViewModel(ts));
    }

    private async Task ToggleTestAsync(TestSetButtonViewModel? vm)
    {
        if (vm is null) return;

        if (_testRunner.IsRunning && _testRunner.ActiveTestSetId == vm.TestSet.Id)
        {
            // Same set is active -> stop it
            _testRunner.Stop();
            vm.IsActive = false;
            TestResultsVisible = false;
            ActiveTestSetName = "";
            TestResults.Clear();
            ResetTaskbarState();
            OnPropertyChanged(nameof(IsPingRunning));
            OnPropertyChanged(nameof(PrimaryPingResult));
            return;
        }

        // Stop any current test (different set or first start)
        if (_testRunner.IsRunning)
        {
            _testRunner.Stop();
            ResetTaskbarState();
            foreach (var btn in TestSetButtons)
                btn.IsActive = false;
        }

        // Start new test
        vm.IsActive = true;
        ActiveTestSetName = vm.TestSet.Name;

        // Initialize result VMs
        TestResults.Clear();
        foreach (var target in vm.TestSet.Targets)
        {
            TestResults.Add(new TestResultViewModel
            {
                TargetId = target.Id,
                TargetName = target.Name,
                Status = TestStatus.Testing
            });
        }
        TestResultsVisible = true;
        OnPropertyChanged(nameof(IsPingRunning));
        OnPropertyChanged(nameof(PrimaryPingResult));

        _activeRunningSet = vm.TestSet;
        try
        {
            await _testRunner.StartAsync(vm.TestSet);
            OnPropertyChanged(nameof(IsPingRunning));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start test set");
            _activeRunningSet = null;
            vm.IsActive = false;
            TestResultsVisible = false;
            ResetTaskbarState();
            OnPropertyChanged(nameof(IsPingRunning));
            OnPropertyChanged(nameof(PrimaryPingResult));
        }
    }

    private void OnTestResultUpdated(object? sender, TestResultUpdatedEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var resultVm = TestResults.FirstOrDefault(r => r.TargetId == e.TargetId);
            if (resultVm is null) return;

            resultVm.Status = e.Result.Status;
            resultVm.LatencyText = e.Result.Status == TestStatus.Success && e.Result.Latency.HasValue
                ? $"{e.Result.Latency.Value.TotalMilliseconds:0} ms"
                : e.Result.Error ?? "";
            resultVm.AddHistoryTick(e.Result);

            OnPropertyChanged(nameof(PrimaryPingResult));
            UpdateTaskbarState(e.Result, resultVm.TargetName);
        });
    }

    public async Task AddOrUpdateTestSetAsync(TestSet testSet, bool isNew)
    {
        if (isNew)
            await _testSetManager.AddAsync(testSet);
        else
            await _testSetManager.UpdateAsync(testSet);

        RefreshTestSetButtons();
    }

    private async Task DuplicateTestSetAsync(TestSetButtonViewModel? vm)
    {
        if (vm is null) return;
        await _testSetManager.DuplicateAsync(vm.TestSet.Id);
        RefreshTestSetButtons();
    }

    private async Task DeleteTestSetAsync(TestSetButtonViewModel? vm)
    {
        if (vm is null) return;

        // Stop if this test set is running
        if (_testRunner.ActiveTestSetId == vm.TestSet.Id)
        {
            _testRunner.Stop();
            TestResultsVisible = false;
            ResetTaskbarState();
            OnPropertyChanged(nameof(IsPingRunning));
            OnPropertyChanged(nameof(PrimaryPingResult));
        }

        await _testSetManager.DeleteAsync(vm.TestSet.Id);
        RefreshTestSetButtons();
    }

    public string GetDefaultAdHocIp()
    {
        string? ip = _adapterState?.IPv4Address;
        if (string.IsNullOrEmpty(ip))
        {
            try
            {
                var nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                                && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count)
                    .FirstOrDefault();
                var unicast = nic?.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                ip = unicast?.Address.ToString();
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(ip))
        {
            int lastDot = ip.LastIndexOf('.');
            if (lastDot > 0)
            {
                string subnet = ip.Substring(0, lastDot + 1);
                return $"{subnet}1";
            }
        }

        return "192.168.1.1";
    }

    public async Task StartAdHocPingAsync(string targetIp, string? targetLabel, bool addToActiveSet)
    {
        string label = string.IsNullOrWhiteSpace(targetLabel) ? targetIp : targetLabel.Trim();
        var target = new TestTarget
        {
            Id = Guid.NewGuid(),
            Name = label,
            Host = targetIp
        };

        // If Complement is enabled and a test is currently running, append to the active test set
        if ((ComplementAdHocPing || addToActiveSet) && _testRunner.IsRunning)
        {
            if (!TestResults.Any(r => r.TargetName.Equals(label, StringComparison.OrdinalIgnoreCase)))
            {
                var newResult = new TestResultViewModel
                {
                    TargetId = target.Id,
                    TargetName = target.Name,
                    Status = TestStatus.Testing
                };
                TestResults.Add(newResult);
                _activeRunningSet?.Targets.Add(target);

                if (_testRunner.ActiveTestSetId.HasValue)
                {
                    var activeSet = _testSetManager.TestSets.FirstOrDefault(t => t.Id == _testRunner.ActiveTestSetId.Value);
                    if (activeSet != null && !activeSet.Targets.Any(t => t.Id == target.Id))
                    {
                        activeSet.Targets.Add(target);
                    }
                }

                AddActivityLog("PING", $"Complemented ping set with: {label} ({targetIp})");
            }
            return;
        }

        // Ephemeral ad-hoc ping: stop any running test and start a transient single-target test
        if (_testRunner.IsRunning)
        {
            _testRunner.Stop();
            ResetTaskbarState();
            foreach (var btn in TestSetButtons)
                btn.IsActive = false;
        }

        var adhocSet = new TestSet
        {
            Id = Guid.NewGuid(),
            Name = $"Ad-hoc: {label}",
            Targets = new List<TestTarget> { target }
        };

        ActiveTestSetName = adhocSet.Name;
        TestResults.Clear();
        TestResults.Add(new TestResultViewModel
        {
            TargetId = target.Id,
            TargetName = target.Name,
            Status = TestStatus.Testing
        });
        TestResultsVisible = true;
        OnPropertyChanged(nameof(IsPingRunning));
        OnPropertyChanged(nameof(PrimaryPingResult));

        _activeRunningSet = adhocSet;
        try
        {
            await _testRunner.StartAsync(adhocSet);
            OnPropertyChanged(nameof(IsPingRunning));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ad-hoc ping");
            _activeRunningSet = null;
            TestResultsVisible = false;
            ResetTaskbarState();
            OnPropertyChanged(nameof(IsPingRunning));
            OnPropertyChanged(nameof(PrimaryPingResult));
        }
    }

    public string GetDefaultSubnetPrefix()
    {
        if (_adapterState?.IPv4Address != null && _adapterState.PrefixLength.HasValue)
        {
            return SubnetCalculator.GetNetworkCidr(_adapterState.IPv4Address, _adapterState.PrefixLength.Value);
        }

        string defaultIp = GetDefaultAdHocIp();
        int lastDot = defaultIp.LastIndexOf('.');
        if (lastDot > 0)
        {
            return $"{defaultIp.Substring(0, lastDot + 1)}0/24";
        }

        return "192.168.1.0/24";
    }

    public async Task StartPingPrefixHostsAsync(List<string> hosts)
    {
        if (hosts == null || hosts.Count == 0) return;

        if (_testRunner.IsRunning)
        {
            _testRunner.Stop();
            ResetTaskbarState();
            foreach (var btn in TestSetButtons)
                btn.IsActive = false;
        }

        var targets = hosts.Select(h => new TestTarget
        {
            Id = Guid.NewGuid(),
            Name = h,
            Host = h
        }).ToList();

        var prefixSet = new TestSet
        {
            Id = Guid.NewGuid(),
            Name = $"Subnet: {hosts.Count} hosts",
            Targets = targets
        };

        ActiveTestSetName = prefixSet.Name;
        TestResults.Clear();
        foreach (var t in targets)
        {
            TestResults.Add(new TestResultViewModel
            {
                TargetId = t.Id,
                TargetName = t.Name,
                Status = TestStatus.Testing
            });
        }
        TestResultsVisible = true;
        OnPropertyChanged(nameof(IsPingRunning));
        OnPropertyChanged(nameof(PrimaryPingResult));

        _activeRunningSet = prefixSet;
        try
        {
            await _testRunner.StartAsync(prefixSet);
            OnPropertyChanged(nameof(IsPingRunning));
            AddActivityLog("PING", $"Started ping sweep test on {hosts.Count} discovered hosts");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start prefix ping test");
            _activeRunningSet = null;
            TestResultsVisible = false;
            ResetTaskbarState();
            OnPropertyChanged(nameof(IsPingRunning));
            OnPropertyChanged(nameof(PrimaryPingResult));
        }
    }

    // --- Capture Current ---

    public async Task<NetworkProfile?> CaptureCurrentProfileAsync()
    {
        if (string.IsNullOrEmpty(_settings.SelectedAdapterId)) return null;
        try
        {
            return await _configService.CaptureCurrentAsync(_settings.SelectedAdapterId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture current configuration");
            return null;
        }
    }

    // --- INotifyPropertyChanged ---

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
