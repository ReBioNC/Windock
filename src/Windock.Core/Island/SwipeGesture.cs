namespace Windock.Core.Island;

/// One step of a swipe: Turn is +1 / -1 when the swipe turns a page (else 0); Drag (-1..1) is how far the
/// unfinished swipe has come, so the card can follow the fingers before it turns.
public readonly record struct SwipeStep(int Turn, double Drag);

/// Turns a two-finger touchpad swipe (a stream of small horizontal wheel steps) or a mouse wheel spin into one
/// page turn. Steps add up until they reach Threshold; then the rest of that gesture, including the touchpad's
/// coasting after the fingers lift, is ignored until the steps pause for GestureGap (a new touch stops the
/// coasting, so a second swipe starts after such a pause) or go the other way.
public sealed class SwipeGesture
{
    /// Half a mouse wheel notch: a short flick on the touchpad is enough, one wheel click still turns at once.
    public const int Threshold = 60;
    public static readonly TimeSpan GestureGap = TimeSpan.FromMilliseconds(120);

    private int _sum;
    private int _usedSign;
    private TimeSpan _last = TimeSpan.MinValue;

    /// delta: positive = towards the next page.
    public SwipeStep Feed(int delta, TimeSpan now)
    {
        if (_last == TimeSpan.MinValue || now - _last > GestureGap) Reset();
        _last = now;
        if (delta == 0) return new SwipeStep(0, Drag());
        if (_usedSign != 0)
        {
            if (Math.Sign(delta) == _usedSign) return new SwipeStep(0, 0);  // coasting after the turn
            Reset();  // the fingers went back the other way: a new swipe
        }
        _sum += delta;
        if (Math.Abs(_sum) < Threshold) return new SwipeStep(0, Drag());
        _usedSign = Math.Sign(_sum);
        _sum = 0;
        return new SwipeStep(_usedSign, 0);
    }

    /// The fingers stopped without finishing the swipe: the card goes back.
    public void Reset()
    {
        _sum = 0;
        _usedSign = 0;
    }

    private double Drag() => Math.Clamp((double)_sum / Threshold, -1, 1);
}
