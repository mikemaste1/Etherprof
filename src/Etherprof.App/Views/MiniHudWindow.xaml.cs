using System.Windows;
using System.Windows.Input;
using Etherprof.App.ViewModels;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;

namespace Etherprof.App.Views;

public partial class MiniHudWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppSettings _settings;
    private readonly ISettingsRepository _settingsRepo;
    private bool _isSavingPosition;

    public MiniHudWindow(MainViewModel vm, AppSettings settings, ISettingsRepository settingsRepo)
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
        double screenWidth = SystemParameters.VirtualScreenWidth;
        double screenHeight = SystemParameters.VirtualScreenHeight;
        double screenLeft = SystemParameters.VirtualScreenLeft;
        double screenTop = SystemParameters.VirtualScreenTop;

        if (_settings.MiniHudLeft.HasValue && _settings.MiniHudTop.HasValue)
        {
            Left = Math.Clamp(_settings.MiniHudLeft.Value, screenLeft, screenLeft + screenWidth - 240);
            Top = Math.Clamp(_settings.MiniHudTop.Value, screenTop, screenTop + screenHeight - 60);
        }
        else
        {
            // Default: Top right corner of primary work area
            Left = SystemParameters.WorkArea.Right - 280;
            Top = SystemParameters.WorkArea.Top + 35;
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || _isSavingPosition) return;
        _isSavingPosition = true;
        _settings.MiniHudLeft = Left;
        _settings.MiniHudTop = Top;
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

    private void OnWindowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _vm.RestoreMainWindow?.Invoke();
        }
    }

    private void OnRestoreClicked(object sender, RoutedEventArgs e)
    {
        _vm.RestoreMainWindow?.Invoke();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _vm.IsMiniHudVisible = false;
    }
}
