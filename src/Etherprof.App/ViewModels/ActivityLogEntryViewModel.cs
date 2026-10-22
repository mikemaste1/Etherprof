namespace Etherprof.App.ViewModels;

using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

public sealed class ActivityLogEntryViewModel : INotifyPropertyChangedHelper
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;
    public string TimeText => Timestamp.ToString("HH:mm:ss");
    public string Category { get; }
    public string Message { get; }

    public string? DetectedSsid { get; }
    public bool HasDetectedSsid => !string.IsNullOrEmpty(DetectedSsid);

    public ICommand CopySsidCommand { get; }
    public ICommand FindInProfilesCommand { get; }

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

    public ActivityLogEntryViewModel(
        string category,
        string message,
        string? ssid = null,
        Action<string>? onFindInProfiles = null,
        Action<string, string>? onLog = null)
    {
        Category = category;
        Message = message;

        if (!string.IsNullOrWhiteSpace(ssid))
        {
            DetectedSsid = ssid.Trim();
        }
        else
        {
            DetectedSsid = TryExtractSsid(category, message);
        }

        CopySsidCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(DetectedSsid))
            {
                try
                {
                    Clipboard.SetText(DetectedSsid);
                    onLog?.Invoke("WIFI", $"Copied SSID to clipboard: {DetectedSsid}");
                }
                catch { }
            }
        });

        FindInProfilesCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(DetectedSsid))
            {
                onFindInProfiles?.Invoke(DetectedSsid);
            }
        });
    }

    private static string? TryExtractSsid(string category, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (string.Equals(category, "WLAN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "WIFI", StringComparison.OrdinalIgnoreCase))
        {
            // Match 'SSID' or "SSID"
            var m = Regex.Match(text, @"['""]([^'""]+)['""]");
            if (m.Success)
            {
                string s = m.Groups[1].Value.Trim();
                if (s.Length >= 2 && !s.Contains('\n') && !s.Contains('\\'))
                    return s;
            }

            // Match SSID: <name> or network <name> or profile <name>
            m = Regex.Match(text, @"(?i)(?:SSID|network|profile)\s*[:']?\s*([a-zA-Z0-9_\-\. ]{2,32})");
            if (m.Success)
            {
                return m.Groups[1].Value.Trim('\'', '"', ' ', '.');
            }
        }

        return null;
    }
}
