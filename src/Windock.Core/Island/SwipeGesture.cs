namespace Windock.Core.Island;

/// Turns a two-finger touchpad swipe (a stream of small horizontal wheel steps) or a mouse wheel spin into one
/// page turn. Steps add up until they reach a notch; then the rest of that gesture, including the touchpad's
/// coasting after the fingers lift, is ignored until the steps pause for GestureGap.
public sealed class SwipeGesture
{
    public const int Notch = 120;
    public static readonly TimeSpan GestureGap = TimeSpan.FromMilliseconds(350);

    private int _sum;
    private bool _used;
    private TimeSpan _last = TimeSpan.MinValue;

    /// delta: positive = towards the next page. Returns +1 / -1 when the gesture turns a page, else 0.
    public int Feed(int delta, TimeSpan now)
    {
        if (_last == TimeSpan.MinValue || now - _last > GestureGap)
        {
            _sum = 0;
            _used = false;
        }
        _last = now;
        if (_used || delta == 0) return 0;
        _sum += delta;
        if (Math.Abs(_sum) < Notch) return 0;
        _used = true;
        return Math.Sign(_sum);
    }
}
