namespace Etherprof.StreamTest.Tests;

using Etherprof.StreamTest.Metrics;
using Etherprof.StreamTest.Models;
using Xunit;

public class BoundedStreamEventLogTests
{
    [Fact]
    public void Add_CapacityExceeded_EvictsOldest()
    {
        var log = new BoundedStreamEventLog(capacity: 3);

        log.Add(new StreamRuntimeEvent { Message = "Event 1" });
        log.Add(new StreamRuntimeEvent { Message = "Event 2" });
        log.Add(new StreamRuntimeEvent { Message = "Event 3" });
        log.Add(new StreamRuntimeEvent { Message = "Event 4" });

        var all = log.GetAll();
        Assert.Equal(3, all.Count);
        Assert.Equal("Event 2", all[0].Message);
        Assert.Equal("Event 3", all[1].Message);
        Assert.Equal("Event 4", all[2].Message);
    }
}
