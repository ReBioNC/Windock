using Windock.Core.Launcher;

namespace Windock.Core.Apps;

/// One window in the Alt+Tab switcher.
public sealed record SwitcherEntry(nint Handle, string AppKey, string AppName, string Title);

/// Order, grouping and search for Windock's Alt+Tab switcher. Input lists are most-recently-used first.
public static class SwitcherList
{
    /// Grouped: apps in the order their most recent window was used, each app's windows together (MRU inside).
    public static IReadOnlyList<SwitcherEntry> Arrange(IReadOnlyList<SwitcherEntry> mru, bool groupByApp)
    {
        if (!groupByApp) return mru;
        var order = new List<string>();
        var byApp = new Dictionary<string, List<SwitcherEntry>>();
        foreach (var e in mru)
        {
            if (!byApp.TryGetValue(e.AppKey, out var list))
            {
                byApp[e.AppKey] = list = new List<SwitcherEntry>();
                order.Add(e.AppKey);
            }
            list.Add(e);
        }
        return order.SelectMany(k => byApp[k]).ToList();
    }

    /// Every word of the query must appear in the app name or window title (case and accents ignored).
    public static IReadOnlyList<SwitcherEntry> Filter(IReadOnlyList<SwitcherEntry> entries, string query)
    {
        var words = LauncherSearch.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return entries;
        return entries.Where(e =>
        {
            var text = LauncherSearch.Fold(e.AppName + " " + e.Title);
            return words.All(w => text.Contains(w, StringComparison.Ordinal));
        }).ToList();
    }

    /// Like Windows: the first Alt+Tab goes to the window used before the current one.
    public static int InitialSelection(IReadOnlyList<SwitcherEntry> arranged, IReadOnlyList<SwitcherEntry> mru)
    {
        if (arranged.Count == 0) return -1;
        if (mru.Count < 2) return 0;
        var index = arranged.ToList().FindIndex(e => e.Handle == mru[1].Handle);
        return index < 0 ? 0 : index;
    }

    /// Next selection, wrapping around.
    public static int Step(int current, int delta, int count) => count == 0 ? -1 : ((current + delta) % count + count) % count;
}
