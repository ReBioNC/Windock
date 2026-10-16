using Windock.Core.Animation;

namespace Windock.Core.Tests.Animation;

public class TweenTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Tween_clamps_before_start_and_after_end()
    {
        var t = new Tween(0, 10, Ms(100), Ms(200), x => x);

        Assert.Equal(0, t.ValueAt(Ms(0)));
        Assert.Equal(5, t.ValueAt(Ms(200)), 6);
        Assert.Equal(10, t.ValueAt(Ms(400)));
        Assert.False(t.IsComplete(Ms(250)));
        Assert.True(t.IsComplete(Ms(300)));
        Assert.Equal(10, t.To);
    }

    [Fact]
    public void Zero_duration_tween_is_immediately_complete()
    {
        var t = new Tween(0, 1, Ms(100), TimeSpan.Zero, Easing.OutCubic);

        Assert.Equal(1, t.ValueAt(Ms(100)));
        Assert.True(t.IsComplete(Ms(100)));
    }
}
