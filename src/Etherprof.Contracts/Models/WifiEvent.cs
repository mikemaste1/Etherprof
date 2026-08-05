namespace Etherprof.Contracts.Models;

public sealed class WifiEvent
{
    public DateTimeOffset Timestamp { get; init; }

    public WifiEventType Type { get; init; }

    public string? PreviousSsid { get; init; }

    public string? CurrentSsid { get; init; }

    public string? PreviousBssid { get; init; }

    public string? CurrentBssid { get; init; }

    public int? PreviousRssiDbm { get; init; }

    public int? CurrentRssiDbm { get; init; }

    public int? PreviousSignalQualityPercent { get; init; }

    public int? CurrentSignalQualityPercent { get; init; }
}
