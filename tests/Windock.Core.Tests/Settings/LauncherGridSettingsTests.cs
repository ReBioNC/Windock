using Windock.Core.Launcher;
using Windock.Core.Settings;

namespace Windock.Core.Tests.Settings;

public class LauncherGridSettingsTests
{
    [Fact]
    public void Defaults_are_the_grid_with_history()
    {
        var l = SettingsDefaults.Create().Launcher;
        Assert.Equal(LauncherLayout.Grid, l.Layout);
        Assert.True(l.ShowCategories);
        Assert.True(l.UseWindowsHistory);
        Assert.True(l.TrackLaunches);
        Assert.Equal(18, l.FrequentCount);
        Assert.Equal(6, l.Columns);
        Assert.Empty(l.CategoryOverrides);
    }

    [Fact]
    public void Round_trips_every_launcher_option()
    {
        var s = SettingsDefaults.Create() with
        {
            Launcher = LauncherSettings.Default with
            {
                Layout = LauncherLayout.List, ShowCategories = false, UseWindowsHistory = false, TrackLaunches = false,
                FrequentCount = 12, Columns = 5,
                CategoryOverrides = new[] { new LauncherCategoryOverride("Discord", LauncherCategory.Office) },
            },
        };

        Assert.Equal(s.Launcher, SettingsJson.Parse(SettingsJson.Serialize(s)).Launcher);
    }

    [Theory]
    [InlineData(2, 8)]
    [InlineData(99, 24)]
    public void Frequent_count_is_clamped(int input, int expected) =>
        Assert.Equal(expected, SettingsJson.Parse("{ \"launcher\": { \"frequentCount\": " + input + " } }").Launcher.FrequentCount);

    [Theory]
    [InlineData(1, 4)]
    [InlineData(20, 8)]
    public void Columns_are_clamped(int input, int expected) =>
        Assert.Equal(expected, SettingsJson.Parse("{ \"launcher\": { \"columns\": " + input + " } }").Launcher.Columns);

    [Fact]
    public void Bad_category_entries_are_skipped()
    {
        var s = SettingsJson.Parse("""{ "launcher": { "categories": [ { "name": "A", "category": "Games" }, { "name": "B", "category": "Nope" }, { "category": "Media" }, 5 ] } }""");

        Assert.Equal(new[] { new LauncherCategoryOverride("A", LauncherCategory.Games) }, s.Launcher.CategoryOverrides);
    }

    [Fact]
    public void Equal_overrides_mean_equal_settings() =>
        Assert.Equal(
            LauncherSettings.Default with { CategoryOverrides = new[] { new LauncherCategoryOverride("A", LauncherCategory.Games) } },
            LauncherSettings.Default with { CategoryOverrides = new List<LauncherCategoryOverride> { new("A", LauncherCategory.Games) } });
}
