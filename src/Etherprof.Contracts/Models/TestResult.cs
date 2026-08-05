namespace Etherprof.Contracts.Models;

public enum TestStatus
{
    Untested,
    Testing,
    Success,
    Failure
}

public sealed class TestResult
{
    public DateTimeOffset Timestamp { get; init; }
    public TestStatus Status { get; init; }
    public TimeSpan? Latency { get; init; }
    public string? Error { get; init; }
}
