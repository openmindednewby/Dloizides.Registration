using System.Collections.Concurrent;

namespace Dloizides.Registration.Screening;

internal sealed class FixedWindowCounter(TimeSpan window, int limit)
{
    private Window? _current;

    public bool TryHit(string key, DateTimeOffset now)
    {
        var active = Volatile.Read(ref _current);
        if (active is null || now >= active.Ends)
        {
            var fresh = new Window(now.Add(window));
            active = Interlocked.CompareExchange(ref _current, fresh, active) == active ? fresh : Volatile.Read(ref _current)!;
        }

        return active.Counts.AddOrUpdate(key, 1, (_, count) => count + 1) <= limit;
    }

    private sealed class Window(DateTimeOffset ends)
    {
        public DateTimeOffset Ends { get; } = ends;

        public ConcurrentDictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
    }
}
