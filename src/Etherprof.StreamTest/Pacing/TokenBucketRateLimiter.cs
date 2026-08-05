namespace Etherprof.StreamTest.Pacing;

using System.Diagnostics;

public sealed class TokenBucketRateLimiter
{
    private readonly long _targetBitsPerSecond;
    private readonly double _bytesPerTimestampTick;
    private double _availableBytes;
    private long _lastTimestamp;
    private double _maxBucketSizeBytes;
    private readonly object _lock = new();

    public TokenBucketRateLimiter(long targetBitsPerSecond)
    {
        if (targetBitsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetBitsPerSecond));

        _targetBitsPerSecond = targetBitsPerSecond;
        double targetBytesPerSec = targetBitsPerSecond / 8.0;
        _bytesPerTimestampTick = targetBytesPerSec / Stopwatch.Frequency;

        // Bucket size capacity: allow up to ~250ms burst capacity (minimum 64 KiB) to fit full DATA frame sizes
        _maxBucketSizeBytes = Math.Max(65536.0, targetBytesPerSec * 0.250);
        _availableBytes = _maxBucketSizeBytes;
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    public long TargetBitsPerSecond => _targetBitsPerSecond;

    public async Task PaceAsync(int bytesToSend, CancellationToken cancellationToken = default)
    {
        if (bytesToSend <= 0) return;

        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan delayNeeded;

            lock (_lock)
            {
                // Ensure bucket capacity is at least large enough to fit bytesToSend
                if (bytesToSend > _maxBucketSizeBytes)
                {
                    _maxBucketSizeBytes = bytesToSend * 1.5;
                }

                long now = Stopwatch.GetTimestamp();
                long elapsedTicks = now - _lastTimestamp;
                _lastTimestamp = now;

                _availableBytes = Math.Min(_maxBucketSizeBytes, _availableBytes + (elapsedTicks * _bytesPerTimestampTick));

                if (_availableBytes >= bytesToSend)
                {
                    _availableBytes -= bytesToSend;
                    return;
                }

                double missingBytes = bytesToSend - _availableBytes;
                double secondsToWait = missingBytes / (_targetBitsPerSecond / 8.0);
                delayNeeded = TimeSpan.FromSeconds(secondsToWait);
            }

            if (delayNeeded > TimeSpan.Zero)
            {
                if (delayNeeded.TotalMilliseconds > 1)
                {
                    await Task.Delay(delayNeeded, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Short sub-millisecond wait
                    await Task.Yield();
                }
            }
        }
    }
}
