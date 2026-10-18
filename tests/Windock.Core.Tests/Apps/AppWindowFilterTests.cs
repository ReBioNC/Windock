using Windock.Core.Apps;

namespace Windock.Core.Tests.Apps;

public class AppWindowFilterTests
{
    /// A normal, visible, titled top-level window of a regular app.
    private static WindowFacts Normal() => new(
        IsVisible: true, IsCloaked: false, IsToolWindow: false, IsAppWindow: false, IsNoActivate: false, HasOwner: false,
        HiddenFromTaskbar: false, IsMinimized: false, Width: 800, Height: 600,
        Title: "Untitled - Notepad", ClassName: "Notepad", ProcessName: "notepad", IsOwnProcess: false, IsChild: false);

    [Fact]
    public void Normal_window_is_an_app() => Assert.True(AppWindowFilter.IsAppWindow(Normal()));

    [Fact]
    public void Invisible_cloaked_tool_or_owned_windows_are_not_apps()
    {
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { IsVisible = false }));
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { IsCloaked = true }));
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { IsToolWindow = true }));
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { HasOwner = true }));
    }

    [Fact]
    public void App_window_style_overrides_tool_and_owner()
    {
        Assert.True(AppWindowFilter.IsAppWindow(Normal() with { IsToolWindow = true, IsAppWindow = true }));
        Assert.True(AppWindowFilter.IsAppWindow(Normal() with { HasOwner = true, IsAppWindow = true }));
    }

    [Fact]
    public void Untitled_window_is_not_an_app_unless_app_window_style()
    {
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { Title = "" }));
        Assert.True(AppWindowFilter.IsAppWindow(Normal() with { Title = "", IsAppWindow = true }));
    }

    [Fact]
    public void Child_windows_inside_an_app_are_not_apps() // e.g. Discord/Electron "Chrome Legacy Window"
    {
        var legacy = Normal() with { IsChild = true, Title = "Chrome Legacy Window", ClassName = "Chrome_RenderWidgetHostHWND", ProcessName = "Discord" };

        Assert.False(AppWindowFilter.IsAppWindow(legacy));
        Assert.False(AppWindowFilter.IsAppWindow(legacy with { IsAppWindow = true }));
    }

    [Fact]
    public void Window_an_app_removed_from_the_taskbar_is_not_shown()
    {
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { HiddenFromTaskbar = true }));
    }

    [Fact]
    public void No_activate_window_is_not_an_app() => Assert.False(AppWindowFilter.IsAppWindow(Normal() with { IsNoActivate = true }));

    [Fact]
    public void Zero_size_window_is_not_an_app_but_minimized_ones_are()
    {
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { Width = 0 }));
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { Height = 1 }));
        Assert.True(AppWindowFilter.IsAppWindow(Normal() with { Width = 160, Height = 28, IsMinimized = true }));
    }

    [Theory]
    [InlineData("msedgewebview2")]
    [InlineData("MSEdgeWebView2")]
    [InlineData("TextInputHost")]
    [InlineData("ShellExperienceHost")]
    [InlineData("SearchHost")]
    [InlineData("StartMenuExperienceHost")]
    public void Helper_processes_are_not_apps(string process)
    {
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { ProcessName = process, Title = "Microsoft Edge WebView2" }));
    }

    [Theory]
    [InlineData("CabinetWClass", true)]
    [InlineData("ExploreWClass", true)]
    [InlineData("Shell_Dialog", false)]
    [InlineData("#32770", false)]
    public void Only_folder_windows_count_for_explorer(string className, bool expected)
    {
        var window = Normal() with { ProcessName = "explorer", ClassName = className, Title = "Something" };

        Assert.Equal(expected, AppWindowFilter.IsAppWindow(window));
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    public void Shell_surfaces_are_not_apps(string className)
    {
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { ClassName = className }));
    }

    [Fact]
    public void Own_settings_window_counts_but_untitled_own_windows_do_not()
    {
        Assert.True(AppWindowFilter.IsAppWindow(Normal() with { IsOwnProcess = true, ProcessName = "Windock", Title = "Pengaturan Windock" }));
        Assert.False(AppWindowFilter.IsAppWindow(Normal() with { IsOwnProcess = true, ProcessName = "Windock", Title = "" }));
    }
}
