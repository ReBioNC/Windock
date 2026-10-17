using Windock.Core.Layout;

namespace Windock.Core.Tests.Layout;

public class DockDropTargetTests
{
    private static readonly double[] Centers = { 100, 160, 220 };

    [Theory]
    [InlineData(50, 0)]
    [InlineData(129, 1)]
    [InlineData(161, 2)]
    [InlineData(500, 3)]
    public void Index_counts_pinned_icons_left_of_cursor(double x, int expected)
    {
        Assert.Equal(expected, DockDropTarget.IndexFor(Centers, x));
    }

    [Fact]
    public void Empty_pin_list_drops_at_zero()
    {
        Assert.Equal(0, DockDropTarget.IndexFor(Array.Empty<double>(), 300));
    }

    [Theory]
    [InlineData(-81, true)]
    [InlineData(-80, false)]
    [InlineData(10, false)]
    public void Dragging_far_above_the_plate_means_remove(double yAbovePlate, bool expected)
    {
        Assert.Equal(expected, DockDropTarget.IsRemoveGesture(plateTop: 0, cursorY: yAbovePlate));
    }
}
