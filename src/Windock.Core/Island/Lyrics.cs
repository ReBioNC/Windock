using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Windock.Core.Island;

public sealed record LyricLine(TimeSpan Time, string Text);

/// Time-synced lyrics in the LRC format ("[01:14.32]Not that I miss you").
public static class Lrc
{
    private static readonly Regex Tag = new(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]", RegexOptions.Compiled);

    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrc)) return lines;
        foreach (var raw in lrc.Split('\n'))
        {
            var matches = Tag.Matches(raw);
            if (matches.Count == 0) continue;
            var text = raw[(matches[^1].Index + matches[^1].Length)..].Trim();
            if (text.Length == 0) text = "♪";  // an instrumental gap
            foreach (Match m in matches)
            {
                var seconds = double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                lines.Add(new LyricLine(TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) + TimeSpan.FromSeconds(seconds), text));
            }
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    /// The line being sung at position: the last one that has started; -1 before the first.
    public static int LineAt(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        var index = -1;
        for (var i = 0; i < lines.Count && lines[i].Time <= position; i++) index = i;
        return index;
    }
}

/// Looking songs up on LRCLIB (lrclib.net, free, no key). Only the title, artist, album and length are sent.
public static class LyricsQuery
{
    private static readonly Regex Noise = new(
        @"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|mv|hd|4k|visuali[sz]er|remaster(ed)?|live|color coded)\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// Browser players report video titles: "Artist - Song (Official Video)" with the channel as artist.
    public static (string Title, string Artist) Clean(string title, string artist)
    {
        var t = Noise.Replace(title, "").Trim();
        var a = artist.Replace(" - Topic", "", StringComparison.OrdinalIgnoreCase).Replace("VEVO", "", StringComparison.Ordinal).Trim();
        var dash = t.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && (a.Length == 0 || t.StartsWith(a, StringComparison.OrdinalIgnoreCase) || !t.Contains(a, StringComparison.OrdinalIgnoreCase)))
        {
            a = t[..dash].Trim();
            t = t[(dash + 3)..].Trim();
        }
        return (t, a);
    }

    public static string GetUrl(string title, string artist, string album, TimeSpan duration)
    {
        var url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
        if (album.Length > 0) url += $"&album_name={Uri.EscapeDataString(album)}";
        if (duration > TimeSpan.Zero) url += $"&duration={(int)Math.Round(duration.TotalSeconds)}";
        return url;
    }

    public static string SearchUrl(string title, string artist) =>
        $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";

    /// A /get reply (one object) or a /search reply (a list): the first synced lyrics, or null.
    public static IReadOnlyList<LyricLine>? ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            IEnumerable<JsonElement> items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : new[] { root };
            foreach (var item in items)
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("syncedLyrics", out var synced) || synced.ValueKind != JsonValueKind.String) continue;
                var lines = Lrc.Parse(synced.GetString());
                if (lines.Count > 0) return lines;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
