namespace Etherprof.Contracts.Models;

public sealed class SpeedTestRequest
{
    public SpeedTestDirection Direction { get; init; }

    public long TransferBytes { get; init; }

    public string AdapterId { get; init; } = "";

    public string LocalIpAddress { get; init; } = "";
}
