using System.Windows;
using System.Windows.Input;
using Etherprof.App.ViewModels;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;

namespace Etherprof.App.Views;

public partial class LiveQrScannerWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppSettings _settings;
    private readonly ISettingsRepository _settingsRepo;
    private bool _isSavingPosition;

    public LiveQrScannerWindow(MainViewModel vm, AppSettings settings, ISettingsRepository settingsRepo)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        _settingsRepo = settingsRepo;
        DataContext = _vm;

        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
        KeyDown += OnWindowKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        double screenWidth = SystemParameters.VirtualScreenWidth;
        double screenHeight = SystemParameters.VirtualScreenHeight;
        double screenLeft = SystemParameters.VirtualScreenLeft;
        double screenTop = SystemParameters.VirtualScreenTop;

        if (_settings.QrLiveBarLeft.HasValue && _settings.QrLiveBarTop.HasValue)
        {
            Left = Math.Clamp(_settings.QrLiveBarLeft.Value, screenLeft, screenLeft + screenWidth - 380);
            Top = Math.Clamp(_settings.QrLiveBarTop.Value, screenTop, screenTop + screenHeight - 200);
        }
        else
        {
            // Default position: lower right corner above taskbar
            Left = SystemParameters.WorkArea.Right - 400;
            Top = SystemParameters.WorkArea.Bottom - 340;
        }

        Topmost = _settings.QrLiveBarTopmost;
        _vm.QrScanPanel.IsDetachedBarTopmost = Topmost;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || _isSavingPosition) return;
        _isSavingPosition = true;
        _settings.QrLiveBarLeft = Left;
        _settings.QrLiveBarTop = Top;
        _ = _settingsRepo.SaveAsync(_settings);
        _isSavingPosition = false;
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            _vm.QrScanPanel.PasteImageAndDecode();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            OnCloseClicked(sender, e);
            e.Handled = true;
        }
    }

    private void OnToggleTopmostClicked(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _vm.QrScanPanel.IsDetachedBarTopmost = Topmost;
        _settings.QrLiveBarTopmost = Topmost;
        _ = _settingsRepo.SaveAsync(_settings);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _vm.QrScanPanel.IsDetachedBarOpen = false;
        _vm.QrScanPanel.CloseDetachedBarCommand.Execute(null);
        Hide();
    }

    private void OnDismissNotificationClicked(object sender, RoutedEventArgs e)
    {
        _vm.QrScanPanel.LiveDecodeNotification = null;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Hide instead of destroying window instance
        e.Cancel = true;
        _vm.QrScanPanel.IsDetachedBarOpen = false;
        _ = _vm.QrScanPanel.StopLiveScanAsync();
        Hide();
    }
}
