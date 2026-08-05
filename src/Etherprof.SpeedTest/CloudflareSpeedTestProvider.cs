namespace Etherprof.SpeedTest;

using System.Diagnostics;
using System.Net.Http.Headers;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Core;
using Microsoft.Extensions.Logging;

public sealed class CloudflareSpeedTestProvider : ISpeedTestProvider
{
    private static readonly Uri DownloadBaseUri = new("https://speed.cloudflare.com/__down");
    private static readonly Uri UploadBaseUri = new("https://speed.cloudflare.com/__up");

    private readonly ILogger<CloudflareSpeedTestProvider> _logger;

    public string Name => "Cloudflare";

    public CloudflareSpeedTestProvider(ILogger<CloudflareSpeedTestProvider> logger)
    {
        _logger = logger;
    }

    public async Task<SpeedTestResult> RunDownloadAsync(
        SpeedTestRequest request,
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = new Stopwatch();
        long totalBytesRead = 0;

        using var client = SelectedAdapterHttpClientFactory.CreateClient(request.LocalIpAddress, _logger);

        try
        {
            _logger.LogInformation("Starting download test of {Bytes} bytes on bound IP {LocalIp}", request.TransferBytes, request.LocalIpAddress);

            stopwatch.Start(); // Timing starts immediately before first request (Technical Amendment 4)

            const long maxChunkBytes = 50_000_000; // Cloudflare caps single __down GET requests to <100MB
            long targetBytes = request.TransferBytes;
            long remainingBytesToRequest = targetBytes;
            long lastProgressReportTime = 0;
            var buffer = new byte[64 * 1024];

            while (remainingBytesToRequest > 0)
            {
                long currentChunkBytes = Math.Min(remainingBytesToRequest, maxChunkBytes);
                remainingBytesToRequest -= currentChunkBytes;

                var uri = new Uri($"{DownloadBaseUri}?bytes={currentChunkBytes}&nocache={Guid.NewGuid()}");
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);

                using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalBytesRead += bytesRead;

                    long nowMs = stopwatch.ElapsedMilliseconds;
                    if (nowMs - lastProgressReportTime >= 150) // Throttle updates (~150ms)
                    {
                        lastProgressReportTime = nowMs;
                        ReportProgress(progress, startedAt, totalBytesRead, targetBytes, stopwatch.Elapsed);
                    }
                }
            }

            stopwatch.Stop();
            var completedAt = DateTimeOffset.UtcNow;

            // Report final progress update
            ReportProgress(progress, startedAt, totalBytesRead, targetBytes, stopwatch.Elapsed);

            bool isExactMatch = totalBytesRead == targetBytes;
            double mbps = SpeedCalculationHelper.CalculateMbps(totalBytesRead, stopwatch.Elapsed);

            _logger.LogInformation("Download completed: {Transferred}/{Target} bytes in {Elapsed} ms ({Mbps:F1} Mbps)",
                totalBytesRead, targetBytes, stopwatch.ElapsedMilliseconds, mbps);

            return new SpeedTestResult
            {
                StartedAt = startedAt,
                CompletedAt = completedAt,
                Direction = SpeedTestDirection.Download,
                BytesTransferred = totalBytesRead,
                Elapsed = stopwatch.Elapsed,
                AverageMbps = mbps,
                Success = isExactMatch,
                Cancelled = false,
                Error = isExactMatch ? null : $"Incomplete transfer ({totalBytesRead}/{targetBytes} bytes)"
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation("Download test cancelled");
            return new SpeedTestResult
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Direction = SpeedTestDirection.Download,
                BytesTransferred = totalBytesRead,
                Elapsed = stopwatch.Elapsed,
                AverageMbps = SpeedCalculationHelper.CalculateMbps(totalBytesRead, stopwatch.Elapsed),
                Success = false,
                Cancelled = true,
                Error = "Cancelled"
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Download test failed");
            return new SpeedTestResult
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Direction = SpeedTestDirection.Download,
                BytesTransferred = totalBytesRead,
                Elapsed = stopwatch.Elapsed,
                AverageMbps = 0,
                Success = false,
                Cancelled = false,
                Error = ex.Message
            };
        }
    }

    public async Task<SpeedTestResult> RunUploadAsync(
        SpeedTestRequest request,
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = new Stopwatch();
        long totalEmittedBytes = 0;

        using var client = SelectedAdapterHttpClientFactory.CreateClient(request.LocalIpAddress, _logger);

        try
        {
            var uri = new Uri($"{UploadBaseUri}?nocache={Guid.NewGuid()}");
            using var payloadStream = new GeneratedPayloadStream(request.TransferBytes);
            long lastProgressReportTime = 0;

            payloadStream.OnBytesEmitted = bytesCount =>
            {
                totalEmittedBytes += bytesCount;

                long nowMs = stopwatch.ElapsedMilliseconds;
                if (nowMs - lastProgressReportTime >= 150)
                {
                    lastProgressReportTime = nowMs;
                    ReportProgress(progress, startedAt, totalEmittedBytes, request.TransferBytes, stopwatch.Elapsed);
                }
            };

            using var content = new StreamContent(payloadStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };

            _logger.LogInformation("Starting upload test of {Bytes} bytes on bound IP {LocalIp}", request.TransferBytes, request.LocalIpAddress);

            stopwatch.Start(); // Timing starts before HTTP request send (Technical Amendment 4)

            using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            stopwatch.Stop();
            var completedAt = DateTimeOffset.UtcNow;

            // Report final progress update
            ReportProgress(progress, startedAt, totalEmittedBytes, request.TransferBytes, stopwatch.Elapsed);

            bool isExactMatch = totalEmittedBytes == request.TransferBytes;
            double mbps = SpeedCalculationHelper.CalculateMbps(totalEmittedBytes, stopwatch.Elapsed);

            _logger.LogInformation("Upload completed: {Transferred}/{Target} bytes in {Elapsed} ms ({Mbps:F1} Mbps)",
                totalEmittedBytes, request.TransferBytes, stopwatch.ElapsedMilliseconds, mbps);

            return new SpeedTestResult
            {
                StartedAt = startedAt,
                CompletedAt = completedAt,
                Direction = SpeedTestDirection.Upload,
                BytesTransferred = totalEmittedBytes,
                Elapsed = stopwatch.Elapsed,
                AverageMbps = mbps,
                Success = isExactMatch,
                Cancelled = false,
                Error = isExactMatch ? null : $"Incomplete upload ({totalEmittedBytes}/{request.TransferBytes} bytes)"
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation("Upload test cancelled");
            return new SpeedTestResult
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Direction = SpeedTestDirection.Upload,
                BytesTransferred = totalEmittedBytes,
                Elapsed = stopwatch.Elapsed,
                AverageMbps = SpeedCalculationHelper.CalculateMbps(totalEmittedBytes, stopwatch.Elapsed),
                Success = false,
                Cancelled = true,
                Error = "Cancelled"
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Upload test failed");
            return new SpeedTestResult
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Direction = SpeedTestDirection.Upload,
                BytesTransferred = totalEmittedBytes,
                Elapsed = stopwatch.Elapsed,
                AverageMbps = 0,
                Success = false,
                Cancelled = false,
                Error = ex.Message
            };
        }
    }

    private static void ReportProgress(
        IProgress<SpeedTestProgress>? progress,
        DateTimeOffset startedAt,
        long bytesTransferred,
        long totalBytes,
        TimeSpan elapsed)
    {
        if (progress == null) return;

        double mbps = SpeedCalculationHelper.CalculateMbps(bytesTransferred, elapsed);
        double percent = totalBytes > 0 ? Math.Min(100.0, (bytesTransferred * 100.0) / totalBytes) : 0;

        progress.Report(new SpeedTestProgress
        {
            Timestamp = DateTimeOffset.UtcNow,
            BytesTransferred = bytesTransferred,
            TotalBytes = totalBytes,
            CurrentMbps = mbps,
            PercentComplete = percent
        });
    }
}
