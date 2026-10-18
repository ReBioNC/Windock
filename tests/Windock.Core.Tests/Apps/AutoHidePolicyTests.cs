using Windock.Core.Apps;
using Windock.Core.Settings;

namespace Windock.Core.Tests.Apps;

public class AutoHidePolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Never_mode_always_shows_the_dock(bool covered, bool pointer)
    {
        Assert.False(AutoHidePolicy.ShouldHide(DockAutoHide.Never, covered, pointer));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void Always_mode_hides_unless_the_pointer_is_using_the_dock(bool covered, bool pointer, bool hidden)
    {
        Assert.Equal(hidden, AutoHidePolicy.ShouldHide(DockAutoHide.Always, covered, pointer));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void When_covered_mode_hides_only_while_a_window_covers_the_dock(bool covered, bool pointer, bool hidden)
    {
        Assert.Equal(hidden, AutoHidePolicy.ShouldHide(DockAutoHide.WhenCovered, covered, pointer));
    }

    [Theory]
    [InlineData(DockAutoHide.Never, true)]
    [InlineData(DockAutoHide.WhenCovered, false)]
    [InlineData(DockAutoHide.Always, false)]
    public void Screen_space_is_reserved_only_when_the_dock_never_hides(DockAutoHide mode, bool reserve)
    {
        Assert.Equal(reserve, AutoHidePolicy.ReservesScreenSpace(mode));
    }

    [Fact]
    public void Overlap_is_any_intersection_with_the_dock_area()
    {
        var dock = (Left: 600, Top: 1010, Right: 1300, Bottom: 1080);

        Assert.True(AutoHidePolicy.Covers((-8, -8, 1928, 1088), dock));        // maximized
        Assert.True(AutoHidePolicy.Covers((900, 500, 1200, 1020), dock));      // window dragged onto the dock
        Assert.False(AutoHidePolicy.Covers((900, 100, 1200, 1009), dock));     // ends just above
        Assert.False(AutoHidePolicy.Covers((0, 600, 599, 1080), dock));        // beside the dock
    }
}
