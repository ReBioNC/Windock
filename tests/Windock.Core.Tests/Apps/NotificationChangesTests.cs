using Windock.Core.Apps;

namespace Windock.Core.Tests.Apps;

public class NotificationChangesTests
{
    private static Dictionary<string, AppNotifications> State(params (string App, int Badge, long Toast)[] apps) =>
        apps.ToDictionary(a => a.App, a => new AppNotifications(a.Badge, a.Toast));

    [Fact]
    public void First_reading_marks_only_apps_with_a_badge()
    {
        var (raise, clear) = NotificationChanges.Compare(null, State(("wa", 2, 100), ("claude", 0, 90)));

        Assert.Equal(new[] { "wa" }, raise);
        Assert.Empty(clear);
    }

    [Fact]
    public void A_new_toast_marks_the_app()
    {
        var (raise, _) = NotificationChanges.Compare(State(("wa", 0, 100)), State(("wa", 0, 150)));

        Assert.Equal(new[] { "wa" }, raise);
    }

    [Fact]
    public void A_first_toast_from_a_new_app_marks_it()
    {
        var (raise, _) = NotificationChanges.Compare(State(), State(("discord", 0, 10)));

        Assert.Equal(new[] { "discord" }, raise);
    }

    [Fact]
    public void A_growing_badge_marks_the_app()
    {
        var (raise, _) = NotificationChanges.Compare(State(("wa", 1, 100)), State(("wa", 3, 100)));

        Assert.Equal(new[] { "wa" }, raise);
    }

    [Fact]
    public void Badge_back_to_zero_clears()
    {
        var (raise, clear) = NotificationChanges.Compare(State(("wa", 2, 100)), State(("wa", 0, 100)));

        Assert.Empty(raise);
        Assert.Equal(new[] { "wa" }, clear);
    }

    [Fact]
    public void Badge_row_removed_clears()
    {
        var (_, clear) = NotificationChanges.Compare(State(("wa", 2, 100)), State());

        Assert.Equal(new[] { "wa" }, clear);
    }

    [Fact]
    public void Nothing_new_changes_nothing()
    {
        var (raise, clear) = NotificationChanges.Compare(State(("wa", 2, 100)), State(("wa", 2, 100)));

        Assert.Empty(raise);
        Assert.Empty(clear);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(2, "2")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void Badge_text(int count, string? expected) => Assert.Equal(expected, NotificationChanges.BadgeText(count));

    [Theory]
    [InlineData("<badge value=\"3\"/>", 3)]
    [InlineData("<badge value='x'/>", 0)]
    [InlineData("<badge value=\"newMessage\"/>", 1)]
    [InlineData("<badge value=\"none\"/>", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Parses_badges(string? payload, int expected) => Assert.Equal(expected, NotificationChanges.ParseBadge(payload));
}
