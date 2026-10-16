using System.Text.Json;
using System.Text.Json.Nodes;

namespace Windock.Core.Settings;

public sealed class SettingsParseException(string message, Exception? inner = null) : Exception(message, inner);

/// Reads settings field by field so one bad value only resets that field (ARCHITECTURE §8).
public static class SettingsJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static DockSettings Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new SettingsParseException("settings.json is not valid JSON", ex);
        }
        if (root is not JsonObject obj) throw new SettingsParseException("settings.json must contain a JSON object");

        var d = SettingsDefaults.Create();
        var a = obj["appearance"] as JsonObject;
        var mag = a?["magnification"] as JsonObject;
        var b = obj["behavior"] as JsonObject;
        var safety = obj["safety"] as JsonObject;

        var settings = new DockSettings(
            Version: Get(obj, "version", d.Version),
            Appearance: new AppearanceSettings(
                Position: GetEnum(a, "position", d.Appearance.Position),
                IconSize: Get(a, "iconSize", d.Appearance.IconSize),
                Magnification: new MagnificationSettings(
                    Enabled: Get(mag, "enabled", d.Appearance.Magnification.Enabled),
                    Size: Get(mag, "size", d.Appearance.Magnification.Size)),
                BackgroundOpacity: Get(a, "backgroundOpacity", d.Appearance.BackgroundOpacity),
                Blur: GetEnum(a, "blur", d.Appearance.Blur),
                ShowRunningIndicators: Get(a, "showRunningIndicators", d.Appearance.ShowRunningIndicators)),
            Behavior: new BehaviorSettings(
                TaskbarMode: GetEnum(b, "taskbarMode", d.Behavior.TaskbarMode),
                AutoHide: Get(b, "autoHide", d.Behavior.AutoHide),
                BounceOnLaunch: Get(b, "bounceOnLaunch", d.Behavior.BounceOnLaunch),
                StartWithWindows: Get(b, "startWithWindows", d.Behavior.StartWithWindows)),
            Pins: ReadPins(obj["pins"] as JsonArray),
            Safety: new SafetySettings(GetString(safety, "emergencyHotkey") ?? d.Safety.EmergencyHotkey),
            FirstRunCompleted: Get(obj, "firstRunCompleted", d.FirstRunCompleted));

        return SettingsNormalizer.Normalize(settings);
    }

    public static string Serialize(DockSettings s)
    {
        var root = new JsonObject
        {
            ["version"] = s.Version,
            ["appearance"] = new JsonObject
            {
                ["position"] = s.Appearance.Position.ToString(),
                ["iconSize"] = s.Appearance.IconSize,
                ["magnification"] = new JsonObject
                {
                    ["enabled"] = s.Appearance.Magnification.Enabled,
                    ["size"] = s.Appearance.Magnification.Size,
                },
                ["backgroundOpacity"] = s.Appearance.BackgroundOpacity,
                ["blur"] = s.Appearance.Blur.ToString(),
                ["showRunningIndicators"] = s.Appearance.ShowRunningIndicators,
            },
            ["behavior"] = new JsonObject
            {
                ["taskbarMode"] = s.Behavior.TaskbarMode.ToString(),
                ["autoHide"] = s.Behavior.AutoHide,
                ["bounceOnLaunch"] = s.Behavior.BounceOnLaunch,
                ["startWithWindows"] = s.Behavior.StartWithWindows,
            },
            ["pins"] = new JsonArray(s.Pins.Select(p => (JsonNode)new JsonObject { ["path"] = p.Path }).ToArray()),
            ["safety"] = new JsonObject { ["emergencyHotkey"] = s.Safety.EmergencyHotkey },
            ["firstRunCompleted"] = s.FirstRunCompleted,
        };
        return root.ToJsonString(Indented);
    }

    private static IReadOnlyList<PinEntry> ReadPins(JsonArray? array)
    {
        if (array is null) return Array.Empty<PinEntry>();
        var pins = new List<PinEntry>();
        foreach (var item in array)
        {
            var path = GetString(item as JsonObject, "path");
            if (!string.IsNullOrWhiteSpace(path)) pins.Add(new PinEntry(path));
        }
        return pins;
    }

    private static T Get<T>(JsonObject? obj, string name, T fallback)
    {
        if (obj?[name] is not JsonValue value) return fallback;
        try
        {
            return value.TryGetValue(out T? result) && result is not null ? result : fallback;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
        {
            return fallback;
        }
    }

    private static string? GetString(JsonObject? obj, string name) =>
        obj?[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static TEnum GetEnum<TEnum>(JsonObject? obj, string name, TEnum fallback) where TEnum : struct, Enum =>
        GetString(obj, name) is { } text && Enum.TryParse<TEnum>(text, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : fallback;
}
