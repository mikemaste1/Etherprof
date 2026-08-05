namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public class WifiStateChangedEventArgs : EventArgs
{
    public WifiConnectionState State { get; }

    public WifiStateChangedEventArgs(WifiConnectionState state)
    {
        State = state;
    }
}

public class WifiRoamEventArgs : EventArgs
{
    public WifiEvent Event { get; }

    public WifiRoamEventArgs(WifiEvent eventData)
    {
        Event = eventData;
    }
}

public class WifiConnectionEventArgs : EventArgs
{
    public WifiEvent Event { get; }

    public WifiConnectionEventArgs(WifiEvent eventData)
    {
        Event = eventData;
    }
}

public interface IWifiMonitor : IDisposable
{
    WifiConnectionState? CurrentState { get; }

    event EventHandler<WifiStateChangedEventArgs>? StateChanged;
    event EventHandler<WifiRoamEventArgs>? Roamed;
    event EventHandler<WifiConnectionEventArgs>? ConnectionEvent;

    Task StartAsync(string adapterId, CancellationToken cancellationToken);

    Task StopAsync();
}
