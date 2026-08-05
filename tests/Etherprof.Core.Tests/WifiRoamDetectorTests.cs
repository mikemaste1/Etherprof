namespace Etherprof.Core.Tests;

using Etherprof.Contracts.Models;
using Etherprof.Core;
using Xunit;

public class WifiRoamDetectorTests
{
    private readonly WifiRoamDetector _detector = new();

    [Fact]
    public void InitialConnection_ProducesConnectedEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:11:03",
            RssiDbm = -55,
            SignalQualityPercent = 88
        };

        var events = _detector.Process(state, now);

        Assert.Single(events);
        Assert.Equal(WifiEventType.Connected, events[0].Type);
        Assert.Equal("OfficeWiFi", events[0].CurrentSsid);
        Assert.Equal("84:16:F9:2A:11:03", events[0].CurrentBssid);
    }

    [Fact]
    public void DirectRoam_SameSsidDifferentBssid_ProducesRoamEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var state1 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:11:03",
            RssiDbm = -60
        };

        var state2 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:26:91",
            RssiDbm = -58
        };

        _detector.Process(state1, now);
        var events = _detector.Process(state2, now.AddSeconds(10));

        Assert.Single(events);
        Assert.Equal(WifiEventType.Roam, events[0].Type);
        Assert.Equal("OfficeWiFi", events[0].PreviousSsid);
        Assert.Equal("OfficeWiFi", events[0].CurrentSsid);
        Assert.Equal("84:16:F9:2A:11:03", events[0].PreviousBssid);
        Assert.Equal("84:16:F9:2A:26:91", events[0].CurrentBssid);
    }

    [Fact]
    public void NetworkChanged_DifferentSsid_ProducesNetworkChangedEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var state1 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:11:03"
        };

        var state2 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "GuestWiFi",
            Bssid = "84:16:F9:2A:99:99"
        };

        _detector.Process(state1, now);
        var events = _detector.Process(state2, now.AddSeconds(10));

        Assert.Single(events);
        Assert.Equal(WifiEventType.NetworkChanged, events[0].Type);
        Assert.Equal("OfficeWiFi", events[0].PreviousSsid);
        Assert.Equal("GuestWiFi", events[0].CurrentSsid);
    }

    [Fact]
    public void BriefRoamInterruption_Within3Seconds_CorrelatesToOneRoamEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var state1 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:11:03"
        };

        var disconnectedState = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = false,
            Availability = WifiMonitorAvailability.Disconnected
        };

        var state2 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:26:91"
        };

        _detector.Process(state1, now);
        var eventsDisconnect = _detector.Process(disconnectedState, now.AddSeconds(1));
        Assert.Empty(eventsDisconnect); // Pending disconnect correlation window active

        var eventsReconnect = _detector.Process(state2, now.AddSeconds(2));
        Assert.Single(eventsReconnect);
        Assert.Equal(WifiEventType.Roam, eventsReconnect[0].Type);
        Assert.Equal("84:16:F9:2A:11:03", eventsReconnect[0].PreviousBssid);
        Assert.Equal("84:16:F9:2A:26:91", eventsReconnect[0].CurrentBssid);
    }

    [Fact]
    public void ReconnectSameBssid_Within3Seconds_ProducesNoRoamEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var state1 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:11:03"
        };

        var disconnectedState = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = false,
            Availability = WifiMonitorAvailability.Disconnected
        };

        _detector.Process(state1, now);
        _detector.Process(disconnectedState, now.AddSeconds(1));
        var eventsReconnect = _detector.Process(state1, now.AddSeconds(2));

        Assert.Empty(eventsReconnect); // Reconnected to same AP within 3s, no noise
    }

    [Fact]
    public void Disconnect_Exceeding3Seconds_ProducesDisconnectedEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var state1 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:11:03"
        };

        var disconnectedState = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = false,
            Availability = WifiMonitorAvailability.Disconnected
        };

        _detector.Process(state1, now);
        _detector.Process(disconnectedState, now.AddSeconds(1));

        // 4 seconds pass
        var pendingEvents = _detector.CheckPendingDisconnect(now.AddSeconds(5));
        Assert.Single(pendingEvents);
        Assert.Equal(WifiEventType.Disconnected, pendingEvents[0].Type);
        Assert.Equal("OfficeWiFi", pendingEvents[0].PreviousSsid);
    }

    [Fact]
    public void AdapterSwitch_ResetsState_DoesNotClassifyAsRoam()
    {
        var now = DateTimeOffset.UtcNow;
        var state1 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:11:03"
        };

        var state2 = new WifiConnectionState
        {
            AdapterId = "Adapter2",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:26:91"
        };

        _detector.Process(state1, now);
        _detector.Reset(); // Switching adapter resets roam detector

        var events = _detector.Process(state2, now.AddSeconds(1));
        Assert.Single(events);
        Assert.Equal(WifiEventType.Connected, events[0].Type); // New connected event, NOT roam
    }

    [Fact]
    public void TelemetryChangesOnly_ProducesNoEvents()
    {
        var now = DateTimeOffset.UtcNow;
        var state1 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:11:03",
            RssiDbm = -60,
            SignalQualityPercent = 80,
            Channel = 36
        };

        var state2 = new WifiConnectionState
        {
            AdapterId = "Adapter1",
            IsConnected = true,
            Ssid = "OfficeWiFi",
            Bssid = "84:16:F9:2A:11:03",
            RssiDbm = -55,
            SignalQualityPercent = 90,
            Channel = 36
        };

        _detector.Process(state1, now);
        var events = _detector.Process(state2, now.AddSeconds(1));

        Assert.Empty(events);
    }
}
