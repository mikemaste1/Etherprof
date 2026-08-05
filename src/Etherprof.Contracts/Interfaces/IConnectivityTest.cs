namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public interface IConnectivityTest
{
    string Type { get; }
    Task<TestResult> ExecuteAsync(TestTarget target, CancellationToken ct = default);
}
