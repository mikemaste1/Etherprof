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

    // Panel Expander States (Closed by default)
    public bool QuickIpExpanded { get; set; } = false;
    public bool WifiExpanded { get; set; } = false;
    public bool SpeedTestExpanded { get; set; } = false;
    public bool StreamClientExpanded { get; set; } = false;
    public bool StreamServerExpanded { get; set; } = false;
    public bool ActivityLogExpanded { get; set; } = false;
    public bool TestResultsExpanded { get; set; } = false;
    public bool WifiProfilesExpanded { get; set; } = false;
    public bool QrScanExpanded { get; set; } = false;

    // Ping Settings
    public bool IsQuickPing { get; set; } = false;
    public bool ComplementAdHocPing { get; set; } = false;

    // Mini HUD & Minimized Ping Monitoring
    public bool IsMiniHudVisible { get; set; } = false;
    public bool AutoShowHudOnMinimize { get; set; } = true;
    public double? MiniHudLeft { get; set; }
    public double? MiniHudTop { get; set; }

    // Live QR Scanner Bar
    public double? QrLiveBarLeft { get; set; }
    public double? QrLiveBarTop { get; set; }
    public bool QrLiveBarTopmost { get; set; } = true;

    // Section Visibility & Order (dynamic hide/show, persisted in activation log order)
    public bool QuickIpVisible { get; set; } = true;
    public bool WifiVisible { get; set; } = true;
    public bool SpeedTestVisible { get; set; } = true;
    public bool StreamClientVisible { get; set; } = true;
    public bool StreamServerVisible { get; set; } = true;
    public bool ActivityLogVisible { get; set; } = true;
    public bool TestResultsVisible { get; set; } = true;
    public bool WifiProfilesVisible { get; set; } = true;
    public bool QrScanVisible { get; set; } = true;

    // Starred Sections (shown in top section toolbar)
    public bool QuickIpStarred { get; set; } = true;
    public bool WifiStarred { get; set; } = true;
    public bool WifiProfilesStarred { get; set; } = true;
    public bool SpeedTestStarred { get; set; } = true;
    public bool StreamClientStarred { get; set; } = false;
    public bool StreamServerStarred { get; set; } = false;
    public bool ActivityLogStarred { get; set; } = true;
    public bool TestResultsStarred { get; set; } = true;
    public bool QrScanStarred { get; set; } = false;

    public List<string> SectionOrder { get; set; } = new()
    {
        "QuickIp", "Wifi", "WifiProfiles", "SpeedTest", "StreamClient", "StreamServer", "ActivityLog", "TestResults", "QrScan"
    };

    // Internet Check (Google DNS 8.8.8.8 -> ONLINE / OFFLINE window title)
    public bool InternetCheckEnabled { get; set; } = true;
}
