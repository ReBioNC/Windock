using System.Text.Json;
using System.Text.Json.Nodes;
using Windock.Core.Logging;

namespace Windock.Core.Launcher;

/// Apps opened through Windock (dock and launcher), for the launcher's Frequent tab, plus apps the user removed
/// from Frequent. Kept in usage.json next to settings.json; a broken file starts over empty.
public sealed class UsageStore
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private readonly string _path;
    private readonly ILog _log;
    private readonly Dictionary<string, UsageRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    public UsageStore(string directory, ILog log)
    {
        _path = Path.Combine(directory, "usage.json");
        _log = log;
        Load();
    }

    public IReadOnlyCollection<UsageRecord> Records => _records.Values;

    /// Display names left out of Frequent.
    public IReadOnlySet<string> HiddenNames => _hidden;

    public void Record(string key, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var count = _records.TryGetValue(key, out var r) ? r.Count : 0;
        _records[key] = new UsageRecord(key, count + 1, nowUtc);
        Save();
    }

    public void Hide(string name)
    {
        if (_hidden.Add(name)) Save();
    }

    public void Clear()
    {
        _records.Clear();
        _hidden.Clear();
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject root) return;
            if (root["apps"] is JsonObject apps)
                foreach (var (key, value) in apps)
                {
                    if (value is not JsonObject app || app["count"] is not JsonValue c || !c.TryGetValue(out int count) || count <= 0) continue;
                    DateTime? last = app["last"] is JsonValue l && l.TryGetValue(out DateTime t) ? t.ToUniversalTime() : null;
                    _records[key] = new UsageRecord(key, count, last);
                }
            if (root["hidden"] is JsonArray hidden)
                foreach (var item in hidden)
                    if (item is JsonValue v && v.TryGetValue(out string? name) && !string.IsNullOrWhiteSpace(name)) _hidden.Add(name);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            _log.Error("usage.json could not be read; starting over", ex);
            _records.Clear();
            _hidden.Clear();
        }
    }

    private void Save()
    {
        var apps = new JsonObject();
        foreach (var r in _records.Values.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            var app = new JsonObject { ["count"] = r.Count };
            if (r.LastUsedUtc is { } last) app["last"] = last;
            apps[r.Key] = app;
        }
        var root = new JsonObject { ["version"] = 1, ["apps"] = apps, ["hidden"] = new JsonArray(_hidden.Order().Select(h => (JsonNode)h).ToArray()) };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(Indented));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("usage.json could not be saved", ex);
        }
    }
}
