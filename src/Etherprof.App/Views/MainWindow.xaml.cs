using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using Etherprof.App.ViewModels;
using Etherprof.Contracts.Models;

namespace Etherprof.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        try
        {
            var iconUri = new Uri("pack://application:,,,/Etherprof;component/app.ico", UriKind.RelativeOrAbsolute);
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }
        catch { }
        Loaded += OnLoaded;
    }

    private MiniHudWindow? _hudWindow;
    private NetworkLoadHudWindow? _netLoadHudWindow;
    private LiveQrScannerWindow? _qrLiveWindow;
    private bool _autoShownByMinimize;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // Wire dialog open actions
            vm.OpenProfileEditor = profile => ShowProfileEditor(vm, profile);
            vm.OpenTestSetEditor = testSet => ShowTestSetEditor(vm, testSet);
            vm.OpenCaptureProfile = () => ShowCaptureProfile(vm);
            vm.OpenAdapterSelector = () => ShowAdapterSelector(vm);
            vm.OpenAliasEditorDialog = (bssid, alias) => ShowAliasEditor(vm, bssid, alias);
            vm.OpenAdHocPingDialog = defaultIp => ShowAdHocPingDialog(vm, defaultIp);
            vm.OpenPingPrefixDialog = proposedPrefix => ShowPingPrefixDialog(vm, proposedPrefix);
            vm.OpenBrowserDialogAction = host => ShowBrowserDialogForTarget(host);
            vm.OpenTracerouteDialogAction = host => ShowTracerouteDialogForTarget(host);

            // Wire HUD actions
            vm.RequestShowMiniHud = () => ShowMiniHud(vm);
            vm.RequestHideMiniHud = () => HideMiniHud();
            vm.RestoreMainWindow = RestoreFromHud;

            // Wire NetworkLoad HUD show/hide via IsVisible changes
            vm.NetworkLoadHud.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Etherprof.App.ViewModels.NetworkLoadViewModel.IsVisible))
                {
                    if (vm.NetworkLoadHud.IsVisible) ShowNetworkLoadHud(vm);
                    else HideNetworkLoadHud();
                }
            };
            // Restore Network Load HUD if it was active
            if (vm.NetworkLoadHud.IsVisible)
            {
                ShowNetworkLoadHud(vm);
            }

            // Wire Detached Live QR Scanner Bar
            vm.QrScanPanel.OpenDetachedBarRequested = () => ShowLiveQrBar(vm);
            vm.QrScanPanel.CloseDetachedBarRequested = () => HideLiveQrBar();

            KeyDown += OnMainWindowKeyDown;

            // Restore HUD if it was active
            if (vm.IsMiniHudVisible)
            {
                ShowMiniHud(vm);
            }

            // Initialize dynamic sections layout
            InitializeDynamicSections(vm);
        }
    }

    private void ShowProfileEditor(MainViewModel vm, NetworkProfile? existingProfile)
    {
        var dialog = new ProfileEditorDialog(existingProfile)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _ = vm.AddOrUpdateProfileAsync(dialog.Result, existingProfile is null);
        }
    }

    private void ShowTestSetEditor(MainViewModel vm, TestSet? existingTestSet)
    {
        var dialog = new TestSetEditorDialog(existingTestSet)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _ = vm.AddOrUpdateTestSetAsync(dialog.Result, existingTestSet is null);
        }
    }

    private void ShowAdHocPingDialog(MainViewModel vm, string defaultIp)
    {
        var dialog = new AdHocPingDialog(defaultIp)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.TargetIp))
        {
            _ = vm.StartAdHocPingAsync(dialog.TargetIp, dialog.TargetLabel, false);
        }
    }

    private void ShowPingPrefixDialog(MainViewModel vm, string proposedPrefix)
    {
        // Non-modal status notebook window: can be minimized to taskbar, numbered, timestamped, operated alongside main window
        var window = new PingPrefixDialog(proposedPrefix)
        {
            OnStartContinuousPing = hosts =>
            {
                if (hosts.Count > 0)
                {
                    _ = vm.StartPingPrefixHostsAsync(hosts);
                }
            }
        };

        window.Show();
    }

    private async void ShowCaptureProfile(MainViewModel vm)
    {
        var captured = await vm.CaptureCurrentProfileAsync();
        if (captured is null) return;

        var dialog = new CaptureProfileDialog(captured)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _ = vm.AddOrUpdateProfileAsync(dialog.Result, true);
        }
    }

    private async void ShowAdapterSelector(MainViewModel vm)
    {
        var dialog = new AdapterSelectorDialog(vm)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private void ShowAliasEditor(MainViewModel vm, string bssid, string? currentAlias)
    {
        var editorVm = new BssidAliasEditorViewModel(bssid, currentAlias);
        var dialog = new BssidAliasEditorDialog(editorVm)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _ = vm.WifiPanel.SetAliasAsync(bssid, editorVm.Alias);
        }
    }

    private void ShowMiniHud(MainViewModel vm)
    {
        if (_hudWindow is null)
        {
            _hudWindow = new MiniHudWindow(vm, vm.Settings, vm.SettingsRepo);
            _hudWindow.Closed += (s, e) => _hudWindow = null;
        }
        _hudWindow.Show();
        _hudWindow.Topmost = true;
    }

    private void HideMiniHud()
    {
        _hudWindow?.Hide();
    }

    private void ShowLiveQrBar(MainViewModel vm)
    {
        if (_qrLiveWindow is null)
        {
            _qrLiveWindow = new LiveQrScannerWindow(vm, vm.Settings, vm.SettingsRepo);
            _qrLiveWindow.Closed += (s, e) => _qrLiveWindow = null;
        }
        _qrLiveWindow.Show();
        _qrLiveWindow.Activate();
        vm.QrScanPanel.IsDetachedBarOpen = true;
    }

    private void HideLiveQrBar()
    {
        _qrLiveWindow?.Hide();
    }

    private void OnMainWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (Keyboard.FocusedElement is not TextBox tb || tb.IsReadOnly)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.QrScanVisible = true;
                    vm.QrScanExpanded = true;
                    vm.QrScanPanel.PasteImageAndDecode();
                    e.Handled = true;
                }
            }
        }
    }
    private void RestoreFromHud()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Focus();

        if (_autoShownByMinimize && DataContext is MainViewModel vm && !vm.IsMiniHudVisible)
        {
            HideMiniHud();
            _autoShownByMinimize = false;
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (DataContext is MainViewModel vm)
        {
            if (WindowState == WindowState.Minimized)
            {
                if (vm.AutoShowHudOnMinimize && vm.IsPingRunning && (_hudWindow == null || !_hudWindow.IsVisible))
                {
                    _autoShownByMinimize = true;
                    ShowMiniHud(vm);
                }
            }
            else if (WindowState == WindowState.Normal)
            {
                if (_autoShownByMinimize && !vm.IsMiniHudVisible)
                {
                    HideMiniHud();
                    _autoShownByMinimize = false;
                }
            }
        }
    }

    private void ShowBrowserDialogForTarget(string host)
    {
        var dialog = new OpenBrowserDialog(host) { Owner = this };
        dialog.Show();
    }

    private void ShowTracerouteDialogForTarget(string host)
    {
        var dialog = new TracerouteDialog(host) { Owner = this };
        dialog.Show();
    }

    private void ShowNetworkLoadHud(MainViewModel vm)
    {
        if (_netLoadHudWindow is null)
        {
            _netLoadHudWindow = new NetworkLoadHudWindow(vm.NetworkLoadHud, vm.Settings, vm.SettingsRepo);
            _netLoadHudWindow.Closed += (s, e) => _netLoadHudWindow = null;
        }
        _netLoadHudWindow.Show();
        _netLoadHudWindow.Topmost = true;
    }

    private void HideNetworkLoadHud()
    {
        _netLoadHudWindow?.Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _hudWindow?.Close();
        _hudWindow = null;
        _netLoadHudWindow?.Close();
        _netLoadHudWindow = null;
    }

    // --- Dynamic Section Ordering & Hide/Show System ---
    private readonly Dictionary<string, FrameworkElement> _sections = new();

    private void InitializeDynamicSections(MainViewModel vm)
    {
        _sections["QuickIp"] = quickIpExpander;
        _sections["Wifi"] = wifiExpander;
        _sections["WifiProfiles"] = wifiProfilesExpander;
        _sections["SpeedTest"] = speedTestExpander;
        _sections["StreamClient"] = streamClientExpander;
        _sections["StreamServer"] = streamServerExpander;
        _sections["ActivityLog"] = activityLogExpander;
        _sections["TestResults"] = testResultsExpander;
        _sections["QrScan"] = qrScanExpander;

        // Clear default static XAML children
        sectionsContainer.Children.Clear();

        // Restore sections in persisted log order
        var order = vm.Settings.SectionOrder;
        foreach (var key in order)
        {
            if (_sections.TryGetValue(key, out var el))
            {
                if (IsSectionVisible(vm, key) && !sectionsContainer.Children.Contains(el))
                {
                    sectionsContainer.Children.Add(el);
                }
            }
        }

        // Add any visible section that was not in order list
        foreach (var kvp in _sections)
        {
            if (IsSectionVisible(vm, kvp.Key) && !sectionsContainer.Children.Contains(kvp.Value))
            {
                sectionsContainer.Children.Add(kvp.Value);
            }
        }

        // Subscribe to dynamic toggle changes
        vm.SectionVisibilityChanged += OnSectionVisibilityChanged;
    }

    private static bool IsSectionVisible(MainViewModel vm, string key) => key switch
    {
        "QuickIp" => vm.QuickIpVisible,
        "Wifi" => vm.WifiVisible,
        "WifiProfiles" => vm.WifiProfilesVisible,
        "SpeedTest" => vm.SpeedTestVisible,
        "StreamClient" => vm.StreamClientVisible,
        "StreamServer" => vm.StreamServerVisible,
        "ActivityLog" => vm.ActivityLogVisible,
        "TestResults" => vm.TestResultsSectionVisible,
        "QrScan" => vm.QrScanVisible,
        _ => true
    };

    private void OnSectionVisibilityChanged(string sectionKey, bool isVisible)
    {
        if (!_sections.TryGetValue(sectionKey, out var el)) return;

        if (isVisible)
        {
            if (!sectionsContainer.Children.Contains(el))
            {
                // "Log style - first toggled closer to top" (newly toggled appends at bottom)
                sectionsContainer.Children.Add(el);
            }
        }
        else
        {
            sectionsContainer.Children.Remove(el);
        }
    }
}
