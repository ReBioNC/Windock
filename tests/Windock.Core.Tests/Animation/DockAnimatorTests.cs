using Windock.Core.Animation;

namespace Windock.Core.Tests.Animation;

public class DockAnimatorTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private static DockAnimator Started(params string[] keys)
    {
        var a = new DockAnimator();
        a.Reset(keys);
        return a;
    }

    private static double Slot(DockAnimator a, string key, double ms) => a.Slots(Ms(ms)).Single(s => s.Key == key).SlotProgress;

    [Fact]
    public void Reset_items_are_fully_visible_without_animation()
    {
        var a = Started("a", "b");

        Assert.All(a.Slots(Ms(0)), s => Assert.Equal(1, s.SlotProgress));
        Assert.False(a.IsAnimating(Ms(0)));
    }

    [Fact]
    public void Added_item_grows_over_200ms()
    {
        var a = Started("a", "b");

        a.ApplyChanges(new[] { "a", "b", "c" }, new[] { "c" }, Array.Empty<string>(), Ms(1000));

        Assert.Equal(new[] { "a", "b", "c" }, a.Slots(Ms(1000)).Select(s => s.Key));
        Assert.Equal(0, Slot(a, "c", 1000));
        Assert.Equal(1, Slot(a, "c", 1200));
        Assert.True(a.IsAnimating(Ms(1100)));
        Assert.False(a.IsAnimating(Ms(1201)));
    }

    [Fact]
    public void Removed_item_stays_as_ghost_at_old_index_until_exit_completes()
    {
        var a = Started("a", "b", "c");

        a.ApplyChanges(new[] { "a", "c" }, Array.Empty<string>(), new[] { "b" }, Ms(0));

        Assert.Equal(new[] { "a", "b", "c" }, a.Slots(Ms(100)).Select(s => s.Key));
        Assert.Equal(0.875, Slot(a, "b", 100), 6);
        Assert.Equal(new[] { "a", "c" }, a.Slots(Ms(201)).Select(s => s.Key));
        Assert.False(a.IsAnimating(Ms(201)));
    }

    [Fact]
    public void Ghost_whose_left_neighbour_also_left_keeps_numeric_index()
    {
        var a = Started("a", "b", "c", "d");

        a.ApplyChanges(new[] { "a", "d" }, Array.Empty<string>(), new[] { "b", "c" }, Ms(0));

        Assert.Equal(new[] { "a", "b", "c", "d" }, a.Slots(Ms(50)).Select(s => s.Key));
    }

    [Fact]
    public void Pointer_enter_and_exit_drive_magnify_progress()
    {
        var a = Started("a");

        a.SetPointerInside(true, Ms(0));

        Assert.Equal(0, a.MagnifyProgress(Ms(0)));
        Assert.Equal(Easing.OutCubic(0.5), a.MagnifyProgress(Ms(75)), 6);
        Assert.Equal(1, a.MagnifyProgress(Ms(150)));
        Assert.True(a.IsAnimating(Ms(100)));
        Assert.False(a.IsAnimating(Ms(151)));
    }

    [Fact]
    public void Pointer_exit_mid_animation_reverses_from_current_value()
    {
        var a = Started("a");
        a.SetPointerInside(true, Ms(0));
        var v = a.MagnifyProgress(Ms(75));

        a.SetPointerInside(false, Ms(75));

        Assert.Equal(v, a.MagnifyProgress(Ms(75)), 6);
        Assert.True(a.MagnifyProgress(Ms(100)) < v);
        Assert.Equal(0, a.MagnifyProgress(Ms(225)));
    }

    [Fact]
    public void Repeated_pointer_state_does_not_restart_animation()
    {
        var a = Started("a");
        a.SetPointerInside(true, Ms(0));

        a.SetPointerInside(true, Ms(100));

        Assert.Equal(1, a.MagnifyProgress(Ms(150)));
    }

    [Fact]
    public void Readding_a_ghost_cancels_its_exit()
    {
        var a = Started("a", "b", "c");
        a.ApplyChanges(new[] { "a", "c" }, Array.Empty<string>(), new[] { "b" }, Ms(0));

        a.ApplyChanges(new[] { "a", "b", "c" }, new[] { "b" }, Array.Empty<string>(), Ms(50));

        Assert.True(Slot(a, "b", 50) > 0.9);
        Assert.Equal(1, Slot(a, "b", 250));
        Assert.Equal(new[] { "a", "b", "c" }, a.Slots(Ms(400)).Select(s => s.Key));
    }

    [Fact]
    public void Disabled_animations_complete_instantly()
    {
        var a = Started("a");
        a.AnimationsEnabled = false;

        a.ApplyChanges(new[] { "a", "b" }, new[] { "b" }, Array.Empty<string>(), Ms(0));
        a.SetPointerInside(true, Ms(0));

        Assert.Equal(1, Slot(a, "b", 0));
        Assert.Equal(1, a.MagnifyProgress(Ms(0)));
        Assert.False(a.IsAnimating(Ms(0)));
    }
}
