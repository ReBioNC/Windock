using Windock.Core.Apps;

namespace Windock.Core.Tests.Apps;

public class SwitcherListTests
{
    private static SwitcherEntry W(nint h, string app, string title) => new(h, app.ToLowerInvariant(), app, title);

    private static readonly SwitcherEntry[] Mru =
    {
        W(1, "Claude", "Windock"), W(2, "Chrome", "YouTube"), W(3, "Discord", "#umum"), W(4, "Chrome", "Gmail"), W(5, "Café", "Menu"),
    };

    private static nint[] Handles(IEnumerable<SwitcherEntry> e) => e.Select(x => x.Handle).ToArray();

    [Fact]
    public void Ungrouped_keeps_most_recent_first() => Assert.Equal(new nint[] { 1, 2, 3, 4, 5 }, Handles(SwitcherList.Arrange(Mru, false)));

    [Fact]
    public void Grouped_puts_an_apps_windows_together_in_app_recency_order() =>
        Assert.Equal(new nint[] { 1, 2, 4, 3, 5 }, Handles(SwitcherList.Arrange(Mru, true)));

    [Fact]
    public void First_selection_is_the_previously_used_window()
    {
        var arranged = SwitcherList.Arrange(Mru, true);

        Assert.Equal(1, SwitcherList.InitialSelection(arranged, Mru));
        Assert.Equal(-1, SwitcherList.InitialSelection(Array.Empty<SwitcherEntry>(), Mru));
        Assert.Equal(0, SwitcherList.InitialSelection(new[] { Mru[0] }, new[] { Mru[0] }));
    }

    [Theory]
    [InlineData("chr", new long[] { 2, 4 })]
    [InlineData("chrome gma", new long[] { 4 })]
    [InlineData("CAFE", new long[] { 5 })]
    [InlineData("", new long[] { 1, 2, 3, 4, 5 })]
    [InlineData("zzz", new long[0])]
    public void Search_matches_every_word_in_app_or_title(string query, long[] expected) =>
        Assert.Equal(expected.Select(x => (nint)x).ToArray(), Handles(SwitcherList.Filter(Mru, query)));

    [Theory]
    [InlineData(0, 1, 5, 1)]
    [InlineData(4, 1, 5, 0)]
    [InlineData(0, -1, 5, 4)]
    [InlineData(0, 1, 0, -1)]
    public void Step_wraps(int current, int delta, int count, int expected) => Assert.Equal(expected, SwitcherList.Step(current, delta, count));
}
