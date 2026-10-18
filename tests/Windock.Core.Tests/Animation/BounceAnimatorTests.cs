using Windock.Core.Animation;

namespace Windock.Core.Tests.Animation;

public class BounceAnimatorTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Rises_falls_and_rests()
    {
        var b = new BounceAnimator();
        b.Start("a", Ms(1000), BounceAnimator.LaunchTimeout);

        Assert.Equal(0, b.Lift("a", Ms(1000)));
        Assert.Equal(0.75, b.Lift("a", Ms(1150)), 3);  // ease-out: 1 - 0.5²
        Assert.Equal(1, b.Lift("a", Ms(1300)), 3);
        Assert.Equal(0.75, b.Lift("a", Ms(1450)), 3);  // ease-in: 1 - 0.5²
        Assert.Equal(0, b.Lift("a", Ms(1650)));
        Assert.Equal(0.75, b.Lift("a", Ms(1850)), 3);  // second bounce
    }

    [Fact]
    public void Stop_lands_the_current_bounce_first()
    {
        var b = new BounceAnimator();
        b.Start("a", Ms(0), BounceAnimator.LaunchTimeout);

        b.Stop("a", Ms(200));

        Assert.True(b.Lift("a", Ms(500)) > 0);
        Assert.True(b.IsBouncing("a", Ms(599)));
        Assert.False(b.IsBouncing("a", Ms(600)));
        Assert.Equal(0, b.Lift("a", Ms(800)));
        Assert.False(b.IsAnimating(Ms(600)));
    }

    [Fact]
    public void Stop_while_resting_ends_at_once()
    {
        var b = new BounceAnimator();
        b.Start("a", Ms(0), BounceAnimator.LaunchTimeout);

        b.Stop("a", Ms(650));

        Assert.False(b.IsBouncing("a", Ms(650)));
    }

    [Fact]
    public void Times_out_after_the_limit_rounded_to_a_landing()
    {
        var b = new BounceAnimator();
        b.Start("a", Ms(0), BounceAnimator.LaunchTimeout);

        // 10 s = 14 cycles (9.8 s) + 200 ms into the 15th rise, which lands at 9.8 + 0.6 s.
        Assert.True(b.IsBouncing("a", Ms(10_399)));
        Assert.False(b.IsBouncing("a", Ms(10_400)));
    }

    [Fact]
    public void Single_bounce_with_cycle_limit()
    {
        var b = new BounceAnimator();
        b.Start("a", Ms(0), BounceAnimator.Cycle);

        Assert.True(b.IsBouncing("a", Ms(500)));
        Assert.False(b.IsBouncing("a", Ms(700)));
        Assert.Equal(0, b.Lift("a", Ms(850)));
    }

    [Fact]
    public void Restart_while_bouncing_keeps_the_phase()
    {
        var b = new BounceAnimator();
        b.Start("a", Ms(0), BounceAnimator.Cycle);

        b.Start("a", Ms(300), BounceAnimator.LaunchTimeout);

        Assert.Equal(1, b.Lift("a", Ms(300)), 3);
        Assert.True(b.IsBouncing("a", Ms(5000)));
    }

    [Fact]
    public void Does_nothing_when_animations_are_off()
    {
        var b = new BounceAnimator { AnimationsEnabled = false };
        b.Start("a", Ms(0), BounceAnimator.LaunchTimeout);

        Assert.False(b.IsAnimating(Ms(0)));
        Assert.Equal(0, b.Lift("a", Ms(150)));
    }
}
