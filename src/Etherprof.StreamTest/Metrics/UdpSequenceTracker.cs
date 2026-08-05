namespace Etherprof.StreamTest.Metrics;

using System.Diagnostics;

public sealed class UdpSequenceTracker
{
    private readonly int _reorderWindowSize;
    private long _uniquePacketsReceived;
    private long _duplicatePackets;
    private long _outOfOrderPackets;
    private long _finalizedPacketsLost;
    private long _highestSequence;
    private long _firstSequence = -1;
    private bool _firstPacketReceived;

    private readonly HashSet<long> _pendingMissingSequences = new();
    private readonly HashSet<long> _finalizedLostSequences = new();
    private long _lastPacketTimestampTicks;
    private long _longestGapTicks;
    private readonly object _lock = new();

    public UdpSequenceTracker(int reorderWindowSize = 128)
    {
        _reorderWindowSize = reorderWindowSize;
    }

    public long UniquePacketsReceived { get { lock (_lock) return _uniquePacketsReceived; } }
    public long DuplicatePackets { get { lock (_lock) return _duplicatePackets; } }
    public long OutOfOrderPackets { get { lock (_lock) return _outOfOrderPackets; } }
    public long FinalizedPacketsLost { get { lock (_lock) return _finalizedPacketsLost; } }

    public long ExpectedPackets
    {
        get
        {
            lock (_lock)
            {
                if (_firstSequence < 0 || _highestSequence < _firstSequence) return 0;
                return _highestSequence - _firstSequence + 1;
            }
        }
    }

    public double LossPercent
    {
        get
        {
            lock (_lock)
            {
                long expected = ExpectedPackets;
                if (expected <= 0) return 0.0;
                return Math.Clamp((double)_finalizedPacketsLost / expected * 100.0, 0.0, 100.0);
            }
        }
    }

    public TimeSpan LongestGap
    {
        get
        {
            lock (_lock)
            {
                return TimeSpan.FromSeconds((double)_longestGapTicks / Stopwatch.Frequency);
            }
        }
    }

    public void ProcessPacket(long sequence, long nowTicks = 0)
    {
        if (nowTicks == 0) nowTicks = Stopwatch.GetTimestamp();

        lock (_lock)
        {
            // LongestGap starts only after the first valid traffic packet is received
            if (_firstPacketReceived && _lastPacketTimestampTicks > 0)
            {
                long gapTicks = nowTicks - _lastPacketTimestampTicks;
                if (gapTicks > _longestGapTicks)
                {
                    _longestGapTicks = gapTicks;
                }
            }
            _lastPacketTimestampTicks = nowTicks;
            _firstPacketReceived = true;

            if (_firstSequence < 0)
            {
                _firstSequence = sequence;
                _highestSequence = sequence;
                _uniquePacketsReceived++;
                return;
            }

            if (sequence > _highestSequence)
            {
                // Add new gap to pending missing sequences
                for (long s = _highestSequence + 1; s < sequence; s++)
                {
                    _pendingMissingSequences.Add(s);
                }

                _highestSequence = sequence;
                _uniquePacketsReceived++;

                // Finalize pending missing sequences that have fallen behind the reorder window
                long minPendingSequence = sequence - _reorderWindowSize;
                var toFinalize = _pendingMissingSequences.Where(s => s < minPendingSequence).ToList();
                foreach (long lostSeq in toFinalize)
                {
                    _pendingMissingSequences.Remove(lostSeq);
                    _finalizedLostSequences.Add(lostSeq);
                    _finalizedPacketsLost++;
                }
            }
            else if (sequence < _highestSequence)
            {
                // Out of order packet
                if (_pendingMissingSequences.Remove(sequence))
                {
                    // Arrived within reorder window: recovered before finalization!
                    _uniquePacketsReceived++;
                    _outOfOrderPackets++;
                }
                else if (_finalizedLostSequences.Contains(sequence))
                {
                    // Arrived very late after finalization: increment out of order, do NOT decrease finalized loss!
                    _duplicatePackets++;
                    _outOfOrderPackets++;
                }
                else
                {
                    // Duplicate packet
                    _duplicatePackets++;
                }
            }
            else
            {
                // Duplicate packet equal to highest sequence
                _duplicatePackets++;
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _uniquePacketsReceived = 0;
            _duplicatePackets = 0;
            _outOfOrderPackets = 0;
            _finalizedPacketsLost = 0;
            _highestSequence = 0;
            _firstSequence = -1;
            _firstPacketReceived = false;
            _pendingMissingSequences.Clear();
            _finalizedLostSequences.Clear();
            _lastPacketTimestampTicks = 0;
            _longestGapTicks = 0;
        }
    }
}
