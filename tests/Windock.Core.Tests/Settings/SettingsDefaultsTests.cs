using Windock.Core.Settings;

namespace Windock.Core.Tests.Settings;

public class SettingsDefaultsTests
{
    [Fact]
    public void Defaults_match_design_section_6()
    {
        var s = SettingsDefaults.Create();

        Assert.Equal(1, s.Version);
        Assert.Equal(DockPosition.Bottom, s.Appearance.Position);
        Assert.Equal(48, s.Appearance.IconSize);
        Assert.Equal(new MagnificationSettings(true, 80), s.Appearance.Magnification);
        Assert.Equal(0.55, s.Appearance.BackgroundOpacity);
        Assert.Equal(BlurMode.Standard, s.Appearance.Blur);
        Assert.True(s.Appearance.ShowRunningIndicators);
        Assert.Equal(new BehaviorSettings(TaskbarMode.Hidden, DockAutoHide.Never, true, false, ExplorerContextMenu: true, Mode: DockMode.Windock), s.Behavior);
        Assert.Equal("Ctrl+Alt+Shift+W", s.Safety.EmergencyHotkey);
        Assert.Empty(s.Pins);
        Assert.False(s.FirstRunCompleted);
    }
}
