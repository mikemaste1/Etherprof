namespace Etherprof.App.ViewModels;

using Etherprof.Contracts.Models;
using Etherprof.Core;

public sealed class WifiEventViewModel : INotifyPropertyChangedHelper
{
    private readonly WifiEvent _event;
    private readonly Func<string?, string?> _aliasResolver;

    public WifiEvent Event => _event;

    public string TimestampText => _event.Timestamp.ToString("HH:mm:ss.fff");

    public WifiEventType Type => _event.Type;

    public string TypeText => _event.Type switch
    {
        WifiEventType.Connected => "CONNECTED",
        WifiEventType.Disconnected => "DISCONNECTED",
        WifiEventType.Roam => "ROAM",
        WifiEventType.NetworkChanged => "NETWORK CHANGED",
        _ => _event.Type.ToString().ToUpperInvariant()
    };

    public string DetailsText
    {
        get
        {
            var prevBssidName = GetDisplayName(_event.PreviousBssid);
            var currBssidName = GetDisplayName(_event.CurrentBssid);

            return _event.Type switch
            {
                WifiEventType.Roam => $"{prevBssidName} → {currBssidName}",
                WifiEventType.Connected => $"{_event.CurrentSsid} → {currBssidName}",
                WifiEventType.Disconnected => $"{_event.PreviousSsid}{(string.IsNullOrEmpty(prevBssidName) ? "" : $" (Last AP: {prevBssidName})")}",
                WifiEventType.NetworkChanged => $"{_event.PreviousSsid} → {_event.CurrentSsid}",
                _ => ""
            };
        }
    }

    public WifiEventViewModel(WifiEvent ev, Func<string?, string?> aliasResolver)
    {
        _event = ev;
        _aliasResolver = aliasResolver;
    }

    public void NotifyAliasChanged()
    {
        OnPropertyChanged(nameof(DetailsText));
    }

    private string GetDisplayName(string? bssid)
    {
        if (string.IsNullOrEmpty(bssid))
            return "";

        if (BssidNormalizer.TryNormalize(bssid, out var norm))
        {
            var alias = _aliasResolver(norm);
            if (!string.IsNullOrWhiteSpace(alias))
                return alias;
            return norm;
        }

        return bssid;
    }
}

public abstract class INotifyPropertyChangedHelper : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
