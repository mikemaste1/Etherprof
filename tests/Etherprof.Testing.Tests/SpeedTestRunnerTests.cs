namespace Etherprof.Testing.Tests;

using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Core;
using Etherprof.SpeedTest;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class SpeedTestRunnerTests
{
    private readonly Mock<ISpeedTestProvider> _mockProvider = new();
    private readonly Mock<INetworkAdapterProvider> _mockAdapterProvider = new();
    private readonly Mock<IWifiMonitor> _mockWifiMonitor = new();
    private readonly BoundedSpeedTestResultLog _resultLog = new();

    private readonly SpeedTestRunner _runner;

    public SpeedTestRunnerTests()
    {
        _runner = new SpeedTestRunner(
            _mockProvider.Object,
            _mockAdapterProvider.Object,
            _mockWifiMonitor.Object,
            _resultLog,
            NullLogger<SpeedTestRunner>.Instance);
    }

    [Fact]
    public async Task StartAsync_AdapterUnavailable_FailsCleanly()
    {
        _mockAdapterProvider.Setup(a => a.GetStateAsync("adapter1"))
            .ReturnsAsync(new NetworkAdapterState { AdapterId = "adapter1", IsAvailable = false });

        var result = await _runner.StartAsync(SpeedTestDirection.Download, SpeedCalculationHelper.Size10MiB, "adapter1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Selected adapter unavailable.", result.Error);
        Assert.Equal(1, _resultLog.Count);
    }

    [Fact]
    public async Task StartAsync_NoIPv4Address_FailsCleanly()
    {
        _mockAdapterProvider.Setup(a => a.GetStateAsync("adapter1"))
            .ReturnsAsync(new NetworkAdapterState
            {
                AdapterId = "adapter1",
                IsAvailable = true,
                IsConnected = true,
                IPv4Address = null
            });

        var result = await _runner.StartAsync(SpeedTestDirection.Download, SpeedCalculationHelper.Size10MiB, "adapter1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("No usable IPv4 address on selected adapter.", result.Error);
    }

    [Fact]
    public async Task StartAsync_ValidAdapter_CapturesContextAndExecutesProvider()
    {
        _mockAdapterProvider.Setup(a => a.GetStateAsync("adapter1"))
            .ReturnsAsync(new NetworkAdapterState
            {
                AdapterId = "adapter1",
                IsAvailable = true,
                IsConnected = true,
                IPv4Address = "192.168.1.100"
            });

        _mockWifiMonitor.SetupGet(w => w.CurrentState)
            .Returns(new WifiConnectionState
            {
                AdapterId = "adapter1",
                Availability = WifiMonitorAvailability.Available,
                IsConnected = true,
                Ssid = "OfficeWiFi",
                Bssid = "84:16:F9:2A:11:03",
                RssiDbm = -60,
                SignalQualityPercent = 80,
                Band = WifiBand.GHz5,
                Channel = 44
            });

        _mockProvider.Setup(p => p.RunDownloadAsync(It.IsAny<SpeedTestRequest>(), It.IsAny<IProgress<SpeedTestProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeedTestResult
            {
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow.AddSeconds(1),
                Direction = SpeedTestDirection.Download,
                BytesTransferred = SpeedCalculationHelper.Size10MiB,
                Elapsed = TimeSpan.FromSeconds(1),
                AverageMbps = 83.89,
                Success = true
            });

        var result = await _runner.StartAsync(SpeedTestDirection.Download, SpeedCalculationHelper.Size10MiB, "adapter1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(83.89, result.AverageMbps);
        Assert.True(result.NetworkContext.IsWifi);
        Assert.Equal("OfficeWiFi", result.NetworkContext.Ssid);
        Assert.Equal("84:16:F9:2A:11:03", result.NetworkContext.StartBssid);
        Assert.False(result.NetworkContext.RoamedDuringTest);
        Assert.Equal(1, _resultLog.Count);
    }
}
