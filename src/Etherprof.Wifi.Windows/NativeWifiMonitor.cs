namespace Etherprof.Wifi.Windows;

using System.Runtime.InteropServices;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Core;
using Etherprof.Wifi.Windows.Interop;
using Microsoft.Extensions.Logging;

public sealed class NativeWifiMonitor : IWifiMonitor
{
    private readonly ILogger<NativeWifiMonitor> _logger;
    private readonly WifiRoamDetector _roamDetector = new();
    private readonly object _lock = new();

    private WlanSafeHandle? _wlanHandle;
    private Guid _monitoredInterfaceGuid = Guid.Empty;
    private string _adapterId = "";

    private NativeWifiMethods.WlanNotificationCallback? _notificationCallback;
    private Timer? _timer;
    private CancellationTokenSource? _cts;
    private bool _isRealtimeQualitySupported = true; // Tried at runtime
    private bool _disposed;

    public WifiConnectionState? CurrentState { get; private set; }

    public event EventHandler<WifiStateChangedEventArgs>? StateChanged;
    public event EventHandler<WifiRoamEventArgs>? Roamed;
    public event EventHandler<WifiConnectionEventArgs>? ConnectionEvent;

    public NativeWifiMonitor(ILogger<NativeWifiMonitor> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(string adapterId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            StopInternal();

            _adapterId = adapterId;
            _roamDetector.Reset();

            if (string.IsNullOrWhiteSpace(adapterId))
            {
                SetState(new WifiConnectionState
                {
                    AdapterId = adapterId,
                    Availability = WifiMonitorAvailability.NotWifiAdapter,
                    IsConnected = false,
                    Timestamp = DateTimeOffset.UtcNow
                });
                return Task.CompletedTask;
            }

            // Open WLAN API handle
            uint result = NativeWifiMethods.WlanOpenHandle(2, IntPtr.Zero, out _, out _wlanHandle);
            if (result != NativeWifiMethods.ERROR_SUCCESS || _wlanHandle == null || _wlanHandle.IsInvalid)
            {
                _logger.LogWarning("WlanOpenHandle failed with error code {Error}", result);
                SetState(new WifiConnectionState
                {
                    AdapterId = adapterId,
                    Availability = WifiMonitorAvailability.Error,
                    Error = $"WlanOpenHandle failed (Code {result})",
                    Timestamp = DateTimeOffset.UtcNow
                });
                return Task.CompletedTask;
            }

            // Match adapter ID string to WLAN Interface GUID
            if (!TryFindMatchingInterface(_wlanHandle, adapterId, out _monitoredInterfaceGuid))
            {
                _logger.LogInformation("Adapter {AdapterId} is not a Wi-Fi interface", adapterId);
                SetState(new WifiConnectionState
                {
                    AdapterId = adapterId,
                    Availability = WifiMonitorAvailability.NotWifiAdapter,
                    IsConnected = false,
                    Timestamp = DateTimeOffset.UtcNow
                });
                return Task.CompletedTask;
            }

            _logger.LogInformation("Started Wi-Fi monitoring for adapter {AdapterId} (Guid: {Guid})", adapterId, _monitoredInterfaceGuid);

            // Register notification callback
            _notificationCallback = OnWlanNotification;
            NativeWifiMethods.WlanRegisterNotification(
                _wlanHandle,
                WLAN_NOTIFICATION_SOURCE.WLAN_NOTIFICATION_SOURCE_ACM | WLAN_NOTIFICATION_SOURCE.WLAN_NOTIFICATION_SOURCE_MSM,
                true,
                _notificationCallback,
                IntPtr.Zero,
                IntPtr.Zero,
                out _);

            _cts = new CancellationTokenSource();

            // Perform initial query
            QueryAndEmitState();

            // Start 1-second telemetry timer
            _timer = new Timer(OnTimerTick, null, 1000, 1000);

            return Task.CompletedTask;
        }
    }

    public Task StopAsync()
    {
        lock (_lock)
        {
            StopInternal();
            return Task.CompletedTask;
        }
    }

    private void StopInternal()
    {
        _timer?.Dispose();
        _timer = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_wlanHandle != null && !_wlanHandle.IsInvalid)
        {
            NativeWifiMethods.WlanRegisterNotification(
                _wlanHandle,
                WLAN_NOTIFICATION_SOURCE.WLAN_NOTIFICATION_SOURCE_NONE,
                true,
                null,
                IntPtr.Zero,
                IntPtr.Zero,
                out _);

            _wlanHandle.Dispose();
            _wlanHandle = null;
        }

        _monitoredInterfaceGuid = Guid.Empty;
        _roamDetector.Reset();
    }

    private void OnWlanNotification(ref WLAN_NOTIFICATION_DATA notificationData, IntPtr context)
    {
        // Filter notifications for our monitored interface
        if (notificationData.InterfaceGuid != _monitoredInterfaceGuid)
            return;

        // Queue quick state refresh on notification
        Task.Run(() => QueryAndEmitState());
    }

    private void OnTimerTick(object? state)
    {
        QueryAndEmitState();
    }

    private void QueryAndEmitState()
    {
        lock (_lock)
        {
            if (_wlanHandle == null || _wlanHandle.IsInvalid || _monitoredInterfaceGuid == Guid.Empty)
                return;

            var timestamp = DateTimeOffset.UtcNow;
            var newState = QueryCurrentState(_wlanHandle, _monitoredInterfaceGuid, _adapterId, timestamp);

            SetState(newState);

            // Process roaming and state transitions through detector
            var events = _roamDetector.Process(newState, timestamp);

            // Check for pending disconnect expiry
            var expiredEvents = _roamDetector.CheckPendingDisconnect(timestamp);
            events.AddRange(expiredEvents);

            foreach (var ev in events)
            {
                if (ev.Type == WifiEventType.Roam)
                {
                    Roamed?.Invoke(this, new WifiRoamEventArgs(ev));
                }
                else
                {
                    ConnectionEvent?.Invoke(this, new WifiConnectionEventArgs(ev));
                }
            }
        }
    }

    private void SetState(WifiConnectionState state)
    {
        CurrentState = state;
        StateChanged?.Invoke(this, new WifiStateChangedEventArgs(state));
    }

    private WifiConnectionState QueryCurrentState(WlanSafeHandle handle, Guid interfaceGuid, string adapterId, DateTimeOffset timestamp)
    {
        uint result = NativeWifiMethods.WlanQueryInterface(
            handle,
            ref interfaceGuid,
            WLAN_INTF_OPCODE.wlan_intf_opcode_current_connection,
            IntPtr.Zero,
            out uint dataSize,
            out IntPtr pData,
            out _);

        if (result == NativeWifiMethods.ERROR_ACCESS_DENIED)
        {
            _logger.LogWarning("Windows location/privacy setting denied Wi-Fi query access");
            return QueryStateWithPrivacyRestriction(handle, interfaceGuid, adapterId, timestamp);
        }

        if (result != NativeWifiMethods.ERROR_SUCCESS || pData == IntPtr.Zero)
        {
            return new WifiConnectionState
            {
                AdapterId = adapterId,
                Availability = WifiMonitorAvailability.Disconnected,
                IsConnected = false,
                Timestamp = timestamp
            };
        }

        try
        {
            var connAttr = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(pData);
            if (connAttr.isState != WLAN_INTERFACE_STATE.wlan_interface_state_connected)
            {
                return new WifiConnectionState
                {
                    AdapterId = adapterId,
                    Availability = WifiMonitorAvailability.Disconnected,
                    IsConnected = false,
                    Timestamp = timestamp
                };
            }

            var assoc = connAttr.wlanAssociationAttributes;
            var ssid = assoc.dot11Ssid.ToString();

            string? bssid = null;
            if (assoc.dot11Bssid != null && assoc.dot11Bssid.Length == 6)
            {
                var hex = BitConverter.ToString(assoc.dot11Bssid).Replace("-", ":");
                if (hex != "00:00:00:00:00:00" && BssidNormalizer.TryNormalize(hex, out var norm))
                {
                    bssid = norm;
                }
            }

            // Check if privacy/location restrictions zeroed out BSSID/SSID despite query returning success
            if (string.IsNullOrEmpty(ssid) && string.IsNullOrEmpty(bssid))
            {
                return QueryStateWithPrivacyRestriction(handle, interfaceGuid, adapterId, timestamp);
            }

            int? signalQuality = (int)assoc.wlanSignalQuality;
            int? rssiDbm = QueryRssi(handle, interfaceGuid);
            int? channel = QueryChannel(handle, interfaceGuid);
            int? centerFreqMhz = null;

            // Try realtime connection quality API if supported
            if (_isRealtimeQualitySupported)
            {
                try
                {
                    var (rtRssi, rtQuality, rtFreq) = QueryRealtimeQuality(handle, interfaceGuid);
                    if (rtRssi.HasValue) rssiDbm = rtRssi;
                    if (rtQuality.HasValue) signalQuality = rtQuality;
                    if (rtFreq.HasValue) centerFreqMhz = rtFreq;
                }
                catch
                {
                    _isRealtimeQualitySupported = false; // Fall back gracefully on error
                }
            }

            var band = WifiBandResolver.Resolve(centerFreqMhz, channel);

            return new WifiConnectionState
            {
                AdapterId = adapterId,
                Availability = WifiMonitorAvailability.Available,
                IsConnected = true,
                Ssid = ssid,
                Bssid = bssid,
                RssiDbm = rssiDbm,
                SignalQualityPercent = signalQuality,
                Channel = channel,
                CenterFrequencyMhz = centerFreqMhz,
                Band = band,
                Timestamp = timestamp
            };
        }
        finally
        {
            NativeWifiMethods.WlanFreeMemory(pData);
        }
    }

    private WifiConnectionState QueryStateWithPrivacyRestriction(WlanSafeHandle handle, Guid interfaceGuid, string adapterId, DateTimeOffset timestamp)
    {
        int? rssi = QueryRssi(handle, interfaceGuid);
        int? channel = QueryChannel(handle, interfaceGuid);
        var band = WifiBandResolver.Resolve(null, channel);

        return new WifiConnectionState
        {
            AdapterId = adapterId,
            Availability = WifiMonitorAvailability.PermissionDenied,
            IsConnected = true,
            RssiDbm = rssi,
            Channel = channel,
            Band = band,
            Timestamp = timestamp,
            Error = "Windows privacy settings prevent Etherprof from reading SSID/BSSID."
        };
    }

    private static int? QueryRssi(WlanSafeHandle handle, Guid interfaceGuid)
    {
        uint result = NativeWifiMethods.WlanQueryInterface(
            handle,
            ref interfaceGuid,
            WLAN_INTF_OPCODE.wlan_intf_opcode_rssi,
            IntPtr.Zero,
            out _,
            out IntPtr pData,
            out _);

        if (result == NativeWifiMethods.ERROR_SUCCESS && pData != IntPtr.Zero)
        {
            try
            {
                int rssi = Marshal.ReadInt32(pData);
                return rssi;
            }
            finally
            {
                NativeWifiMethods.WlanFreeMemory(pData);
            }
        }
        return null;
    }

    private static int? QueryChannel(WlanSafeHandle handle, Guid interfaceGuid)
    {
        uint result = NativeWifiMethods.WlanQueryInterface(
            handle,
            ref interfaceGuid,
            WLAN_INTF_OPCODE.wlan_intf_opcode_channel_number,
            IntPtr.Zero,
            out _,
            out IntPtr pData,
            out _);

        if (result == NativeWifiMethods.ERROR_SUCCESS && pData != IntPtr.Zero)
        {
            try
            {
                int ch = Marshal.ReadInt32(pData);
                return ch > 0 ? ch : null;
            }
            finally
            {
                NativeWifiMethods.WlanFreeMemory(pData);
            }
        }
        return null;
    }

    private (int? rssi, int? quality, int? freqMhz) QueryRealtimeQuality(WlanSafeHandle handle, Guid interfaceGuid)
    {
        uint result = NativeWifiMethods.WlanQueryInterface(
            handle,
            ref interfaceGuid,
            WLAN_INTF_OPCODE.wlan_intf_opcode_realtime_connection_quality,
            IntPtr.Zero,
            out uint dataSize,
            out IntPtr pData,
            out _);

        if (result == NativeWifiMethods.ERROR_SUCCESS && pData != IntPtr.Zero && dataSize >= 8)
        {
            try
            {
                // Extract available fields if present
                int rssi = Marshal.ReadInt32(pData, 0);
                int quality = Marshal.ReadInt32(pData, 4);
                int freq = dataSize >= 12 ? Marshal.ReadInt32(pData, 8) : 0;
                return (rssi, quality, freq > 0 ? freq : null);
            }
            finally
            {
                NativeWifiMethods.WlanFreeMemory(pData);
            }
        }
        return (null, null, null);
    }

    private static bool TryFindMatchingInterface(WlanSafeHandle handle, string adapterId, out Guid matchingGuid)
    {
        matchingGuid = Guid.Empty;

        uint result = NativeWifiMethods.WlanEnumInterfaces(handle, IntPtr.Zero, out IntPtr pList);
        if (result != NativeWifiMethods.ERROR_SUCCESS || pList == IntPtr.Zero)
            return false;

        try
        {
            uint count = (uint)Marshal.ReadInt32(pList, 0);
            int itemSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
            IntPtr currentPtr = pList + 8; // Skip dwNumberOfItems and dwIndex (8 bytes)

            // Try parsing adapterId as Guid if possible
            bool hasParsedGuid = Guid.TryParse(adapterId, out Guid targetGuid);

            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(currentPtr);
                if (hasParsedGuid && info.InterfaceGuid == targetGuid)
                {
                    matchingGuid = info.InterfaceGuid;
                    return true;
                }
                else if (string.Equals(info.InterfaceGuid.ToString("B"), adapterId, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(info.InterfaceGuid.ToString("D"), adapterId, StringComparison.OrdinalIgnoreCase))
                {
                    matchingGuid = info.InterfaceGuid;
                    return true;
                }

                currentPtr += itemSize;
            }
        }
        finally
        {
            NativeWifiMethods.WlanFreeMemory(pList);
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopInternal();
    }
}
