using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Core;
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

    private NetworkAdapterState? _adapterState;
    private bool _isRefreshing;

    // Dialog open actions - set from code-behind
    public Action<NetworkProfile?>? OpenProfileEditor { get; set; }
    public Action<TestSet?>? OpenTestSetEditor { get; set; }
    public Action? OpenCaptureProfile { get; set; }
    public Action? OpenAdapterSelector { get; set; }
    public Action<string, string?>? OpenAliasEditorDialog { get; set; }

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

        // Subscribe to adapter changes
        _adapterProvider.AdapterChanged += OnAdapterChanged;
        _testRunner.ResultUpdated += OnTestResultUpdated;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("MainViewModel initializing");

        // Initialize Wi-Fi Panel cache
        await WifiPanel.InitializeAsync();

        // Load profiles into buttons
        RefreshProfileButtons();
        RefreshTestSetButtons();

        // Restore selected adapter
        if (!string.IsNullOrEmpty(_settings.SelectedAdapterId))
        {
            HasSelectedAdapter = true;
            await RefreshAdapterStateAsync();
            await WifiPanel.UpdateSelectedAdapterAsync(_settings.SelectedAdapterId);
            SpeedTestPanel.SetActiveAdapterId(_settings.SelectedAdapterId);
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
            return;
        }

        AdapterAvailable = true;

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

        LinkStatus = _adapterState.IsConnected ? "●" : "○";
        LinkSpeed = _adapterState.IsConnected ? (_adapterState.LinkSpeed ?? "") : "Disconnected";

        if (_adapterState.IPv4Address is not null)
            CurrentIp = $"{_adapterState.IPv4Address}/{_adapterState.PrefixLength ?? 0}";
        else
            CurrentIp = "No IPv4";

        DhcpStatus = _adapterState.IsDhcpEnabled ? "DHCP" : "Static";
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
            return;
        }

        // Stop any current test (different set or first start)
        if (_testRunner.IsRunning)
        {
            _testRunner.Stop();
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

        try
        {
            await _testRunner.StartAsync(vm.TestSet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start test set");
            vm.IsActive = false;
            TestResultsVisible = false;
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
        }

        await _testSetManager.DeleteAsync(vm.TestSet.Id);
        RefreshTestSetButtons();
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
