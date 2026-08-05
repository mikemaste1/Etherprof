namespace Etherprof.Contracts.Models;

public sealed class SpeedTestResult
{
    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset CompletedAt { get; init; }

    public SpeedTestDirection Direction { get; init; }

    public long BytesTransferred { get; init; }

    public TimeSpan Elapsed { get; init; }

    public double AverageMbps { get; init; }

    public bool Success { get; init; }

    public bool Cancelled { get; init; }

    public string? Error { get; init; }

    public SpeedTestNetworkContext NetworkContext { get; init; } = new();
}
