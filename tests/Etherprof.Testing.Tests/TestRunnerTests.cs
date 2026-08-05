using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Etherprof.Testing.Tests;

public class TestRunnerTests
{
    private sealed class MockConnectivityTest : IConnectivityTest
    {
        public string Type => "Mock";
        public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(10);
        public TestStatus ResultStatus { get; set; } = TestStatus.Success;

        public async Task<TestResult> ExecuteAsync(TestTarget target, CancellationToken ct = default)
        {
            await Task.Delay(Delay, ct);
            return new TestResult
            {
                Timestamp = DateTimeOffset.Now,
                Status = ResultStatus,
                Latency = ResultStatus == TestStatus.Success ? Delay : null,
                Error = ResultStatus == TestStatus.Failure ? "Mock failure" : null
            };
        }
    }

    private static TestSet CreateTestSet(int targetCount = 3)
    {
        return new TestSet
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Targets = Enumerable.Range(0, targetCount).Select(i => new TestTarget
            {
                Id = Guid.NewGuid(),
                Name = $"Target{i}",
                Host = $"10.0.0.{i + 1}"
            }).ToList()
        };
    }

    [Fact]
    public async Task Start_SetsIsRunning()
    {
        var mock = new MockConnectivityTest();
        var runner = new TestRunner(mock, NullLogger<TestRunner>.Instance);
        var testSet = CreateTestSet();

        await runner.StartAsync(testSet);
        Assert.True(runner.IsRunning);
        Assert.Equal(testSet.Id, runner.ActiveTestSetId);

        runner.Stop();
        runner.Dispose();
    }

    [Fact]
    public async Task Stop_ClearsIsRunning()
    {
        var mock = new MockConnectivityTest();
        var runner = new TestRunner(mock, NullLogger<TestRunner>.Instance);
        var testSet = CreateTestSet();

        await runner.StartAsync(testSet);
        runner.Stop();

        // Give a moment for the task to complete
        await Task.Delay(100);

        Assert.False(runner.IsRunning);
        Assert.Null(runner.ActiveTestSetId);

        runner.Dispose();
    }

    [Fact]
    public async Task Start_NewTestSet_StopsPrevious()
    {
        var mock = new MockConnectivityTest { Delay = TimeSpan.FromMilliseconds(50) };
        var runner = new TestRunner(mock, NullLogger<TestRunner>.Instance);
        var testSet1 = CreateTestSet();
        var testSet2 = CreateTestSet();

        await runner.StartAsync(testSet1);
        Assert.Equal(testSet1.Id, runner.ActiveTestSetId);

        await runner.StartAsync(testSet2);
        Assert.Equal(testSet2.Id, runner.ActiveTestSetId);

        runner.Stop();
        runner.Dispose();
    }

    [Fact]
    public async Task ResultUpdated_FiredForEachTarget()
    {
        var mock = new MockConnectivityTest { Delay = TimeSpan.FromMilliseconds(5) };
        var runner = new TestRunner(mock, NullLogger<TestRunner>.Instance);
        var testSet = CreateTestSet(3);
        var updatedTargets = new HashSet<Guid>();

        runner.ResultUpdated += (_, args) => updatedTargets.Add(args.TargetId);

        await runner.StartAsync(testSet);
        await Task.Delay(500); // let it run a round

        // All 3 targets should have had results
        foreach (var target in testSet.Targets)
        {
            Assert.Contains(target.Id, updatedTargets);
        }

        runner.Stop();
        runner.Dispose();
    }

    [Fact]
    public async Task CurrentResults_ContainsLatestResults()
    {
        var mock = new MockConnectivityTest { Delay = TimeSpan.FromMilliseconds(5) };
        var runner = new TestRunner(mock, NullLogger<TestRunner>.Instance);
        var testSet = CreateTestSet(2);

        await runner.StartAsync(testSet);
        await Task.Delay(500);

        Assert.Equal(2, runner.CurrentResults.Count);
        foreach (var target in testSet.Targets)
        {
            Assert.True(runner.CurrentResults.ContainsKey(target.Id));
        }

        runner.Stop();
        runner.Dispose();
    }
}
