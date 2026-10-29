using System.Text.Json;
using System.Text.Json.Nodes;

namespace Windock.Core.Island;

/// One copy: a text, or a picture. ImageKey identifies the picture's content (the same image copied twice has the
/// same key); Picture is the app's own image data, which the history only holds on to.
public sealed record ClipItem(string? Text, string? ImageKey = null, object? Picture = null, int Width = 0, int Height = 0)
{
    public bool IsImage => ImageKey is not null;

    public bool SameAs(ClipItem other) => IsImage ? ImageKey == other.ImageKey : !other.IsImage && Text == other.Text;
}

/// Texts and pictures copied recently, newest first. Kept in memory only (never written to disk). Pictures take
/// far more memory than texts, so only the newest few are kept.
public sealed class ClipboardHistory
{
    public const int MaxItems = 20, MaxLength = 10_000, MaxImages = 5;

    private readonly List<ClipItem> _items = new();

    public IReadOnlyList<ClipItem> Items => _items;

    /// A copy again moves it to the top; blank text is ignored; very long text is cut.
    public bool Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length > MaxLength) text = text[..MaxLength];
        return Add(new ClipItem(text));
    }

    public bool AddImage(string key, object picture, int width, int height) =>
        !string.IsNullOrEmpty(key) && Add(new ClipItem(null, key, picture, width, height));

    private bool Add(ClipItem item)
    {
        if (_items.Count > 0 && _items[0].SameAs(item)) return false;
        _items.RemoveAll(i => i.SameAs(item));
        _items.Insert(0, item);
        if (_items.Count > MaxItems) _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        var images = _items.Where(i => i.IsImage).Skip(MaxImages).ToList();
        _items.RemoveAll(images.Contains);
        return true;
    }

    public void RemoveImages() => _items.RemoveAll(i => i.IsImage);

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
