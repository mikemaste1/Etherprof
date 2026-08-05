namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public sealed class NetworkAdapterChangedEventArgs : EventArgs
{
    public string? AdapterId { get; init; }
}

public interface INetworkAdapterProvider : IDisposable
{
    Task<IReadOnlyList<NetworkAdapter>> GetAdaptersAsync();
    Task<NetworkAdapterState> GetStateAsync(string adapterId);
    event EventHandler<NetworkAdapterChangedEventArgs>? AdapterChanged;
    void StartMonitoring();
    void StopMonitoring();
}
