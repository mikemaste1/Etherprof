namespace Etherprof.Contracts.Models;

public sealed class SpeedTestNetworkContext
{
    public string AdapterId { get; init; } = "";

    public string? AdapterName { get; init; }

    public string? LocalIpAddress { get; init; }

    public bool IsWifi { get; init; }

    public string? Ssid { get; init; }

    public string? StartBssid { get; init; }

    public string? EndBssid { get; init; }

    public int? StartRssiDbm { get; init; }

    public int? EndRssiDbm { get; init; }

    public int? StartSignalQualityPercent { get; init; }

    public int? EndSignalQualityPercent { get; init; }

    public WifiBand StartBand { get; init; }

    public WifiBand EndBand { get; init; }

    public int? StartChannel { get; init; }

    public int? EndChannel { get; init; }

    public bool RoamedDuringTest { get; init; }

    public int RoamCount { get; init; }

    public string? FirstRoamFromBssid { get; init; }

    public string? FirstRoamToBssid { get; init; }
}
