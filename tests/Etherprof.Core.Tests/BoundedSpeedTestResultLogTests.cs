namespace Etherprof.Core.Tests;

using Etherprof.Contracts.Models;
using Etherprof.Core;
using Xunit;

public class BoundedSpeedTestResultLogTests
{
    [Fact]
    public void Add_UnderCapacity_RetainsAllResultsInOrder()
    {
        var log = new BoundedSpeedTestResultLog();
        var now = DateTimeOffset.UtcNow;

        var res1 = new SpeedTestResult { StartedAt = now, Direction = SpeedTestDirection.Download, AverageMbps = 85.5 };
        var res2 = new SpeedTestResult { StartedAt = now.AddSeconds(10), Direction = SpeedTestDirection.Upload, AverageMbps = 22.0 };

        log.Add(res1);
        log.Add(res2);

        Assert.Equal(2, log.Count);
        var list = log.GetResults();
        Assert.Equal(res1, list[0]);
        Assert.Equal(res2, list[1]);
    }

    [Fact]
    public void Add_ExceedingCapacity_EvictsOldestResult()
    {
        var log = new BoundedSpeedTestResultLog();
        var baseTime = DateTimeOffset.UtcNow;

        for (int i = 0; i < 25; i++)
        {
            log.Add(new SpeedTestResult
            {
                StartedAt = baseTime.AddSeconds(i),
                Direction = SpeedTestDirection.Download,
                AverageMbps = i * 1.5
            });
        }

        Assert.Equal(BoundedSpeedTestResultLog.Capacity, log.Count);
        var list = log.GetResults();
        Assert.Equal(20, list.Count);

        // First item should be index 5 since 0-4 were evicted
        Assert.Equal(5 * 1.5, list[0].AverageMbps);
        Assert.Equal(24 * 1.5, list[19].AverageMbps);
    }

    [Fact]
    public void Clear_EmptiesLog()
    {
        var log = new BoundedSpeedTestResultLog();
        log.Add(new SpeedTestResult { AverageMbps = 50.0 });
        Assert.Equal(1, log.Count);

        log.Clear();
        Assert.Equal(0, log.Count);
        Assert.Empty(log.GetResults());
    }
}
