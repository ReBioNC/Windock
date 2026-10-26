using Windock.Core.Island;

namespace Windock.Core.Tests.Island;

public class SwipeGestureTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Small_touchpad_steps_add_up_to_one_page()
    {
        var swipe = new SwipeGesture();

        Assert.Equal(0, swipe.Feed(40, Ms(0)));
        Assert.Equal(0, swipe.Feed(40, Ms(16)));
        Assert.Equal(1, swipe.Feed(40, Ms(32)));
    }

    [Fact]
    public void The_rest_of_the_swipe_and_its_coasting_turn_nothing_more()
    {
        var swipe = new SwipeGesture();
        swipe.Feed(200, Ms(0));

        for (var t = 16; t < 1000; t += 16) Assert.Equal(0, swipe.Feed(60, Ms(t)));
    }

    [Fact]
    public void A_new_swipe_after_a_pause_turns_again_and_can_go_back()
    {
        var swipe = new SwipeGesture();
        swipe.Feed(150, Ms(0));

        Assert.Equal(-1, swipe.Feed(-150, Ms(400)));
    }

    [Fact]
    public void A_tiny_wobble_does_not_turn()
    {
        var swipe = new SwipeGesture();

        Assert.Equal(0, swipe.Feed(30, Ms(0)));
        Assert.Equal(0, swipe.Feed(30, Ms(400)));  // after a pause the sum starts over
    }
}
