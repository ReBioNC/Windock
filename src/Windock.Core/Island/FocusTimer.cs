namespace Windock.Core.Island;

public enum FocusPhase { Focus, ShortBreak, LongBreak }

public sealed record FocusDurations(int FocusMinutes, int ShortBreakMinutes, int LongBreakMinutes, int Sessions);

/// A pomodoro timer: focus, then a short break (a long one after the last session of a round). When a phase ends
/// the next one waits for Start, so a break never begins on its own. Time is passed in, so tests drive it.
public sealed class FocusTimer
{
    private readonly Func<FocusDurations> _durations;
    private TimeSpan _remaining;
    private DateTime _endsAt;

    public FocusTimer(Func<FocusDurations> durations)
    {
        _durations = durations;
        _remaining = Length;
    }

    public FocusPhase Phase { get; private set; } = FocusPhase.Focus;

    /// 1-based session of the round; a round has FocusDurations.Sessions sessions.
    public int Session { get; private set; } = 1;

    public bool Running { get; private set; }

    public int Sessions => Math.Max(1, _durations().Sessions);

    /// The full length of the current phase.
    public TimeSpan Length
    {
        get
        {
            var d = _durations();
            return TimeSpan.FromMinutes(Phase switch
            {
                FocusPhase.ShortBreak => d.ShortBreakMinutes,
                FocusPhase.LongBreak => d.LongBreakMinutes,
                _ => d.FocusMinutes,
            });
        }
    }

    /// The break that follows the current focus session.
    public FocusPhase NextBreak => Session >= Sessions ? FocusPhase.LongBreak : FocusPhase.ShortBreak;

    public TimeSpan Remaining(DateTime now)
    {
        if (!Running) return _remaining;
        var left = _endsAt - now;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// 0 at the start of the phase, 1 at its end.
    public double Progress(DateTime now) => Length > TimeSpan.Zero ? Math.Clamp(1 - Remaining(now) / Length, 0, 1) : 0;

    public void Start(DateTime now)
    {
        if (Running) return;
        _endsAt = now + _remaining;
        Running = true;
    }

    public void Pause(DateTime now)
    {
        if (!Running) return;
        _remaining = Remaining(now);
        Running = false;
    }

    /// Back to the first focus session, stopped.
    public void Reset()
    {
        Running = false;
        Phase = FocusPhase.Focus;
        Session = 1;
        _remaining = Length;
    }

    /// From a focus session: start its break now. From a break: skip it and get the next session ready.
    public void BreakOrSkip(DateTime now)
    {
        if (Phase == FocusPhase.Focus)
        {
            Enter(NextBreak);
            Start(now);
        }
        else
        {
            FinishBreak();
        }
    }

    /// Settings changed: a phase that has not started yet takes the new length.
    public void DurationsChanged()
    {
        if (!Running) _remaining = Length;
    }

    /// Call every second while running. Returns the phase that just ended, or null.
    public FocusPhase? Tick(DateTime now)
    {
        if (!Running || now < _endsAt) return null;
        var ended = Phase;
        if (ended == FocusPhase.Focus) Enter(NextBreak);
        else FinishBreak();
        return ended;
    }

    private void FinishBreak()
    {
        Session = Phase == FocusPhase.LongBreak ? 1 : Session + 1;
        Enter(FocusPhase.Focus);
    }

    private void Enter(FocusPhase phase)
    {
        Running = false;
        Phase = phase;
        _remaining = Length;
    }
}
