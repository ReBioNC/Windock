using Windock.Core.Settings;

namespace Windock.Core.Tests.Settings;

/// Settings added in v0.6: theme, status area, launcher, recycle bin, hidden apps, attention.
public class V06SettingsTests
{
    [Fact]
    public void Defaults_match_the_v06_design()
    {
        var s = SettingsDefaults.Create();

        Assert.Equal(new ThemeSettings("#1E1E1E", 16, true, "#FFFFFF", "#2F6FE4", false), s.Theme);
        Assert.Equal(new StatusSettings(Enabled: true, ShowTray: true, ShowWifi: true, ShowVolume: true, ShowBattery: true,
            ShowBatteryPercent: false, ShowLanguage: true, ShowClock: true, Clock24Hour: true, ShowDate: true, ShowSeconds: false,
            ShowNotifications: true), s.Status);
        Assert.Equal(new LauncherSettings(true, "Alt+Space", 8, false), s.Launcher);
        Assert.Equal(new TrashSettings(true, true), s.Trash);
        Assert.Empty(s.HiddenApps);
        Assert.Equal(AttentionStyle.Bounce, s.Behavior.Attention);
        Assert.Equal(AttentionRepeat.UntilOpened, s.Behavior.AttentionRepeat);
        Assert.True(s.Behavior.AttentionFromNotifications);
    }

    [Fact]
    public void All_new_settings_roundtrip()
    {
        var d = SettingsDefaults.Create();
        var original = d with
        {
            Theme = new ThemeSettings("#102030", 8, false, "#FF8800", "#00AA55", true),
            Status = d.Status with { ShowWifi = false, Clock24Hour = false, ShowSeconds = true, ShowBatteryPercent = true },
            Launcher = new LauncherSettings(false, "Ctrl+Space", 10, true),
            Trash = new TrashSettings(false, false),
            HiddenApps = new[] { new HiddenApp("path:c:\\apps\\tool.exe", "Tool") },
            Behavior = d.Behavior with { Attention = AttentionStyle.Dot, AttentionRepeat = AttentionRepeat.Once, AttentionFromNotifications = false },
        };

        Assert.Equal(original, SettingsJson.Parse(SettingsJson.Serialize(original)));
    }

    [Fact]
    public void Hidden_apps_compare_by_content()
    {
        var d = SettingsDefaults.Create();
        Assert.Equal(d with { HiddenApps = new[] { new HiddenApp("a", "A") } }, d with { HiddenApps = new List<HiddenApp> { new("a", "A") } });
    }

    [Theory]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("12AB34", "#12AB34")]
    [InlineData("red", "#1E1E1E")]
    [InlineData("#12345", "#1E1E1E")]
    public void Plate_color_is_normalized_or_falls_back(string input, string expected)
    {
        var s = SettingsJson.Parse("{ \"theme\": { \"plateColor\": \"" + input + "\" } }");

        Assert.Equal(expected, s.Theme.PlateColor);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(99, 30)]
    [InlineData(12, 12)]
    public void Corner_radius_is_clamped(int input, int expected)
    {
        Assert.Equal(expected, SettingsJson.Parse("{ \"theme\": { \"cornerRadius\": " + input + " } }").Theme.CornerRadius);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(50, 12)]
    [InlineData(9, 9)]
    public void Launcher_result_count_is_clamped(int input, int expected)
    {
        Assert.Equal(expected, SettingsJson.Parse("{ \"launcher\": { \"maxResults\": " + input + " } }").Launcher.MaxResults);
    }

    [Fact]
    public void Invalid_hidden_app_entries_are_skipped()
    {
        var s = SettingsJson.Parse("{ \"hiddenApps\": [ {\"appId\":\"a\",\"name\":\"A\"}, {\"name\":\"no id\"}, 5, {\"appId\":\"b\"} ] }");

        Assert.Equal(new[] { new HiddenApp("a", "A"), new HiddenApp("b", "b") }, s.HiddenApps);
    }

    [Fact]
    public void Missing_new_sections_use_defaults()
    {
        var s = SettingsJson.Parse("{ \"version\": 1 }");

        Assert.Equal(SettingsDefaults.Create(), s);
    }
}
