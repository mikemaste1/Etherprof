namespace Etherprof.App.ViewModels;

using System;

public sealed class ActivityLogEntryViewModel
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;
    public string TimeText => Timestamp.ToString("HH:mm:ss");
    public string Category { get; }
    public string Message { get; }

    public string CategoryBrush => Category switch
    {
        "WLAN" => "#2563EB",       // Blue
        "DOWNLOAD" => "#16A34A",   // Bright Green for SpeedTest Download
        "UPLOAD" => "#DC2626",     // Bright Red for SpeedTest Upload
        "SPEED" => "#16A34A",      // Green fallback
        "STREAM" => "#D97706",     // Amber
        "ADAPTER" => "#4B5563",    // Slate
        "IP" => "#0284C7",         // Light Blue
        "PROFILE" => "#0369A1",    // Ocean Blue
        "PING" => "#15803D",       // Green
        "WIFI" => "#7E22CE",       // Purple
        _ => "#475569"
    };

    public ActivityLogEntryViewModel(string category, string message)
    {
        Category = category;
        Message = message;
    }
}
