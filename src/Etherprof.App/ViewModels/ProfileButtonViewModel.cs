using System.ComponentModel;
using System.Runtime.CompilerServices;
using Etherprof.Contracts.Models;

namespace Etherprof.App.ViewModels;

public sealed class ProfileButtonViewModel : INotifyPropertyChanged
{
    public NetworkProfile Profile { get; }

    private ProfileState _state = ProfileState.Idle;
    public ProfileState State
    {
        get => _state;
        set { if (_state != value) { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); OnPropertyChanged(nameof(StatusText)); } }
    }

    public string DisplayText => State switch
    {
        ProfileState.Active => $"● {Profile.Name}",
        ProfileState.Applying => Profile.Name,
        ProfileState.Failed => $"✕ {Profile.Name}",
        _ => Profile.Name
    };

    public string StatusText => State switch
    {
        ProfileState.Applying => "Applying...",
        ProfileState.Failed => "Failed",
        _ => FormatSettingsSummary()
    };

    private string FormatSettingsSummary()
    {
        if (Profile.Type == NetworkProfileType.Dhcp)
        {
            if (Profile.ApplyDns && Profile.Dns?.Servers.Count > 0)
                return $"DHCP · DNS: {string.Join(", ", Profile.Dns.Servers)}";
            return "DHCP";
        }

        if (Profile.Type == NetworkProfileType.Static && Profile.IPv4 is not null)
        {
            var parts = new List<string> { $"{Profile.IPv4.Address}/{Profile.IPv4.PrefixLength}" };
            if (!string.IsNullOrEmpty(Profile.IPv4.Gateway))
                parts.Add($"GW: {Profile.IPv4.Gateway}");
            if (Profile.ApplyDns && Profile.Dns?.Servers.Count > 0)
                parts.Add($"DNS: {string.Join(", ", Profile.Dns.Servers)}");
            return string.Join(" · ", parts);
        }

        if (Profile.Type == NetworkProfileType.DnsOnly && Profile.Dns?.Servers.Count > 0)
        {
            return $"DNS: {string.Join(", ", Profile.Dns.Servers)}";
        }

        return "";
    }

    public string GetClipboardSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Profile: {Profile.Name}");
        sb.AppendLine($"Type: {Profile.Type}");
        if (Profile.Type == NetworkProfileType.Static && Profile.IPv4 != null)
        {
            sb.AppendLine($"IP: {Profile.IPv4.Address}/{Profile.IPv4.PrefixLength}");
            if (!string.IsNullOrEmpty(Profile.IPv4.Gateway))
                sb.AppendLine($"Gateway: {Profile.IPv4.Gateway}");
        }
        if (Profile.ApplyDns && Profile.Dns != null)
        {
            if (Profile.Dns.Mode == DnsMode.Static && Profile.Dns.Servers.Count > 0)
                sb.AppendLine($"DNS: {string.Join(", ", Profile.Dns.Servers)}");
            else
                sb.AppendLine("DNS: Automatic");
        }
        return sb.ToString().TrimEnd();
    }

    public ProfileButtonViewModel(NetworkProfile profile)
    {
        Profile = profile;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
