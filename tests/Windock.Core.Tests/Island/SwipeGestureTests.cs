using Windock.Core.Island;

namespace Windock.Core.Tests.Island;

public class SwipeGestureTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Small_touchpad_steps_add_up_to_one_page_and_the_card_follows_them()
    {
        var swipe = new SwipeGesture();

        Assert.Equal(new SwipeStep(0, 0.5), swipe.Feed(30, Ms(0)));
        Assert.Equal(1, swipe.Feed(30, Ms(16)).Turn);
    }

    [Fact]
    public void One_wheel_click_turns_at_once()
    {
        Assert.Equal(-1, new SwipeGesture().Feed(-120, Ms(0)).Turn);
    }

    [Fact]
    public void The_rest_of_the_swipe_and_its_coasting_turn_nothing_more()
    {
        var swipe = new SwipeGesture();
        swipe.Feed(80, Ms(0));

        for (var t = 16; t < 1000; t += 16) Assert.Equal(new SwipeStep(0, 0), swipe.Feed(20, Ms(t)));
    }

    [Fact]
    public void A_new_swipe_after_a_short_pause_turns_again()
    {
        var swipe = new SwipeGesture();
        swipe.Feed(80, Ms(0));

        Assert.Equal(1, swipe.Feed(80, Ms(150)).Turn);
    }

    [Fact]
    public void Going_back_the_other_way_turns_back_without_a_pause()
    {
        var swipe = new SwipeGesture();
        swipe.Feed(80, Ms(0));

        Assert.Equal(-1, swipe.Feed(-80, Ms(30)).Turn);
    }

    [Fact]
    public void An_unfinished_swipe_starts_over_after_a_pause()
    {
        var swipe = new SwipeGesture();

        Assert.Equal(0, swipe.Feed(40, Ms(0)).Turn);
        Assert.Equal(new SwipeStep(0, 40 / 60.0), swipe.Feed(40, Ms(300)));
    }
}
