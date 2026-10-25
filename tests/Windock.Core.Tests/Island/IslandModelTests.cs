using Windock.Core.Island;
using Windock.Core.Tests.Fakes;

namespace Windock.Core.Tests.Island;

public class IslandModelTests
{
    private readonly ManualDelayScheduler _scheduler = new();
    private readonly IslandModel _model;

    public IslandModelTests() => _model = new IslandModel(_scheduler, () => TimeSpan.FromSeconds(2));

    private static IslandEvent Volume(double level) => new(IslandEventKind.Volume, "v", "Volume", level);
    private static IslandEvent Device(string text) => new(IslandEventKind.Device, "d", text);

    [Fact]
    public void Starts_idle_and_collapsed()
    {
        Assert.Equal(IslandMode.Idle, _model.Mode);
        Assert.False(_model.Expanded);
    }

    [Fact]
    public void Playing_media_shows_the_media_card()
    {
        _model.SetMedia(new MediaState("Song", "Artist", "Spotify", Playing: true));

        Assert.Equal(IslandMode.Media, _model.Mode);
    }

    [Fact]
    public void Paused_media_falls_back_to_idle_after_the_linger_time()
    {
        _model.SetMedia(new MediaState("Song", "Artist", "Spotify", Playing: true));
        _model.SetMedia(new MediaState("Song", "Artist", "Spotify", Playing: false));
        Assert.Equal(IslandMode.Media, _model.Mode);

        var linger = Assert.Single(_scheduler.Active);
        Assert.Equal(IslandModel.PausedLinger, linger.Delay);
        _scheduler.RunAll();

        Assert.Equal(IslandMode.Idle, _model.Mode);
    }

    [Fact]
    public void Playing_again_brings_the_media_card_back()
    {
        _model.SetMedia(new MediaState("Song", "Artist", "Spotify", Playing: false));
        _scheduler.RunAll();
        _model.SetMedia(new MediaState("Song", "Artist", "Spotify", Playing: true));

        Assert.Equal(IslandMode.Media, _model.Mode);
    }

    [Fact]
    public void An_event_shows_for_the_event_duration_then_ends()
    {
        _model.Show(Device("Mouse connected"));

        Assert.Equal(IslandMode.Event, _model.Mode);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(_scheduler.Active).Delay);
        _scheduler.RunAll();
        Assert.Equal(IslandMode.Idle, _model.Mode);
        Assert.Null(_model.CurrentEvent);
    }

    [Fact]
    public void Volume_replaces_volume_at_once()
    {
        _model.Show(Volume(0.4));
        _model.Show(Volume(0.5));

        Assert.Equal(0.5, _model.CurrentEvent?.Level);
        Assert.Empty(_model.Queued);
        Assert.Single(_scheduler.Active);  // the first timer was cancelled
    }

    [Fact]
    public void Other_events_wait_their_turn()
    {
        _model.Show(Device("Mouse connected"));
        _model.Show(Device("Keyboard connected"));

        Assert.Equal("Mouse connected", _model.CurrentEvent?.Text);
        _scheduler.RunAll();
        Assert.Equal("Keyboard connected", _model.CurrentEvent?.Text);
    }

    [Fact]
    public void Volume_interrupts_a_device_event_which_shows_again_afterwards()
    {
        _model.Show(Device("Mouse connected"));
        _model.Show(Volume(0.3));

        Assert.Equal(IslandEventKind.Volume, _model.CurrentEvent?.Kind);
        _scheduler.RunAll();
        Assert.Equal("Mouse connected", _model.CurrentEvent?.Text);
    }

    [Fact]
    public void The_queue_is_short()
    {
        _model.Show(Device("first"));
        for (var i = 0; i < 10; i++) _model.Show(Device($"queued {i}"));

        Assert.Equal(IslandModel.MaxQueued, _model.Queued.Count);
    }

    [Fact]
    public void Hover_expands_and_leaving_collapses_after_a_delay()
    {
        _model.PointerEntered(expandAllowed: true);
        Assert.True(_model.Expanded);

        _model.PointerLeft();
        Assert.True(_model.Expanded);
        Assert.Equal(IslandModel.CollapseDelay, Assert.Single(_scheduler.Active).Delay);
        _scheduler.RunAll();
        Assert.False(_model.Expanded);
    }

    [Fact]
    public void Coming_back_before_the_delay_keeps_it_expanded()
    {
        _model.PointerEntered(true);
        _model.PointerLeft();
        _model.PointerEntered(true);

        Assert.Empty(_scheduler.Active);
        Assert.True(_model.Expanded);
    }

    [Fact]
    public void Hover_does_not_expand_when_the_setting_is_off_or_an_event_shows()
    {
        _model.PointerEntered(expandAllowed: false);
        Assert.False(_model.Expanded);

        _model.PointerLeft();
        _model.Show(Device("Mouse connected"));
        _model.PointerEntered(expandAllowed: true);
        Assert.False(_model.Expanded);
    }

    [Fact]
    public void While_expanded_volume_is_dropped_and_other_events_wait_for_the_collapse()
    {
        _model.PointerEntered(true);
        _model.Show(Volume(0.2));
        _model.Show(Device("Mouse connected"));

        Assert.Null(_model.CurrentEvent);
        Assert.Single(_model.Queued);

        _model.Collapse();
        Assert.Equal("Mouse connected", _model.CurrentEvent?.Text);
    }

    [Fact]
    public void Clearing_one_kind_keeps_the_rest()
    {
        _model.Show(Device("Mouse connected"));
        _model.Show(new IslandEvent(IslandEventKind.Battery, "b", "Charging"));

        _model.ClearEvents(e => e.Kind == IslandEventKind.Device);

        Assert.Equal("Charging", _model.CurrentEvent?.Text);
        _model.ClearEvents();
        Assert.Null(_model.CurrentEvent);
        Assert.Empty(_model.Queued);
    }
}
