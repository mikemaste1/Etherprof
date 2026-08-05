namespace Etherprof.Contracts.Models;

public enum WifiMonitorAvailability
{
    Available,
    NotWifiAdapter,
    AdapterUnavailable,
    Disconnected,
    PermissionDenied,
    Error
}
