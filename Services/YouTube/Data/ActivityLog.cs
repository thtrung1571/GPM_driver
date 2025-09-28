using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GPM_driver.Services.YouTube.Data;

internal sealed class ActivityLog
{
    private readonly ConcurrentQueue<string> _entries = new();

    internal void Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _entries.Enqueue(message);
        while (_entries.Count > 100)
        {
            _entries.TryDequeue(out _);
        }
    }

    internal IReadOnlyCollection<string> Snapshot()
    {
        return _entries.ToArray();
    }
}
