namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public sealed class TestResultUpdatedEventArgs : EventArgs
{
    public Guid TargetId { get; init; }
    public TestResult Result { get; init; } = null!;
}

public interface ITestRunner : IDisposable
{
    bool IsRunning { get; }
    Guid? ActiveTestSetId { get; }
    int IntervalMs { get; set; }
    IReadOnlyDictionary<Guid, TestResult> CurrentResults { get; }
    event EventHandler<TestResultUpdatedEventArgs>? ResultUpdated;
    Task StartAsync(TestSet testSet, CancellationToken ct = default);
    void Stop();
}
