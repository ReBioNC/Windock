using System.Net.Http;
using Windock.Core.Island;
using Windock.Core.Logging;

namespace Windock.Island;

/// Synced lyrics for the Music page from LRCLIB, fetched when the open card needs them and kept for the last
/// songs (found or not), so replaying a song or reopening the card asks nothing again.
internal sealed class LyricsService(ILog log)
{
    private const int CacheSize = 40;

    private readonly Dictionary<string, IReadOnlyList<LyricLine>?> _cache = new();
    private readonly Queue<string> _order = new();
    private HttpClient? _http;
    private string? _loading;
    private bool _offline;

    /// Raised on the UI thread when a lookup finished.
    public event Action? Changed;

    /// The lyrics for the song, or null with a message saying why (loading, none, offline).
    public (IReadOnlyList<LyricLine>? Lines, string? Message) For(MediaState media, TimeSpan duration)
    {
        var (title, artist) = LyricsQuery.Clean(media.Title, media.Artist);
        var key = $"{artist}\n{title}".ToLowerInvariant();
        if (_cache.TryGetValue(key, out var lines))
            return lines is null ? (null, "No synced lyrics for this song") : (lines, null);
        if (_loading != key) Fetch(key, title, artist, media.Album, duration);
        return (null, _offline ? "Lyrics need an internet connection" : "Looking for lyrics…");
    }

    private async void Fetch(string key, string title, string artist, string album, TimeSpan duration)
    {
        _loading = key;
        IReadOnlyList<LyricLine>? lines = null;
        try
        {
            var get = await Http().GetAsync(LyricsQuery.GetUrl(title, artist, album, duration));
            if (get.IsSuccessStatusCode) lines = LyricsQuery.ParseResponse(await get.Content.ReadAsStringAsync());
            lines ??= LyricsQuery.ParseResponse(await Http().GetStringAsync(LyricsQuery.SearchUrl(title, artist)));
            _offline = false;
            Remember(key, lines);
            log.Info($"lyrics: '{artist} - {title}' -> {(lines is null ? "none" : $"{lines.Count} lines")}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.Error("lyrics: lookup failed", ex);
            _offline = true;  // not cached: tried again next time the card opens
        }
        finally
        {
            if (_loading == key) _loading = null;
        }
        Changed?.Invoke();
    }

    private void Remember(string key, IReadOnlyList<LyricLine>? lines)
    {
        if (!_cache.ContainsKey(key)) _order.Enqueue(key);
        _cache[key] = lines;
        while (_order.Count > CacheSize) _cache.Remove(_order.Dequeue());
    }

    private HttpClient Http()
    {
        if (_http is not null) return _http;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // LRCLIB asks clients to say who they are.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Windock (https://github.com/ReBioNC/Windock)");
        return _http;
    }
}
