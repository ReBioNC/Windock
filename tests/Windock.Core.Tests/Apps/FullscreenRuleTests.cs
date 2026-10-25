using Windock.Core.Apps;

namespace Windock.Core.Tests.Apps;

public class FullscreenRuleTests
{
    private static readonly (int, int, int, int) Monitor = (0, 0, 1920, 1080);

    private static bool Check((int, int, int, int) rect, bool maximized = false, string cls = "UnityWndClass", bool own = false, bool visible = true) =>
        FullscreenRule.IsFullscreenApp(rect, Monitor, maximized, visible, cls, own);

    [Fact]
    public void Borderless_window_covering_the_monitor_is_fullscreen() => Assert.True(Check((0, 0, 1920, 1080)));

    [Fact]
    public void Maximized_window_is_not_fullscreen_even_when_it_covers_the_monitor()
    {
        // With auto-hide the work area is the whole screen, so a maximized window spans (-8,-8)-(1928,1088).
        Assert.False(Check((-8, -8, 1928, 1088), maximized: true));
    }

    [Fact]
    public void Window_smaller_than_the_monitor_is_not_fullscreen() => Assert.False(Check((0, 0, 1920, 1040)));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    public void Desktop_and_shell_surfaces_are_not_fullscreen(string cls) => Assert.False(Check((0, 0, 1920, 1080), cls: cls));

    [Fact]
    public void Own_or_invisible_windows_are_not_fullscreen()
    {
        Assert.False(Check((0, 0, 1920, 1080), own: true));
        Assert.False(Check((0, 0, 1920, 1080), visible: false));
    }

    [Fact]
    public void A_see_through_screen_capture_overlay_is_not_fullscreen() =>
        Assert.False(FullscreenRule.IsFullscreenApp((0, 0, 1920, 1080), Monitor, false, true, "WinUIDesktopWin32WindowClass", false, isLayered: true));
}
