namespace Etherprof.Core;

using Etherprof.Contracts.Models;

public sealed class BoundedSpeedTestResultLog
{
    public const int Capacity = 20;
    private readonly LinkedList<SpeedTestResult> _results = new();
    private readonly object _lock = new();

    public void Add(SpeedTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_lock)
        {
            if (_results.Count >= Capacity)
            {
                _results.RemoveFirst();
            }
            _results.AddLast(result);
        }
    }

    public IReadOnlyList<SpeedTestResult> GetResults()
    {
        lock (_lock)
        {
            return _results.ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _results.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _results.Count;
            }
        }
    }
}
