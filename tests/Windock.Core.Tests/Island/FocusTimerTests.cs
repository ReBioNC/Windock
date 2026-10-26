using Windock.Core.Island;

namespace Windock.Core.Tests.Island;

public class FocusTimerTests
{
    private static readonly DateTime T0 = new(2026, 10, 26, 9, 0, 0);
    private readonly FocusTimer _timer = new(() => new FocusDurations(25, 5, 15, 2));

    [Fact]
    public void Starts_stopped_at_the_full_focus_length()
    {
        Assert.False(_timer.Running);
        Assert.Equal(TimeSpan.FromMinutes(25), _timer.Remaining(T0));
        Assert.Equal(FocusPhase.Focus, _timer.Phase);
        Assert.Equal(1, _timer.Session);
    }

    [Fact]
    public void Counts_down_and_pauses()
    {
        _timer.Start(T0);
        Assert.Equal(TimeSpan.FromMinutes(15), _timer.Remaining(T0.AddMinutes(10)));
        Assert.Equal(0.4, _timer.Progress(T0.AddMinutes(10)), 3);

        _timer.Pause(T0.AddMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(15), _timer.Remaining(T0.AddHours(1)));
        _timer.Start(T0.AddHours(1));
        Assert.Equal(TimeSpan.FromMinutes(5), _timer.Remaining(T0.AddHours(1).AddMinutes(10)));
    }

    [Fact]
    public void A_finished_session_waits_for_its_break()
    {
        _timer.Start(T0);

        Assert.Null(_timer.Tick(T0.AddMinutes(24)));
        Assert.Equal(FocusPhase.Focus, _timer.Tick(T0.AddMinutes(25)));
        Assert.Equal(FocusPhase.ShortBreak, _timer.Phase);
        Assert.False(_timer.Running);
        Assert.Equal(TimeSpan.FromMinutes(5), _timer.Remaining(T0.AddMinutes(26)));
    }

    [Fact]
    public void The_last_session_of_a_round_gets_the_long_break_then_the_round_starts_over()
    {
        _timer.BreakOrSkip(T0);           // session 1 -> short break, running
        _timer.Tick(T0.AddMinutes(5));    // break over -> session 2
        Assert.Equal(2, _timer.Session);
        Assert.Equal(FocusPhase.LongBreak, _timer.NextBreak);

        _timer.Start(T0.AddMinutes(5));
        _timer.Tick(T0.AddMinutes(30));
        Assert.Equal(FocusPhase.LongBreak, _timer.Phase);
        _timer.BreakOrSkip(T0.AddMinutes(30));  // skip the long break
        Assert.Equal(1, _timer.Session);
        Assert.Equal(FocusPhase.Focus, _timer.Phase);
    }

    [Fact]
    public void Reset_goes_back_to_the_first_session()
    {
        _timer.BreakOrSkip(T0);
        _timer.Reset();

        Assert.Equal((FocusPhase.Focus, 1, false), (_timer.Phase, _timer.Session, _timer.Running));
        Assert.Equal(TimeSpan.FromMinutes(25), _timer.Remaining(T0));
    }

    [Fact]
    public void A_new_length_applies_to_a_phase_that_has_not_started()
    {
        var minutes = 25;
        var timer = new FocusTimer(() => new FocusDurations(minutes, 5, 15, 4));
        minutes = 50;
        timer.DurationsChanged();

        Assert.Equal(TimeSpan.FromMinutes(50), timer.Remaining(T0));
    }
}
