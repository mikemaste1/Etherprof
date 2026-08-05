namespace Etherprof.StreamTest.Metrics;

public sealed class JitterCalculator
{
    private double _jitterMs;
    private long _lastSendMicroseconds;
    private long _lastReceiveMicroseconds;
    private bool _initialized;
    private readonly object _lock = new();

    public double CurrentJitterMs
    {
        get
        {
            lock (_lock)
            {
                return _jitterMs;
            }
        }
    }

    public void AddSample(long sendTimestampMicroseconds, long receiveTimestampMicroseconds)
    {
        lock (_lock)
        {
            if (!_initialized)
            {
                _lastSendMicroseconds = sendTimestampMicroseconds;
                _lastReceiveMicroseconds = receiveTimestampMicroseconds;
                _jitterMs = 0;
                _initialized = true;
                return;
            }

            long sendDeltaMicro = sendTimestampMicroseconds - _lastSendMicroseconds;
            long receiveDeltaMicro = receiveTimestampMicroseconds - _lastReceiveMicroseconds;

            _lastSendMicroseconds = sendTimestampMicroseconds;
            _lastReceiveMicroseconds = receiveTimestampMicroseconds;

            // Transit time difference in milliseconds
            double transitDiffMs = Math.Abs(receiveDeltaMicro - sendDeltaMicro) / 1000.0;

            // RFC 3550 jitter calculation formula: J = J + (|D| - J) / 16
            _jitterMs += (transitDiffMs - _jitterMs) / 16.0;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _jitterMs = 0;
            _lastSendMicroseconds = 0;
            _lastReceiveMicroseconds = 0;
            _initialized = false;
        }
    }
}
