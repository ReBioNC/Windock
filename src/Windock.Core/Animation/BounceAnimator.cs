namespace Windock.Core.Animation;

/// Icon bounce for launching apps and windows asking for attention (DESIGN §4): up 300 ms (ease-out),
/// down 300 ms (ease-in), rest 100 ms, repeat. A stopped bounce lands first instead of freezing mid-air.
public sealed class BounceAnimator
{
    public static readonly TimeSpan Rise = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan Fall = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan Rest = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Cycle = Rise + Fall + Rest;
    public static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(10);

    /// Peak lift as a fraction of the icon size.
    public const double HeightFactor = 0.5;

    private readonly Dictionary<string, (TimeSpan Start, TimeSpan End)> _bounces = new();

    public bool AnimationsEnabled { get; set; } = true;

    /// Starts bouncing (or keeps an ongoing bounce going) for at most `limit`, rounded up to whole bounces.
    public void Start(string key, TimeSpan now, TimeSpan limit)
    {
        if (!AnimationsEnabled || limit <= TimeSpan.Zero) return;
        var start = _bounces.TryGetValue(key, out var b) && now < b.End ? b.Start : now;
        _bounces[key] = (start, Landing(start, now + limit));
    }

    /// Stops after the current bounce lands.
    public void Stop(string key, TimeSpan now)
    {
        if (!_bounces.TryGetValue(key, out var b)) return;
        if (now >= b.End) { _bounces.Remove(key); return; }
        _bounces[key] = (b.Start, Landing(b.Start, now));
    }

    public bool IsBouncing(string key, TimeSpan now) => _bounces.TryGetValue(key, out var b) && now < b.End;

    public bool IsAnimating(TimeSpan now)
    {
        foreach (var key in _bounces.Where(p => now >= p.Value.End).Select(p => p.Key).ToList()) _bounces.Remove(key);
        return _bounces.Count > 0;
    }

    /// Current lift in 0..1 (multiply by HeightFactor × icon size).
    public double Lift(string key, TimeSpan now)
    {
        if (!_bounces.TryGetValue(key, out var b) || now >= b.End || now < b.Start) return 0;
        var t = TimeSpan.FromTicks((now - b.Start).Ticks % Cycle.Ticks);
        if (t < Rise) return Easing.OutQuad(t / Rise);
        if (t < Rise + Fall) return 1 - Easing.InQuad((t - Rise) / Fall);
        return 0;
    }

    /// The first moment at or after `at` when the icon is on the ground (end of a fall, or `at` itself while resting).
    private static TimeSpan Landing(TimeSpan start, TimeSpan at)
    {
        var elapsed = at - start;
        var cycles = elapsed.Ticks / Cycle.Ticks;
        var inCycle = TimeSpan.FromTicks(elapsed.Ticks % Cycle.Ticks);
        if (inCycle == TimeSpan.Zero) return at;
        if (inCycle >= Rise + Fall) return at;
        return start + TimeSpan.FromTicks(cycles * Cycle.Ticks) + Rise + Fall;
    }
}
