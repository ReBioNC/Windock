namespace Windock.Core.Animation;

/// Time-based animation state for the dock: slot grow/shrink per item and magnification fade (DESIGN §3–4).
public sealed class DockAnimator
{
    public static readonly TimeSpan SlotDuration = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan MagnifyDuration = TimeSpan.FromMilliseconds(150);

    private readonly List<string> _order = new();
    private readonly Dictionary<string, Tween> _slots = new();
    private readonly HashSet<string> _ghosts = new();
    private Tween? _magnify;
    private bool _pointerInside;

    /// False when Windows "Show animations" is off: every transition completes instantly.
    public bool AnimationsEnabled { get; set; } = true;

    public void Reset(IReadOnlyList<string> keys)
    {
        _order.Clear();
        _order.AddRange(keys);
        _slots.Clear();
        _ghosts.Clear();
    }

    public void ApplyChanges(IReadOnlyList<string> keysInOrder, IReadOnlyCollection<string> added, IReadOnlyCollection<string> removed, TimeSpan now)
    {
        foreach (var key in removed)
        {
            if (!_order.Contains(key)) continue;
            _slots[key] = new Tween(SlotValue(key, now), 0, now, Duration(SlotDuration), Easing.InCubic);
            _ghosts.Add(key);
        }
        foreach (var key in added)
        {
            var from = _order.Contains(key) ? SlotValue(key, now) : 0;
            _slots[key] = new Tween(from, 1, now, Duration(SlotDuration), Easing.OutCubic);
            _ghosts.Remove(key);
        }

        var previous = _order.ToList();
        _order.Clear();
        _order.AddRange(keysInOrder);
        foreach (var ghost in previous.Where(_ghosts.Contains))
        {
            var index = previous.IndexOf(ghost);
            var leftNeighbour = index > 0 ? previous[index - 1] : null;
            var at = leftNeighbour is not null && _order.Contains(leftNeighbour)
                ? _order.IndexOf(leftNeighbour) + 1
                : Math.Min(index, _order.Count);
            _order.Insert(at, ghost);
        }
    }

    public void SetPointerInside(bool inside, TimeSpan now)
    {
        if (inside == _pointerInside) return;
        var from = MagnifyProgress(now);
        _pointerInside = inside;
        _magnify = new Tween(from, inside ? 1 : 0, now, Duration(MagnifyDuration), Easing.OutCubic);
    }

    public double MagnifyProgress(TimeSpan now) => _magnify?.ValueAt(now) ?? (_pointerInside ? 1 : 0);

    public IReadOnlyList<(string Key, double SlotProgress)> Slots(TimeSpan now)
    {
        foreach (var ghost in _ghosts.Where(g => _slots[g].IsComplete(now)).ToList())
        {
            _ghosts.Remove(ghost);
            _slots.Remove(ghost);
            _order.Remove(ghost);
        }
        return _order.Select(k => (k, SlotValue(k, now))).ToList();
    }

    public bool IsAnimating(TimeSpan now) =>
        (_magnify is not null && !_magnify.IsComplete(now)) || _slots.Values.Any(t => !t.IsComplete(now));

    private double SlotValue(string key, TimeSpan now) => _slots.TryGetValue(key, out var t) ? t.ValueAt(now) : 1;

    private TimeSpan Duration(TimeSpan d) => AnimationsEnabled ? d : TimeSpan.Zero;
}
