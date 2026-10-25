using Windock.Core.Island;
using Windock.Core.Settings;

namespace Windock.Core.Tests.Island;

public class IslandRulesTests
{
    [Theory]
    [InlineData(IslandVisibility.AutoHide, false, false, false, true)]   // nothing in the way
    [InlineData(IslandVisibility.AutoHide, true, false, false, false)]   // maximized window covers it
    [InlineData(IslandVisibility.AutoHide, true, true, false, true)]     // desktop in front
    [InlineData(IslandVisibility.AutoHide, true, false, true, true)]     // top edge, event or hover
    [InlineData(IslandVisibility.Always, true, false, false, true)]
    public void Visibility_follows_the_mode(IslandVisibility mode, bool covered, bool desktop, bool attention, bool expected) =>
        Assert.Equal(expected, IslandVisibilityPolicy.ShouldShow(true, false, mode, covered, desktop, attention));

    [Fact]
    public void Off_or_suppressed_is_never_shown()
    {
        Assert.False(IslandVisibilityPolicy.ShouldShow(false, false, IslandVisibility.Always, false, true, true));
        Assert.False(IslandVisibilityPolicy.ShouldShow(true, true, IslandVisibility.Always, false, true, true));
    }

    [Fact]
    public void Island_takes_clock_date_and_battery_from_the_dock()
    {
        var s = SettingsDefaults.Create();

        Assert.True(IslandOwnership.IslandShowsClock(s));
        Assert.False(IslandOwnership.DockShowsClock(s));
        Assert.False(IslandOwnership.DockShowsDate(s));
        Assert.False(IslandOwnership.DockShowsBattery(s));
    }

    [Fact]
    public void Turning_the_island_off_gives_them_back_to_the_dock()
    {
        var d = SettingsDefaults.Create();
        var s = d with { Island = d.Island with { Enabled = false } };

        Assert.False(IslandOwnership.IslandShowsClock(s));
        Assert.True(IslandOwnership.DockShowsClock(s));
        Assert.True(IslandOwnership.DockShowsDate(s));
        Assert.True(IslandOwnership.DockShowsBattery(s));
    }

    [Fact]
    public void An_item_left_in_the_dock_still_follows_the_dock_toggle()
    {
        var d = SettingsDefaults.Create();
        var s = d with { Island = d.Island with { ShowBattery = false }, Status = d.Status with { ShowBattery = false } };

        Assert.False(IslandOwnership.DockShowsBattery(s));
        Assert.False(IslandOwnership.IslandShowsBattery(s));
    }

    [Fact]
    public void Plugging_in_says_charging()
    {
        var e = BatteryEvents.Diff(new PowerReading(60, false, false), new PowerReading(60, true, true));

        Assert.Equal("Charging", e?.Text);
        Assert.Equal("60%", e?.Detail);
    }

    [Fact]
    public void Unplugging_says_on_battery()
    {
        Assert.Equal("On battery", BatteryEvents.Diff(new PowerReading(80, true, true), new PowerReading(80, false, false))?.Text);
    }

    [Fact]
    public void Crossing_a_low_mark_warns_once()
    {
        Assert.Equal("Battery low", BatteryEvents.Diff(new PowerReading(21, false, false), new PowerReading(20, false, false))?.Text);
        Assert.Null(BatteryEvents.Diff(new PowerReading(20, false, false), new PowerReading(19, false, false)));
        Assert.Equal("Battery very low", BatteryEvents.Diff(new PowerReading(11, false, false), new PowerReading(10, false, false))?.Text);
    }

    [Fact]
    public void No_change_or_no_battery_is_no_event()
    {
        Assert.Null(BatteryEvents.Diff(new PowerReading(50, false, false), new PowerReading(49, false, false)));
        Assert.Null(BatteryEvents.Diff(null, new PowerReading(50, false, false)));
    }

    [Fact]
    public void A_receiver_that_is_keyboard_and_mouse_shows_as_one_mouse_event()
    {
        var burst = new DeviceBurst();
        burst.Add(DeviceKind.Keyboard, true, null);
        burst.Add(DeviceKind.Mouse, true, "G304");
        burst.Add(DeviceKind.Mouse, true, null);

        var e = burst.Resolve();

        Assert.Equal("Mouse connected", e?.Text);
        Assert.Equal("G304", e?.Detail);
        Assert.True(burst.IsEmpty);
        Assert.Null(burst.Resolve());
    }

    [Fact]
    public void A_lone_keyboard_or_display_keeps_its_kind()
    {
        var burst = new DeviceBurst();
        burst.Add(DeviceKind.Keyboard, false, null);
        Assert.Equal("Keyboard disconnected", burst.Resolve()?.Text);

        burst.Add(DeviceKind.Display, true, "DELL P2419H");
        Assert.Equal(IslandGlyphs.Display, burst.Resolve()?.Glyph);
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("chrome", "Chrome")]
    [InlineData("msedge.exe", "Edge")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Groove Music")]
    [InlineData("SomePlayer.exe", "SomePlayer")]
    [InlineData("", "")]
    public void Media_app_names_are_readable(string id, string expected) => Assert.Equal(expected, MediaAppName.From(id));

    [Fact]
    public void Island_settings_round_trip_and_clamp()
    {
        var d = SettingsDefaults.Create();
        var s = d with { Island = d.Island with { Visibility = IslandVisibility.Always, Size = IslandSize.Large, ShowDate = false, EventSeconds = 4 } };

        Assert.Equal(s, SettingsJson.Parse(SettingsJson.Serialize(s)));
        Assert.Equal(SettingsNormalizer.MaxIslandEventSeconds,
            SettingsNormalizer.Normalize(d with { Island = d.Island with { EventSeconds = 60 } }).Island.EventSeconds);
    }

    [Fact]
    public void Older_settings_without_island_get_the_defaults()
    {
        var s = SettingsJson.Parse("{\"version\":1}");

        Assert.Equal(IslandSettings.Default, s.Island);
    }
}
