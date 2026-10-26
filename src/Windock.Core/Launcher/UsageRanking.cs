namespace Windock.Core.Launcher;

/// How often (and when last) something was opened, under one UsageKeys key.
public sealed record UsageRecord(string Key, int Count, DateTime? LastUsedUtc);

/// Picks the launcher's Frequent apps: launch counts weighted toward recent use, so an app used 50 times last
/// year does not beat one used every day.
public static class UsageRanking
{
    /// Two weeks without use halves an app's weight; three months cuts it to about a seventh.
    public const double HalfLifeDays = 14;

    public static double Score(int count, DateTime? lastUsedUtc, DateTime nowUtc)
    {
        if (count <= 0) return 0;
        var weight = lastUsedUtc is { } last ? 1.0 / (1.0 + Math.Max(0, (nowUtc - last).TotalDays) / HalfLifeDays) : 0.25;
        return count * weight;
    }

    /// Entries ranked by the records they match (any of their UsageKeys), best first; one per display name.
    public static IReadOnlyList<LauncherEntry> Frequent(IEnumerable<LauncherEntry> entries, IEnumerable<UsageRecord> records,
        IReadOnlySet<string> hiddenNames, DateTime nowUtc, int max)
    {
        var byKey = new Dictionary<string, List<UsageRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in records)
        {
            if (!byKey.TryGetValue(r.Key, out var list)) byKey[r.Key] = list = new List<UsageRecord>();
            list.Add(r);
        }

        var best = new Dictionary<string, (LauncherEntry Entry, double Score)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.Kind != LauncherEntryKind.App || hiddenNames.Contains(entry.Name)) continue;
            var matched = new HashSet<UsageRecord>(ReferenceEqualityComparer.Instance);
            foreach (var key in entry.UsageKeys)
                if (byKey.TryGetValue(key, out var list)) matched.UnionWith(list);
            var score = matched.Sum(r => Score(r.Count, r.LastUsedUtc, nowUtc));
            if (score <= 0) continue;
            if (!best.TryGetValue(entry.Name, out var current) || score > current.Score) best[entry.Name] = (entry, score);
        }
        return best.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Max(0, max))
            .Select(x => x.Entry)
            .ToList();
    }
}
