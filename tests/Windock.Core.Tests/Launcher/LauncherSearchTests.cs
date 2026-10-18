using Windock.Core.Launcher;

namespace Windock.Core.Tests.Launcher;

public class LauncherSearchTests
{
    private static LauncherEntry App(string name) => new(name, name + ".lnk", LauncherEntryKind.App);
    private static LauncherEntry File(string name) => new(name, name, LauncherEntryKind.File);

    private static string[] Names(IEnumerable<LauncherEntry> r) => r.Select(e => e.Name).ToArray();

    [Fact]
    public void Ranks_prefix_word_start_contains_subsequence()
    {
        var entries = new[] { App("Visual Studio Code"), App("Paint"), App("Code Writer"), App("Barcode Tool"), App("Calculator Ode") };

        var result = LauncherSearch.Search(entries, "code", 10);

        Assert.Equal(new[] { "Code Writer", "Visual Studio Code", "Barcode Tool", "Calculator Ode" }, Names(result));
    }

    [Fact]
    public void Shorter_names_win_ties_and_apps_beat_files()
    {
        var entries = new[] { App("Notepad++"), File("Notes"), App("Notepad") };

        Assert.Equal(new[] { "Notepad", "Notepad++", "Notes" }, Names(LauncherSearch.Search(entries, "no", 10)));
    }

    [Fact]
    public void Case_and_accents_are_ignored()
    {
        Assert.Single(LauncherSearch.Search(new[] { App("Café Manager") }, "CAFE", 5));
    }

    [Fact]
    public void Limits_results_and_drops_duplicates()
    {
        var entries = Enumerable.Range(0, 20).Select(i => App("App " + i)).Append(App("App 1")).ToList();

        var result = LauncherSearch.Search(entries, "app", 8);

        Assert.Equal(8, result.Count);
        Assert.Equal(result.Count, result.Select(r => r.Name).Distinct().Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xyz")]
    public void Nothing_for_empty_or_unmatched_queries(string query) =>
        Assert.Empty(LauncherSearch.Search(new[] { App("Chrome") }, query, 5));

    [Fact]
    public void Subsequence_skips_spaces_in_the_query() =>
        Assert.Equal(100, LauncherSearch.Score("Google Chrome", LauncherSearch.Fold("g chr")));
}
