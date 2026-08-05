namespace Etherprof.StreamTest.Models;

public enum StreamTestProtocol
{
    Tcp,
    Udp
}

public enum StreamTestDirection
{
    Send,
    Receive
}

public enum StreamTestState
{
    Idle,
    Connecting,
    Active,
    Reconnecting,
    NoResponse,
    ServerLost,
    Stopping,
    Failed
}

public enum StreamRuntimeEventType
{
    Started,
    TcpConnected,
    TcpLost,
    TcpReconnected,
    UdpNoResponse,
    UdpRecovered,
    ServerLost,
    Roam,
    Stopped,
    Error
}

public sealed class StreamTestRequest
{
    public string ServerHost { get; init; } = "";

    public int Port { get; init; } = 49100;

    public StreamTestProtocol Protocol { get; init; } = StreamTestProtocol.Tcp;

    public StreamTestDirection Direction { get; init; } = StreamTestDirection.Send;

    public long TargetBitsPerSecond { get; init; } = 2_000_000;

    public string AdapterId { get; init; } = "";

    public string LocalIpAddress { get; init; } = "";

    public const long MinTargetBitsPerSecond = 200_000;
    public const long MaxTargetBitsPerSecond = 10_000_000;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServerHost))
        {
            throw new ArgumentException("Server host cannot be empty.", nameof(ServerHost));
        }

        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), "Port must be between 1 and 65535.");
        }

        if (TargetBitsPerSecond is < MinTargetBitsPerSecond or > MaxTargetBitsPerSecond)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetBitsPerSecond), $"Target bitrate must be between {MinTargetBitsPerSecond} bps and {MaxTargetBitsPerSecond} bps.");
        }

        if (string.IsNullOrWhiteSpace(LocalIpAddress))
        {
            throw new InvalidOperationException("Cannot start stream test. Selected adapter has no usable IPv4 address.");
        }
    }
}

public sealed class StreamTestStatistics
{
    public DateTimeOffset StartedAt { get; init; }

    public TimeSpan Duration { get; init; }

    public long TargetBitsPerSecond { get; init; }

    public double ActualBitsPerSecond { get; init; }

    public long BytesTransferred { get; init; }

    public long PacketsSent { get; init; }

    public long PacketsReceived { get; init; }

    public long PacketsLost { get; init; }

    public long DuplicatePackets { get; init; }

    public long OutOfOrderPackets { get; init; }

    public double? LossPercent { get; init; }

    public double? JitterMs { get; init; }

    public TimeSpan? LongestGap { get; init; }

    public int ReconnectCount { get; init; }

    public TimeSpan? CurrentInterruption { get; init; }

    public TimeSpan? LastInterruption { get; init; }

    public TimeSpan? LongestInterruption { get; init; }
}

public sealed class StreamRuntimeEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public StreamRuntimeEventType Type { get; init; }

    public string? Message { get; init; }

    public string? PreviousBssid { get; init; }

    public string? CurrentBssid { get; init; }

    public TimeSpan? Duration { get; init; }

    public long? PacketDelta { get; init; }
}

public sealed class StreamTestProgressEventArgs : EventArgs
{
    public StreamTestStatistics Statistics { get; }

    public StreamTestProgressEventArgs(StreamTestStatistics statistics)
    {
        Statistics = statistics;
    }
}

public sealed class StreamTestStateChangedEventArgs : EventArgs
{
    public StreamTestState PreviousState { get; }
    public StreamTestState NewState { get; }
    public string? Message { get; }

    public StreamTestStateChangedEventArgs(StreamTestState previousState, StreamTestState newState, string? message = null)
    {
        PreviousState = previousState;
        NewState = newState;
        Message = message;
    }
}
