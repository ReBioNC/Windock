using System.Text.RegularExpressions;

namespace Windock.Core.Apps;

/// What Windows' notification store says about one app: its badge (unread count) and its newest toast.
public readonly record struct AppNotifications(int Badge, long LastToast);

/// Turns two readings of Windows' notification store into dock attention changes, keyed by the app's AUMID
/// in lower case (the same form DockModel uses in "aumid:" app ids).
public static class NotificationChanges
{
    /// before = null on the first reading: only apps that already show a badge are marked.
    public static (IReadOnlyList<string> Raise, IReadOnlyList<string> Clear) Compare(
        IReadOnlyDictionary<string, AppNotifications>? before, IReadOnlyDictionary<string, AppNotifications> after)
    {
        var raise = new List<string>();
        var clear = new List<string>();
        foreach (var (app, now) in after)
        {
            if (before is null)
            {
                if (now.Badge > 0) raise.Add(app);
                continue;
            }
            var old = before.GetValueOrDefault(app);
            if (now.LastToast > old.LastToast || now.Badge > old.Badge) raise.Add(app);
            else if (old.Badge > 0 && now.Badge == 0) clear.Add(app);
        }
        if (before is not null)
        {
            // An app whose badge row disappeared has nothing unread any more.
            foreach (var (app, old) in before)
                if (old.Badge > 0 && !after.ContainsKey(app)) clear.Add(app);
        }
        return (raise, clear);
    }

    /// What a numbered badge shows: nothing for 0, the count up to 99, then "99+".
    public static string? BadgeText(int count) => count <= 0 ? null : count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// Badge payloads look like &lt;badge value="3"/&gt;; glyph badges ("newMessage", "attention", …) count as 1, "none" as 0.
    public static int ParseBadge(string? payload)
    {
        if (string.IsNullOrEmpty(payload)) return 0;
        var match = Regex.Match(payload, "value\\s*=\\s*\"([^\"]*)\"");
        if (!match.Success) return 0;
        var value = match.Groups[1].Value.Trim();
        if (int.TryParse(value, out var n)) return Math.Max(0, n);
        return value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }
}
