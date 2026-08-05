namespace Etherprof.Contracts.Models;

public sealed class WifiConnectionState
{
    public string AdapterId { get; init; } = "";

    public WifiMonitorAvailability Availability { get; init; }

    public bool IsConnected { get; init; }

    public string? Ssid { get; init; }

    public string? Bssid { get; init; }

    public int? RssiDbm { get; init; }

    public int? SignalQualityPercent { get; init; }

    public int? Channel { get; init; }

    public int? CenterFrequencyMhz { get; init; }

    public WifiBand Band { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string? Error { get; init; }
}
