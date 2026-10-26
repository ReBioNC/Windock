using System.Text.Json;
using System.Text.Json.Nodes;

namespace Windock.Core.Island;

/// Texts copied recently, newest first. Kept in memory only (never written to disk).
public sealed class ClipboardHistory
{
    public const int MaxItems = 20, MaxLength = 10_000;

    private readonly List<string> _items = new();

    public IReadOnlyList<string> Items => _items;

    /// A copy again moves it to the top; blank text is ignored; very long text is cut.
    public bool Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length > MaxLength) text = text[..MaxLength];
        if (_items.Count > 0 && _items[0] == text) return false;
        _items.Remove(text);
        _items.Insert(0, text);
        if (_items.Count > MaxItems) _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        return true;
    }

    public void Clear() => _items.Clear();

    /// One line for the list: line breaks and runs of spaces collapse to single spaces.
    public static string Preview(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// Files and folders dropped on the island's shelf, in the order they came.
public static class Shelf
{
    public const int MaxItems = 24;

    public static List<string> Add(IEnumerable<string> shelf, IEnumerable<string> dropped)
    {
        var list = shelf.ToList();
        foreach (var path in dropped)
        {
            if (string.IsNullOrWhiteSpace(path) || list.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            list.Add(path);
        }
        return list.Count > MaxItems ? list.Skip(list.Count - MaxItems).ToList() : list;
    }

    /// Items whose file was deleted or moved meanwhile are dropped.
    public static List<string> Prune(IEnumerable<string> shelf, Func<string, bool> exists) => shelf.Where(exists).ToList();

    public static string Serialize(IEnumerable<string> shelf) =>
        new JsonArray(shelf.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public static List<string> Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonArray array
                ? array.Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s : null).OfType<string>().ToList()
                : new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}
