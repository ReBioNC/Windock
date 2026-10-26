using System.Globalization;
using System.Text;

namespace Windock.Core.Launcher;

public enum LauncherEntryKind { App, File }

/// Something the launcher can open. Target is passed to the shell (path, or shell:AppsFolder\…).
/// UsageKeys tie it to launch history (UsageKeys.ForTarget); the first one is what a launch is recorded under.
public sealed record LauncherEntry(string Name, string Target, LauncherEntryKind Kind)
{
    public IReadOnlyList<string> UsageKeys { get; init; } = Array.Empty<string>();
    public LauncherCategory Category { get; init; } = LauncherCategory.Other;
}

/// Ranks launcher entries for a query: name starts with it > a word starts with it > contains it > its letters in order.
/// Apps come before files at the same rank; then shorter names, then alphabetical (spec v0.6 §9).
public static class LauncherSearch
{
    private const int Prefix = 400, WordStart = 300, Contains = 200, Subsequence = 100;

    public static IReadOnlyList<LauncherEntry> Search(IEnumerable<LauncherEntry> entries, string query, int max)
    {
        var q = Fold(query.Trim());
        if (q.Length == 0 || max <= 0) return Array.Empty<LauncherEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return entries
            .Select(e => (Entry: e, Score: Score(e.Name, q)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Kind)
            .ThenBy(x => x.Entry.Name.Length)
            .ThenBy(x => x.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Where(x => seen.Add(x.Entry.Kind + "|" + x.Entry.Name))
            .Take(max)
            .Select(x => x.Entry)
            .ToList();
    }

    /// 0 = no match. foldedQuery must already be passed through Fold.
    public static int Score(string name, string foldedQuery)
    {
        var n = Fold(name);
        if (n.StartsWith(foldedQuery, StringComparison.Ordinal)) return Prefix;
        var index = n.IndexOf(foldedQuery, StringComparison.Ordinal);
        if (index > 0)
        {
            for (var i = index; i >= 0; i = i + 1 < n.Length ? n.IndexOf(foldedQuery, i + 1, StringComparison.Ordinal) : -1)
                if (!char.IsLetterOrDigit(n[i - 1])) return WordStart;
            return Contains;
        }
        var at = 0;
        foreach (var c in foldedQuery)
        {
            if (c == ' ') continue;
            at = n.IndexOf(c, at);
            if (at < 0) return 0;
            at++;
        }
        return Subsequence;
    }

    /// Lower case without accents, so "cafe" finds "Café".
    public static string Fold(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }
}
