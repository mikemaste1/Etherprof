namespace Etherprof.Core;

using Etherprof.Contracts.Models;

public sealed class BoundedWifiEventLog
{
    public const int Capacity = 100;
    private readonly LinkedList<WifiEvent> _events = new();
    private readonly object _lock = new();

    public void Add(WifiEvent wifiEvent)
    {
        ArgumentNullException.ThrowIfNull(wifiEvent);

        lock (_lock)
        {
            if (_events.Count >= Capacity)
            {
                _events.RemoveFirst();
            }
            _events.AddLast(wifiEvent);
        }
    }

    public IReadOnlyList<WifiEvent> GetEvents()
    {
        lock (_lock)
        {
            return _events.ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _events.Count;
            }
        }
    }
}
