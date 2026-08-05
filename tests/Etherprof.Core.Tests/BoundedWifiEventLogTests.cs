namespace Etherprof.Core.Tests;

using Etherprof.Contracts.Models;
using Etherprof.Core;
using Xunit;

public class BoundedWifiEventLogTests
{
    [Fact]
    public void Add_UnderCapacity_RetainsAllEventsInOrder()
    {
        var log = new BoundedWifiEventLog();
        var now = DateTimeOffset.UtcNow;

        var ev1 = new WifiEvent { Timestamp = now, Type = WifiEventType.Connected, CurrentSsid = "Net1" };
        var ev2 = new WifiEvent { Timestamp = now.AddSeconds(1), Type = WifiEventType.Roam, CurrentSsid = "Net1" };

        log.Add(ev1);
        log.Add(ev2);

        Assert.Equal(2, log.Count);
        var events = log.GetEvents();
        Assert.Equal(ev1, events[0]);
        Assert.Equal(ev2, events[1]);
    }

    [Fact]
    public void Add_ExceedingCapacity_EvictsOldestEvent()
    {
        var log = new BoundedWifiEventLog();
        var baseTime = DateTimeOffset.UtcNow;

        for (int i = 0; i < 105; i++)
        {
            log.Add(new WifiEvent
            {
                Timestamp = baseTime.AddSeconds(i),
                Type = WifiEventType.Roam,
                CurrentSsid = $"Net_{i}"
            });
        }

        Assert.Equal(BoundedWifiEventLog.Capacity, log.Count);
        var events = log.GetEvents();
        Assert.Equal(100, events.Count);

        // First item should be index 5 ("Net_5") since index 0-4 were evicted
        Assert.Equal("Net_5", events[0].CurrentSsid);
        Assert.Equal("Net_104", events[99].CurrentSsid);
    }

    [Fact]
    public void Clear_EmptiesLog()
    {
        var log = new BoundedWifiEventLog();
        log.Add(new WifiEvent { Type = WifiEventType.Connected });
        Assert.Equal(1, log.Count);

        log.Clear();
        Assert.Equal(0, log.Count);
        Assert.Empty(log.GetEvents());
    }
}
