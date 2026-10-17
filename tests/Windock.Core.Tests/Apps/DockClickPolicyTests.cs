using Windock.Core.Abstractions;
using Windock.Core.Apps;

namespace Windock.Core.Tests.Apps;

public class DockClickPolicyTests
{
    private static readonly AppId Id = AppId.FromExePath(@"C:\Apps\B.exe");

    private static WindowInfo Win(nint h) => new(h, Id, "B", "t", @"C:\Apps\B.exe", false);

    private static DockItem App(params nint[] windows) =>
        new(Id.Value, DockItemKind.App, Id, "B", @"C:\Apps\B.lnk", @"C:\Apps\B.lnk", true, windows.Select(Win).ToList(), false);

    [Fact]
    public void Not_running_launches_target()
    {
        Assert.Equal(new ClickAction.Launch(@"C:\Apps\B.lnk"), DockClickPolicy.Decide(App(), 0, shift: false));
    }

    [Fact]
    public void Running_inactive_activates_most_recent_window()
    {
        Assert.Equal(new ClickAction.Activate(7), DockClickPolicy.Decide(App(7), foreground: 99, shift: false));
    }

    [Fact]
    public void Single_running_window_that_is_active_is_minimized()
    {
        Assert.Equal(new ClickAction.Minimize(7), DockClickPolicy.Decide(App(7), foreground: 7, shift: false));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(99)]
    public void Several_windows_show_the_window_previews_like_the_taskbar(int foreground)
    {
        Assert.Equal(ClickAction.ShowWindows.Instance, DockClickPolicy.Decide(App(7, 8), foreground, shift: false));
    }

    [Fact]
    public void Shift_click_launches_new_instance_even_when_running()
    {
        Assert.Equal(new ClickAction.Launch(@"C:\Apps\B.lnk"), DockClickPolicy.Decide(App(7), foreground: 7, shift: true));
    }

    [Fact]
    public void Running_app_without_launch_target_cannot_launch_new_instance()
    {
        var item = App(7) with { LaunchTarget = null };

        Assert.Equal(ClickAction.None.Instance, DockClickPolicy.Decide(item, 0, shift: true));
    }

    [Fact]
    public void Recycle_bin_opens_recycle_bin()
    {
        var bin = new DockItem("recyclebin", DockItemKind.RecycleBin, null, "Recycle Bin", null, null, false, Array.Empty<WindowInfo>(), false);

        Assert.Equal(ClickAction.OpenRecycleBin.Instance, DockClickPolicy.Decide(bin, 0, shift: false));
    }

    [Fact]
    public void Separator_does_nothing()
    {
        var sep = new DockItem("sep:trash", DockItemKind.Separator, null, "", null, null, false, Array.Empty<WindowInfo>(), false);

        Assert.Equal(ClickAction.None.Instance, DockClickPolicy.Decide(sep, 0, shift: false));
    }
}
