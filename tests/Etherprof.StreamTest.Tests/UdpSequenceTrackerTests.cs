namespace Etherprof.StreamTest.Tests;

using Etherprof.StreamTest.Metrics;
using Xunit;

public class UdpSequenceTrackerTests
{
    [Fact]
    public void SequenceTracker_InOrderPackets_ZeroLoss()
    {
        var tracker = new UdpSequenceTracker(reorderWindowSize: 10);
        for (int i = 1; i <= 100; i++)
        {
            tracker.ProcessPacket(i);
        }

        Assert.Equal(100, tracker.UniquePacketsReceived);
        Assert.Equal(100, tracker.ExpectedPackets);
        Assert.Equal(0, tracker.FinalizedPacketsLost);
        Assert.Equal(0.0, tracker.LossPercent);
    }

    [Fact]
    public void SequenceTracker_FinalizedLossWindow_FinalizesOlderLosses()
    {
        var tracker = new UdpSequenceTracker(reorderWindowSize: 5);
        tracker.ProcessPacket(1);
        tracker.ProcessPacket(2);
        // Packets 3, 4, 5 missing.
        // Packet 15 arrives, which is 10 steps ahead (> reorderWindowSize 5)
        tracker.ProcessPacket(15);

        Assert.Equal(3, tracker.UniquePacketsReceived);
        Assert.Equal(15, tracker.ExpectedPackets);
        // Packets 3, 4, 5, 6, 7, 8, 9 fall outside window 15 - 5 = 10 -> finalized as lost
        Assert.True(tracker.FinalizedPacketsLost > 0);
    }

    [Fact]
    public void SequenceTracker_VeryLatePacket_DoesNotDecreaseFinalizedLoss()
    {
        var tracker = new UdpSequenceTracker(reorderWindowSize: 5);
        tracker.ProcessPacket(1);
        tracker.ProcessPacket(15); // Finalizes 2..9 as lost

        long initialFinalizedLoss = tracker.FinalizedPacketsLost;

        // Packet 3 arrives very late after finalization
        tracker.ProcessPacket(3);

        Assert.Equal(initialFinalizedLoss, tracker.FinalizedPacketsLost);
        Assert.True(tracker.OutOfOrderPackets > 0);
    }

    [Fact]
    public void SequenceTracker_GatedLongestGap_DoesNotTrackStartupDelay()
    {
        var tracker = new UdpSequenceTracker();
        Assert.Equal(TimeSpan.Zero, tracker.LongestGap);

        // Before first packet, gaps are not tracked
        tracker.ProcessPacket(1);
        Assert.Equal(TimeSpan.Zero, tracker.LongestGap);
    }
}
