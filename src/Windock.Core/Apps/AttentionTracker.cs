using Windock.Core.Settings;

namespace Windock.Core.Apps;

/// Dock icons whose window flashed for attention, until that app is opened (spec v0.6 §2).
/// Bouncing comes in short bursts so an ignored request does not keep the render loop busy.
public sealed class AttentionTracker
{
    public const int BounceBurstCycles = 3;
    public static readonly TimeSpan BurstInterval = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, TimeSpan?> _lastBurst = new();  // key -> start of its latest bounce burst

    public IReadOnlyCollection<string> Keys => _lastBurst.Keys;

    public bool Has(string key) => _lastBurst.ContainsKey(key);

    /// Marks an icon; returns false when it was already marked.
    public bool Request(string key)
    {
        if (_lastBurst.ContainsKey(key)) return false;
        _lastBurst[key] = null;
        return true;
    }

    public bool Clear(string key) => _lastBurst.Remove(key);

    /// Keeps only keys that are still in the dock.
    public void Retain(IEnumerable<string> present)
    {
        var keep = present.ToHashSet();
        foreach (var key in _lastBurst.Keys.Where(k => !keep.Contains(k)).ToList()) _lastBurst.Remove(key);
    }

    /// Icons that should start a bounce burst now. Once: a single burst per request; UntilOpened: a burst every BurstInterval.
    public IReadOnlyList<string> DueBursts(TimeSpan now, AttentionRepeat repeat)
    {
        var due = new List<string>();
        foreach (var (key, last) in _lastBurst.ToList())
        {
            var isDue = last is null || (repeat == AttentionRepeat.UntilOpened && now - last.Value >= BurstInterval);
            if (!isDue) continue;
            _lastBurst[key] = now;
            due.Add(key);
        }
        return due;
    }

    /// Whether a later burst can still come (decides if the burst timer keeps running).
    public bool HasPendingBursts(AttentionRepeat repeat) =>
        _lastBurst.Count > 0 && (repeat == AttentionRepeat.UntilOpened || _lastBurst.Values.Any(v => v is null));
}
