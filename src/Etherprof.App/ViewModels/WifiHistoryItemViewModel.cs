namespace Etherprof.App.ViewModels;

using System;
using System.Windows.Input;
using System.Windows.Media;

public sealed class WifiHistoryItemViewModel : INotifyPropertyChangedHelper
{
    public string Ssid { get; }

    private DateTimeOffset _lastConnected;
    public DateTimeOffset LastConnected
    {
        get => _lastConnected;
        set
        {
            if (_lastConnected != value)
            {
                _lastConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastConnectedText));
            }
        }
    }

    public string LastConnectedText => LastConnected.ToString("yyyy-MM-dd HH:mm");

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent != value)
            {
                _isCurrent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(BadgeBackgroundBrush));
            }
        }
    }

    public string StatusBadgeText => IsCurrent ? "● ACTIVE" : "SAVED";

    public Brush BadgeBackgroundBrush => IsCurrent
        ? new SolidColorBrush(Color.FromRgb(22, 163, 74))    // #16A34A (green)
        : new SolidColorBrush(Color.FromRgb(100, 116, 139));  // #64748B (slate)

    public ICommand CopySsidCommand { get; }
    public ICommand SelectSsidCommand { get; }

    public WifiHistoryItemViewModel(
        string ssid,
        DateTimeOffset lastConnected,
        bool isCurrent,
        Action<string> onSelect,
        Action<string, string>? onLog)
    {
        Ssid = ssid;
        _lastConnected = lastConnected;
        _isCurrent = isCurrent;

        CopySsidCommand = new RelayCommand(_ =>
        {
            try
            {
                System.Windows.Clipboard.SetText(Ssid);
                onLog?.Invoke("WIFI", $"Copied SSID to clipboard: {Ssid}");
            }
            catch { }
        });

        SelectSsidCommand = new RelayCommand(_ => onSelect(Ssid));
    }
}
