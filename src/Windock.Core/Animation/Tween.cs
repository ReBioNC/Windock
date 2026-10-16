namespace Windock.Core.Animation;

public sealed class Tween(double from, double to, TimeSpan start, TimeSpan duration, Func<double, double> easing)
{
    public double To { get; } = to;

    public double ValueAt(TimeSpan now)
    {
        if (IsComplete(now)) return To;
        if (now <= start) return from;
        var t = (now - start).TotalMilliseconds / duration.TotalMilliseconds;
        return from + (To - from) * easing(t);
    }

    public bool IsComplete(TimeSpan now) => duration <= TimeSpan.Zero || now >= start + duration;
}
