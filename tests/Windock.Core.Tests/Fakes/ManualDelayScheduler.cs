using Windock.Core.Abstractions;

namespace Windock.Core.Tests.Fakes;

/// Records scheduled actions; tests run them explicitly.
internal sealed class ManualDelayScheduler : IDelayScheduler
{
    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Active => _entries.Where(e => !e.Disposed).ToList();

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var entry = new Entry(delay, action);
        _entries.Add(entry);
        return entry;
    }

    public void RunAll()
    {
        foreach (var e in Active) { e.Dispose(); e.Action(); }
    }

    internal sealed class Entry(TimeSpan delay, Action action) : IDisposable
    {
        public TimeSpan Delay { get; } = delay;
        public Action Action { get; } = action;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
