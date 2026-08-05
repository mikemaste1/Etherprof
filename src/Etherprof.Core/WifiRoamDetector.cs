namespace Etherprof.Core;

using Etherprof.Contracts.Models;

public sealed class WifiRoamDetector
{
    public static readonly TimeSpan CorrelationWindow = TimeSpan.FromSeconds(3);

    private WifiConnectionState? _lastState;
    private WifiConnectionState? _pendingDisconnectState;
    private DateTimeOffset? _pendingDisconnectTime;

    public void Reset()
    {
        _lastState = null;
        _pendingDisconnectState = null;
        _pendingDisconnectTime = null;
    }

    public List<WifiEvent> Process(WifiConnectionState current, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(current);

        var events = new List<WifiEvent>();

        // Handle active pending disconnect evaluation first if we have a current update
        if (_pendingDisconnectState is not null && _pendingDisconnectTime.HasValue)
        {
            var elapsed = timestamp - _pendingDisconnectTime.Value;

            if (current.IsConnected && !string.IsNullOrEmpty(current.Ssid))
            {
                if (elapsed <= CorrelationWindow)
                {
                    // Reconnected within correlation window
                    var prev = _pendingDisconnectState;
                    _pendingDisconnectState = null;
                    _pendingDisconnectTime = null;

                    if (string.Equals(prev.Ssid, current.Ssid, StringComparison.Ordinal))
                    {
                        // Same SSID
                        if (!string.Equals(prev.Bssid, current.Bssid, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(prev.Bssid) && !string.IsNullOrEmpty(current.Bssid))
                        {
                            // BSSID changed => ROAM
                            events.Add(CreateRoamEvent(prev, current, timestamp));
                        }
                        // Same BSSID => Reconnected to same AP, swallow disconnect noise
                    }
                    else
                    {
                        // Different SSID => NETWORK CHANGED
                        events.Add(CreateNetworkChangedEvent(prev, current, timestamp));
                    }

                    _lastState = current;
                    return events;
                }
                else
                {
                    // Disconnect window expired before reconnect
                    events.Add(CreateDisconnectedEvent(_pendingDisconnectState, _pendingDisconnectTime.Value));
                    _pendingDisconnectState = null;
                    _pendingDisconnectTime = null;

                    // Now process current connection as new CONNECTED event
                    events.Add(CreateConnectedEvent(current, timestamp));
                    _lastState = current;
                    return events;
                }
            }
        }

        if (current.IsConnected && !string.IsNullOrEmpty(current.Ssid))
        {
            if (_lastState is null || !_lastState.IsConnected || string.IsNullOrEmpty(_lastState.Ssid))
            {
                // Initial or fresh connection
                events.Add(CreateConnectedEvent(current, timestamp));
            }
            else
            {
                // Previously connected, now connected
                if (!string.Equals(_lastState.Ssid, current.Ssid, StringComparison.Ordinal))
                {
                    // SSID changed => NETWORK CHANGED
                    events.Add(CreateNetworkChangedEvent(_lastState, current, timestamp));
                }
                else if (!string.Equals(_lastState.Bssid, current.Bssid, StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrEmpty(_lastState.Bssid) && !string.IsNullOrEmpty(current.Bssid))
                {
                    // Same SSID, BSSID changed => ROAM
                    events.Add(CreateRoamEvent(_lastState, current, timestamp));
                }
                // If telemetry only changed (RSSI, quality, channel), NO EVENT generated!
            }

            _lastState = current;
        }
        else
        {
            // Current state is disconnected or unavailable
            if (_lastState is not null && _lastState.IsConnected && !string.IsNullOrEmpty(_lastState.Ssid))
            {
                if (_pendingDisconnectState is null)
                {
                    // Start correlation window
                    _pendingDisconnectState = _lastState;
                    _pendingDisconnectTime = timestamp;
                }
            }
        }

        return events;
    }

    public List<WifiEvent> CheckPendingDisconnect(DateTimeOffset timestamp)
    {
        var events = new List<WifiEvent>();

        if (_pendingDisconnectState is not null && _pendingDisconnectTime.HasValue)
        {
            if (timestamp - _pendingDisconnectTime.Value > CorrelationWindow)
            {
                events.Add(CreateDisconnectedEvent(_pendingDisconnectState, _pendingDisconnectTime.Value));
                _lastState = new WifiConnectionState
                {
                    AdapterId = _pendingDisconnectState.AdapterId,
                    Availability = WifiMonitorAvailability.Disconnected,
                    IsConnected = false,
                    Timestamp = timestamp
                };
                _pendingDisconnectState = null;
                _pendingDisconnectTime = null;
            }
        }

        return events;
    }

    private static WifiEvent CreateConnectedEvent(WifiConnectionState current, DateTimeOffset timestamp)
    {
        return new WifiEvent
        {
            Timestamp = timestamp,
            Type = WifiEventType.Connected,
            CurrentSsid = current.Ssid,
            CurrentBssid = current.Bssid,
            CurrentRssiDbm = current.RssiDbm,
            CurrentSignalQualityPercent = current.SignalQualityPercent
        };
    }

    private static WifiEvent CreateDisconnectedEvent(WifiConnectionState lastConnected, DateTimeOffset timestamp)
    {
        return new WifiEvent
        {
            Timestamp = timestamp,
            Type = WifiEventType.Disconnected,
            PreviousSsid = lastConnected.Ssid,
            PreviousBssid = lastConnected.Bssid,
            PreviousRssiDbm = lastConnected.RssiDbm,
            PreviousSignalQualityPercent = lastConnected.SignalQualityPercent
        };
    }

    private static WifiEvent CreateRoamEvent(WifiConnectionState prev, WifiConnectionState current, DateTimeOffset timestamp)
    {
        return new WifiEvent
        {
            Timestamp = timestamp,
            Type = WifiEventType.Roam,
            PreviousSsid = prev.Ssid,
            CurrentSsid = current.Ssid,
            PreviousBssid = prev.Bssid,
            CurrentBssid = current.Bssid,
            PreviousRssiDbm = prev.RssiDbm,
            CurrentRssiDbm = current.RssiDbm,
            PreviousSignalQualityPercent = prev.SignalQualityPercent,
            CurrentSignalQualityPercent = current.SignalQualityPercent
        };
    }

    private static WifiEvent CreateNetworkChangedEvent(WifiConnectionState prev, WifiConnectionState current, DateTimeOffset timestamp)
    {
        return new WifiEvent
        {
            Timestamp = timestamp,
            Type = WifiEventType.NetworkChanged,
            PreviousSsid = prev.Ssid,
            CurrentSsid = current.Ssid,
            PreviousBssid = prev.Bssid,
            CurrentBssid = current.Bssid,
            PreviousRssiDbm = prev.RssiDbm,
            CurrentRssiDbm = current.RssiDbm,
            PreviousSignalQualityPercent = prev.SignalQualityPercent,
            CurrentSignalQualityPercent = current.SignalQualityPercent
        };
    }
}
