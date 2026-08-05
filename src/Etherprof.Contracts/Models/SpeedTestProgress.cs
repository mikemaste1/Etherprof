namespace Etherprof.Contracts.Models;

public sealed class SpeedTestProgress
{
    public DateTimeOffset Timestamp { get; init; }

    public long BytesTransferred { get; init; }

    public long TotalBytes { get; init; }

    public double CurrentMbps { get; init; }

    public double PercentComplete { get; init; }
}
