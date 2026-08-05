namespace Etherprof.StreamTest.Metrics;

using Etherprof.StreamTest.Models;

public sealed class BoundedStreamEventLog
{
    private readonly int _capacity;
    private readonly LinkedList<StreamRuntimeEvent> _events = new();
    private readonly object _lock = new();

    public BoundedStreamEventLog(int capacity = 100)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public void Add(StreamRuntimeEvent evt)
    {
        lock (_lock)
        {
            if (_events.Count >= _capacity)
            {
                _events.RemoveFirst();
            }
            _events.AddLast(evt);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }

    public IReadOnlyList<StreamRuntimeEvent> GetAll()
    {
        lock (_lock)
        {
            return _events.ToList();
        }
    }
}
