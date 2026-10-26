using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Windock.Core.Island;

/// A task on the island's Today list. Due: a time today to be reminded at.
public sealed record IslandTask(string Id, string Text, TimeSpan? Due, bool Done, DateTime Created, bool Reminded = false);

public static class TaskList
{
    // "Call mom 14:30", "Call mom 9.15", "Call mom 9pm", "Call mom 9:30 am"
    private static readonly Regex TimeAtEnd = new(@"\s+(\d{1,2})(?:[:.](\d{2}))?\s*(am|pm)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// The task text and the time at its end, if there is one.
    public static (string Text, TimeSpan? Due) ParseInput(string input)
    {
        var text = input.Trim();
        var m = TimeAtEnd.Match(text);
        if (!m.Success || (!m.Groups[2].Success && !m.Groups[3].Success)) return (text, null);  // a bare number is part of the text
        var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        if (m.Groups[3].Success)
        {
            if (hour is < 1 or > 12) return (text, null);
            var pm = m.Groups[3].Value.Equals("pm", StringComparison.OrdinalIgnoreCase);
            hour = hour % 12 + (pm ? 12 : 0);
        }
        if (hour > 23 || minute > 59) return (text, null);
        var rest = text[..m.Index].Trim();
        return rest.Length == 0 ? (text, null) : (rest, new TimeSpan(hour, minute, 0));
    }

    public static IslandTask Create(string input, DateTime now)
    {
        var (text, due) = ParseInput(input);
        return new IslandTask(Guid.NewGuid().ToString("N"), text, due, false, now);
    }

    /// What the Today list shows: open tasks (oldest first), then today's finished ones.
    public static IReadOnlyList<IslandTask> Visible(IEnumerable<IslandTask> tasks, DateTime now) =>
        tasks.Where(t => !t.Done || t.Created.Date == now.Date)
            .OrderBy(t => t.Done).ThenBy(t => t.Due ?? TimeSpan.MaxValue).ThenBy(t => t.Created).ToList();

    /// Finished tasks from earlier days are dropped (the list is for today).
    public static IReadOnlyList<IslandTask> Prune(IEnumerable<IslandTask> tasks, DateTime now) =>
        tasks.Where(t => !t.Done || t.Created.Date >= now.Date).ToList();

    /// Open tasks made today whose time has come and that have not reminded yet.
    public static IReadOnlyList<IslandTask> DueReminders(IEnumerable<IslandTask> tasks, DateTime now) =>
        tasks.Where(t => !t.Done && !t.Reminded && t.Due is { } due && t.Created.Date == now.Date && due <= now.TimeOfDay).ToList();

    public static string Serialize(IEnumerable<IslandTask> tasks) =>
        new JsonArray(tasks.Select(t => (JsonNode)new JsonObject
        {
            ["id"] = t.Id, ["text"] = t.Text, ["due"] = t.Due?.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
            ["done"] = t.Done, ["created"] = t.Created.ToString("o", CultureInfo.InvariantCulture), ["reminded"] = t.Reminded,
        }).ToArray()).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public static IReadOnlyList<IslandTask> Parse(string json)
    {
        var list = new List<IslandTask>();
        try
        {
            if (JsonNode.Parse(json) is not JsonArray array) return list;
            foreach (var node in array.OfType<JsonObject>())
            {
                var text = (string?)node["text"];
                if (string.IsNullOrWhiteSpace(text)) continue;
                TimeSpan? due = TimeSpan.TryParseExact((string?)node["due"], @"hh\:mm", CultureInfo.InvariantCulture, out var d) ? d : null;
                var created = DateTime.TryParse((string?)node["created"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var c) ? c : DateTime.Now;
                list.Add(new IslandTask((string?)node["id"] ?? Guid.NewGuid().ToString("N"), text, due,
                    node["done"] is JsonValue dv && dv.TryGetValue(out bool done) && done, created,
                    node["reminded"] is JsonValue rv && rv.TryGetValue(out bool reminded) && reminded));
            }
        }
        catch (JsonException)
        {
        }
        return list;
    }
}

/// The island's quick notes: a list of texts, always at least one (possibly empty).
public static class NoteBook
{
    public static string Serialize(IEnumerable<string> notes) =>
        new JsonArray(notes.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public static List<string> Parse(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonArray array)
            {
                var notes = array.Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s : null).OfType<string>().ToList();
                if (notes.Count > 0) return notes;
            }
        }
        catch (JsonException)
        {
        }
        return new List<string> { "" };
    }
}
