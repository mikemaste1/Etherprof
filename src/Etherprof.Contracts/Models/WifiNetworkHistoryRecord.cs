namespace Etherprof.Contracts.Models;

using System;

public sealed class WifiNetworkHistoryRecord
{
    public string Ssid { get; set; } = "";
    public string? KnownKey { get; set; }
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastConnected { get; set; } = DateTimeOffset.UtcNow;
}
