namespace Etherprof.Testing;

using System.Collections.Concurrent;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Microsoft.Extensions.Logging;

public sealed class TestRunner : ITestRunner
{
    private readonly IConnectivityTest _connectivityTest;
    private readonly ILogger<TestRunner> _logger;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly ConcurrentDictionary<Guid, TestResult> _currentResults = new();

    // Recent history per target (last 10 results) for future intermittent-loss visualization
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<TestResult>> _resultHistory = new();
    private const int MaxHistoryPerTarget = 10;

    public bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;
    public Guid? ActiveTestSetId { get; private set; }
    public IReadOnlyDictionary<Guid, TestResult> CurrentResults => _currentResults;

    public event EventHandler<TestResultUpdatedEventArgs>? ResultUpdated;

    public TestRunner(IConnectivityTest connectivityTest, ILogger<TestRunner> logger)
    {
        _connectivityTest = connectivityTest;
        _logger = logger;
    }

    public async Task StartAsync(TestSet testSet, CancellationToken ct = default)
    {
        // Stop any previous run first (single active test set)
        Stop();

        // Wait for previous run task to complete
        if (_runTask is not null)
        {
            try { await _runTask; } catch { /* ignore */ }
        }

        _currentResults.Clear();
        _resultHistory.Clear();
        ActiveTestSetId = testSet.Id;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _logger.LogInformation("[TestRunner] Starting test set {Name} with {Count} targets",
            testSet.Name, testSet.Targets.Count);

        // Initialize all targets as Testing
        foreach (var target in testSet.Targets)
        {
            var initial = new TestResult
            {
                Timestamp = DateTimeOffset.Now,
                Status = TestStatus.Testing
            };
            _currentResults[target.Id] = initial;
            ResultUpdated?.Invoke(this, new TestResultUpdatedEventArgs
            {
                TargetId = target.Id,
                Result = initial
            });
        }

        var token = _cts.Token;
        _runTask = Task.Run(() => RunContinuousAsync(testSet, token), token);
    }

    public void Stop()
    {
        if (_cts is null) return;

        _logger.LogInformation("[TestRunner] Stopping test set {Id}", ActiveTestSetId);
        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
        ActiveTestSetId = null;
    }

    private async Task RunContinuousAsync(TestSet testSet, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Ping all targets concurrently
                var tasks = testSet.Targets.Select(target =>
                    ExecuteAndReportAsync(target, ct)).ToArray();

                await Task.WhenAll(tasks);

                // Wait ~1 second before next round
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TestRunner] Unexpected error in continuous test loop");
        }
    }

    private async Task ExecuteAndReportAsync(TestTarget target, CancellationToken ct)
    {
        var result = await _connectivityTest.ExecuteAsync(target, ct);

        _currentResults[target.Id] = result;

        // Maintain recent history
        var history = _resultHistory.GetOrAdd(target.Id, _ => new ConcurrentQueue<TestResult>());
        history.Enqueue(result);
        while (history.Count > MaxHistoryPerTarget)
            history.TryDequeue(out _);

        // Notify on arbitrary thread - UI must dispatch
        ResultUpdated?.Invoke(this, new TestResultUpdatedEventArgs
        {
            TargetId = target.Id,
            Result = result
        });
    }

    public void Dispose()
    {
        Stop();
        if (_runTask is not null)
        {
            try { _runTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        }
    }
}
