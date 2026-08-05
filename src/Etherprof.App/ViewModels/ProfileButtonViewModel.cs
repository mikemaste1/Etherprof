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
        ProfileState.Active when Profile.Type == NetworkProfileType.Static && Profile.IPv4 is not null
            => $"{Profile.IPv4.Address}/{Profile.IPv4.PrefixLength}",
        _ => ""
    };

    public ProfileButtonViewModel(NetworkProfile profile)
    {
        Profile = profile;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
