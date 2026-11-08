using System.Windows;
using System.Windows.Input;
using Etherprof.App.ViewModels;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;

namespace Etherprof.App.Views;

public partial class NetworkLoadHudWindow : Window
{
    private readonly NetworkLoadViewModel _vm;
    private readonly AppSettings _settings;
    private readonly ISettingsRepository _settingsRepo;
    private bool _isSavingPosition;

    public NetworkLoadHudWindow(NetworkLoadViewModel vm, AppSettings settings, ISettingsRepository settingsRepo)
    {
        InitializeComponent();
        _vm = vm;
        _settings = settings;
        _settingsRepo = settingsRepo;
        DataContext = _vm;

        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Restore screen position
        double screenWidth  = SystemParameters.VirtualScreenWidth;
        double screenHeight = SystemParameters.VirtualScreenHeight;
        double screenLeft   = SystemParameters.VirtualScreenLeft;
        double screenTop    = SystemParameters.VirtualScreenTop;

        if (_settings.NetworkLoadHudLeft.HasValue && _settings.NetworkLoadHudTop.HasValue)
        {
            Left = Math.Clamp(_settings.NetworkLoadHudLeft.Value, screenLeft, screenLeft + screenWidth  - 240);
            Top  = Math.Clamp(_settings.NetworkLoadHudTop.Value,  screenTop,  screenTop  + screenHeight - 80);
        }
        else
        {
            // Default: top-right corner, slightly below Mini-HUD area
            Left = SystemParameters.WorkArea.Right - 290;
            Top  = SystemParameters.WorkArea.Top  + 80;
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || _isSavingPosition) return;
        _isSavingPosition = true;
        _settings.NetworkLoadHudLeft = Left;
        _settings.NetworkLoadHudTop  = Top;
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

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _vm.IsVisible = false;
    }
}
