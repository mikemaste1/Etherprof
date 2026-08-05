namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public interface ISpeedTestProvider
{
    string Name { get; }

    Task<SpeedTestResult> RunDownloadAsync(
        SpeedTestRequest request,
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken);

    Task<SpeedTestResult> RunUploadAsync(
        SpeedTestRequest request,
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken);
}
