namespace Etherprof.Contracts.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? SelectedAdapterId { get; set; }
    public List<AdapterPreference> AdapterPreferences { get; set; } = new();

    // Stream Test Client Settings (v0.4)
    public string StreamServerHost { get; set; } = "192.168.1.40";
    public int StreamPort { get; set; } = 49100;
    public string StreamProtocol { get; set; } = "Tcp";
    public string StreamDirection { get; set; } = "Send";
    public long StreamTargetBitrate { get; set; } = 2_000_000;

    // Stream Test Server Settings (v0.4)
    public int StreamServerPort { get; set; } = 49100;
}
