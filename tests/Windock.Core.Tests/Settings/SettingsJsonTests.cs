using Windock.Core.Settings;

namespace Windock.Core.Tests.Settings;

public class SettingsJsonTests
{
    private static DockSettings TwoPins() => SettingsDefaults.Create() with
    {
        Pins = new[] { new PinEntry(@"C:\Windows\explorer.exe"), new PinEntry(@"C:\Apps\Chrome.lnk") },
        FirstRunCompleted = true,
    };

    [Fact]
    public void Roundtrip_preserves_all_values()
    {
        var original = TwoPins() with
        {
            Appearance = TwoPins().Appearance with { IconSize = 64, BackgroundOpacity = 0.7, Blur = BlurMode.Off },
            Behavior = TwoPins().Behavior with { TaskbarMode = TaskbarMode.AutoHide, ExplorerContextMenu = false },
        };

        var parsed = SettingsJson.Parse(SettingsJson.Serialize(original));

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Enums_are_written_as_strings()
    {
        var json = SettingsJson.Serialize(SettingsDefaults.Create());

        Assert.Contains("\"taskbarMode\": \"Hidden\"", json);
        Assert.Contains("\"position\": \"Bottom\"", json);
    }

    [Fact]
    public void Unknown_enum_value_falls_back_only_for_that_field()
    {
        var d = SettingsDefaults.Create();
        var json = SettingsJson.Serialize(d with { Appearance = d.Appearance with { IconSize = 64 } })
            .Replace("\"position\": \"Bottom\"", "\"position\": \"Top\"");

        var s = SettingsJson.Parse(json);

        Assert.Equal(DockPosition.Bottom, s.Appearance.Position);
        Assert.Equal(64, s.Appearance.IconSize);
    }

    [Fact]
    public void Wrong_type_falls_back_only_for_that_field()
    {
        var d = SettingsDefaults.Create();
        var json = SettingsJson.Serialize(d with { Appearance = d.Appearance with { IconSize = 64, BackgroundOpacity = 0.7 } })
            .Replace("\"iconSize\": 64", "\"iconSize\": \"big\"");

        var s = SettingsJson.Parse(json);

        Assert.Equal(48, s.Appearance.IconSize);
        Assert.Equal(0.7, s.Appearance.BackgroundOpacity);
    }

    [Fact]
    public void Missing_sections_use_defaults()
    {
        Assert.Equal(SettingsDefaults.Create(), SettingsJson.Parse("{ \"version\": 1 }"));
    }

    [Fact]
    public void Pins_with_invalid_entries_are_skipped()
    {
        var s = SettingsJson.Parse("{ \"pins\": [ {\"path\":\"a.exe\"}, {\"path\":5}, \"x\", {}, {\"path\":\"  \"} ] }");

        Assert.Equal(new[] { new PinEntry("a.exe") }, s.Pins);
    }

    [Fact]
    public void Out_of_range_values_are_normalized()
    {
        var s = SettingsJson.Parse("{ \"appearance\": { \"iconSize\": 500 } }");

        Assert.Equal(96, s.Appearance.IconSize);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public void Non_object_json_throws_SettingsParseException(string text)
    {
        Assert.Throws<SettingsParseException>(() => SettingsJson.Parse(text));
    }
}
