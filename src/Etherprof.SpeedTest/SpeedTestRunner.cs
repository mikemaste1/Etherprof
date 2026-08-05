namespace Etherprof.SpeedTest;

using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Core;
using Microsoft.Extensions.Logging;

public sealed class SpeedTestRunner : ISpeedTestRunner, IDisposable
{
    private readonly ISpeedTestProvider _provider;
    private readonly INetworkAdapterProvider _adapterProvider;
    private readonly IWifiMonitor _wifiMonitor;
    private readonly BoundedSpeedTestResultLog _resultLog;
    private readonly ILogger<SpeedTestRunner> _logger;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public bool IsRunning { get; private set; }
    public SpeedTestDirection? ActiveDirection { get; private set; }
    public BoundedSpeedTestResultLog ResultLog => _resultLog;

    public event EventHandler<SpeedTestProgressEventArgs>? ProgressChanged;
    public event EventHandler<SpeedTestCompletedEventArgs>? Completed;

    public SpeedTestRunner(
        ISpeedTestProvider provider,
        INetworkAdapterProvider adapterProvider,
        IWifiMonitor wifiMonitor,
        BoundedSpeedTestResultLog resultLog,
        ILogger<SpeedTestRunner> logger)
    {
        _provider = provider;
        _adapterProvider = adapterProvider;
        _wifiMonitor = wifiMonitor;
        _resultLog = resultLog;
        _logger = logger;
    }

    public async Task<SpeedTestResult> StartAsync(
        SpeedTestDirection direction,
        long transferBytes,
        string adapterId,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource cts;

        lock (_lock)
        {
            if (IsRunning)
            {
                StopInternal(); // Cancel active test if another starts (Requirement 22)
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts = _cts;
            IsRunning = true;
            ActiveDirection = direction;
        }

        SpeedTestNetworkContext context = new();
        EventHandler<WifiRoamEventArgs>? roamHandler = null;

        try
        {
            // 1. Resolve selected adapter state and local IPv4
            var adapterState = await _adapterProvider.GetStateAsync(adapterId);
            if (adapterState is null || !adapterState.IsAvailable)
            {
                var failResult = CreateFailedResult(direction, "Selected adapter unavailable.", context);
                _resultLog.Add(failResult);
                Completed?.Invoke(this, new SpeedTestCompletedEventArgs(failResult));
                return failResult;
            }

            if (string.IsNullOrWhiteSpace(adapterState.IPv4Address))
            {
                var failResult = CreateFailedResult(direction, "No usable IPv4 address on selected adapter.", context);
                _resultLog.Add(failResult);
                Completed?.Invoke(this, new SpeedTestCompletedEventArgs(failResult));
                return failResult;
            }

            string localIp = adapterState.IPv4Address;

            // 2. Capture Start Wi-Fi Context
            var wifiState = _wifiMonitor.CurrentState;
            bool isWifi = wifiState is not null && wifiState.Availability != WifiMonitorAvailability.NotWifiAdapter;

            string? startBssid = wifiState?.Bssid;
            string? endBssid = startBssid;
            int? startRssi = wifiState?.RssiDbm;
            int? endRssi = startRssi;
            int? startQuality = wifiState?.SignalQualityPercent;
            int? endQuality = startQuality;
            WifiBand startBand = wifiState?.Band ?? WifiBand.Unknown;
            WifiBand endBand = startBand;
            int? startChannel = wifiState?.Channel;
            int? endChannel = startChannel;

            bool roamedDuringTest = false;
            int roamCount = 0;
            string? firstRoamFrom = null;
            string? firstRoamTo = null;

            // Subscribe to normalized Wi-Fi roam events during test (Technical Amendment 9)
            roamHandler = (_, args) =>
            {
                roamedDuringTest = true;
                roamCount++;
                if (firstRoamFrom is null)
                {
                    firstRoamFrom = args.Event.PreviousBssid;
                    firstRoamTo = args.Event.CurrentBssid;
                }
            };

            if (isWifi)
            {
                _wifiMonitor.Roamed += roamHandler;
            }

            // Create SpeedTestRequest
            var request = new SpeedTestRequest
            {
                Direction = direction,
                TransferBytes = transferBytes,
                AdapterId = adapterId,
                LocalIpAddress = localIp
            };

            var progressReporter = new Progress<SpeedTestProgress>(p =>
            {
                ProgressChanged?.Invoke(this, new SpeedTestProgressEventArgs(p));
            });

            // 3. Execute speedtest with provider
            SpeedTestResult providerResult;
            if (direction == SpeedTestDirection.Download)
            {
                providerResult = await _provider.RunDownloadAsync(request, progressReporter, cts.Token);
            }
            else
            {
                providerResult = await _provider.RunUploadAsync(request, progressReporter, cts.Token);
            }

            // 4. Capture End Wi-Fi Context
            var endWifiState = _wifiMonitor.CurrentState;
            if (endWifiState is not null)
            {
                endBssid = endWifiState.Bssid;
                endRssi = endWifiState.RssiDbm;
                endQuality = endWifiState.SignalQualityPercent;
                endBand = endWifiState.Band;
                endChannel = endWifiState.Channel;

                if (!roamedDuringTest && !string.Equals(startBssid, endBssid, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(startBssid) && !string.IsNullOrEmpty(endBssid))
                {
                    roamedDuringTest = true;
                    roamCount++;
                    firstRoamFrom = startBssid;
                    firstRoamTo = endBssid;
                }
            }

            context = new SpeedTestNetworkContext
            {
                AdapterId = adapterId,
                LocalIpAddress = localIp,
                IsWifi = isWifi,
                Ssid = wifiState?.Ssid,
                StartBssid = startBssid,
                EndBssid = endBssid,
                StartRssiDbm = startRssi,
                EndRssiDbm = endRssi,
                StartSignalQualityPercent = startQuality,
                EndSignalQualityPercent = endQuality,
                StartBand = startBand,
                EndBand = endBand,
                StartChannel = startChannel,
                EndChannel = endChannel,
                RoamedDuringTest = roamedDuringTest,
                RoamCount = roamCount,
                FirstRoamFromBssid = firstRoamFrom,
                FirstRoamToBssid = firstRoamTo
            };

            var finalResult = new SpeedTestResult
            {
                StartedAt = providerResult.StartedAt,
                CompletedAt = providerResult.CompletedAt,
                Direction = providerResult.Direction,
                BytesTransferred = providerResult.BytesTransferred,
                Elapsed = providerResult.Elapsed,
                AverageMbps = providerResult.AverageMbps,
                Success = providerResult.Success,
                Cancelled = providerResult.Cancelled,
                Error = providerResult.Error,
                NetworkContext = context
            };

            _resultLog.Add(finalResult);
            Completed?.Invoke(this, new SpeedTestCompletedEventArgs(finalResult));
            return finalResult;
        }
        catch (OperationCanceledException)
        {
            var cancelResult = new SpeedTestResult
            {
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Direction = direction,
                BytesTransferred = 0,
                Elapsed = TimeSpan.Zero,
                AverageMbps = 0,
                Success = false,
                Cancelled = true,
                Error = "Cancelled",
                NetworkContext = context
            };
            _resultLog.Add(cancelResult);
            Completed?.Invoke(this, new SpeedTestCompletedEventArgs(cancelResult));
            return cancelResult;
        }
        finally
        {
            if (roamHandler is not null)
            {
                _wifiMonitor.Roamed -= roamHandler;
            }

            lock (_lock)
            {
                IsRunning = false;
                ActiveDirection = null;
                _cts = null;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    private static SpeedTestResult CreateFailedResult(SpeedTestDirection direction, string errorMsg, SpeedTestNetworkContext context)
    {
        return new SpeedTestResult
        {
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Direction = direction,
            BytesTransferred = 0,
            Elapsed = TimeSpan.Zero,
            AverageMbps = 0,
            Success = false,
            Cancelled = false,
            Error = errorMsg,
            NetworkContext = context
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
