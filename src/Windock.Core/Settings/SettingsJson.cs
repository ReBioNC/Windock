using System.Text.Json;
using System.Text.Json.Nodes;
using Windock.Core.Launcher;

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
        var theme = obj["theme"] as JsonObject;
        var status = obj["status"] as JsonObject;
        var launcher = obj["launcher"] as JsonObject;
        var switcher = obj["switcher"] as JsonObject;
        var trash = obj["trash"] as JsonObject;
        var island = obj["island"] as JsonObject;
        var ds = d.Status;
        var di = d.Island;

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
                AutoHide: ReadAutoHide(b, d.Behavior.AutoHide),
                BounceOnLaunch: Get(b, "bounceOnLaunch", d.Behavior.BounceOnLaunch),
                StartWithWindows: Get(b, "startWithWindows", d.Behavior.StartWithWindows),
                ExplorerContextMenu: Get(b, "explorerContextMenu", d.Behavior.ExplorerContextMenu),
                Mode: GetEnum(b, "mode", d.Behavior.Mode),
                Attention: GetEnum(b, "attention", d.Behavior.Attention),
                AttentionRepeat: GetEnum(b, "attentionRepeat", d.Behavior.AttentionRepeat),
                AttentionFromNotifications: Get(b, "attentionFromNotifications", d.Behavior.AttentionFromNotifications),
                ShowBadgeCounts: Get(b, "showBadgeCounts", d.Behavior.ShowBadgeCounts)),
            Pins: ReadPins(obj["pins"] as JsonArray),
            Safety: new SafetySettings(GetString(safety, "emergencyHotkey") ?? d.Safety.EmergencyHotkey),
            FirstRunCompleted: Get(obj, "firstRunCompleted", d.FirstRunCompleted))
        {
            Theme = new ThemeSettings(
                PlateColor: GetString(theme, "plateColor") ?? d.Theme.PlateColor,
                CornerRadius: Get(theme, "cornerRadius", d.Theme.CornerRadius),
                ShowBorder: Get(theme, "showBorder", d.Theme.ShowBorder),
                IndicatorColor: GetString(theme, "indicatorColor") ?? d.Theme.IndicatorColor,
                AccentColor: GetString(theme, "accentColor") ?? d.Theme.AccentColor,
                FollowWallpaper: Get(theme, "followWallpaper", d.Theme.FollowWallpaper)),
            Status = new StatusSettings(
                Enabled: Get(status, "enabled", ds.Enabled),
                ShowTray: Get(status, "showTray", ds.ShowTray),
                ShowWifi: Get(status, "showWifi", ds.ShowWifi),
                ShowVolume: Get(status, "showVolume", ds.ShowVolume),
                ShowBattery: Get(status, "showBattery", ds.ShowBattery),
                ShowBatteryPercent: Get(status, "showBatteryPercent", ds.ShowBatteryPercent),
                ShowLanguage: Get(status, "showLanguage", ds.ShowLanguage),
                ShowClock: Get(status, "showClock", ds.ShowClock),
                Clock24Hour: Get(status, "clock24Hour", ds.Clock24Hour),
                ShowDate: Get(status, "showDate", ds.ShowDate),
                ShowSeconds: Get(status, "showSeconds", ds.ShowSeconds),
                ShowNotifications: Get(status, "showNotifications", ds.ShowNotifications)),
            Launcher = new LauncherSettings(
                Enabled: Get(launcher, "enabled", d.Launcher.Enabled),
                Hotkey: GetString(launcher, "hotkey") ?? d.Launcher.Hotkey,
                MaxResults: Get(launcher, "maxResults", d.Launcher.MaxResults),
                IncludeFiles: Get(launcher, "includeFiles", d.Launcher.IncludeFiles))
            {
                Layout = GetEnum(launcher, "layout", d.Launcher.Layout),
                ShowCategories = Get(launcher, "showCategories", d.Launcher.ShowCategories),
                UseWindowsHistory = Get(launcher, "useWindowsHistory", d.Launcher.UseWindowsHistory),
                TrackLaunches = Get(launcher, "trackLaunches", d.Launcher.TrackLaunches),
                FrequentCount = Get(launcher, "frequentCount", d.Launcher.FrequentCount),
                Columns = Get(launcher, "columns", d.Launcher.Columns),
                CategoryOverrides = ReadCategoryOverrides(launcher?["categories"] as JsonArray),
            },
            Switcher = new SwitcherSettings(
                Enabled: Get(switcher, "enabled", d.Switcher.Enabled),
                GroupByApp: Get(switcher, "groupByApp", d.Switcher.GroupByApp),
                CardSize: GetEnum(switcher, "cardSize", d.Switcher.CardSize),
                CurrentScreenOnly: Get(switcher, "currentScreenOnly", d.Switcher.CurrentScreenOnly),
                LivePreviews: Get(switcher, "livePreviews", d.Switcher.LivePreviews),
                WindowsSwitcherInFullscreen: Get(switcher, "windowsSwitcherInFullscreen", d.Switcher.WindowsSwitcherInFullscreen)),
            Trash = new TrashSettings(
                ShowRecycleBin: Get(trash, "showRecycleBin", d.Trash.ShowRecycleBin),
                ConfirmBeforeTrash: Get(trash, "confirmBeforeTrash", d.Trash.ConfirmBeforeTrash)),
            HiddenApps = ReadHiddenApps(obj["hiddenApps"] as JsonArray),
            Island = new IslandSettings(
                Enabled: Get(island, "enabled", di.Enabled),
                Visibility: GetEnum(island, "visibility", di.Visibility),
                ShowClock: Get(island, "showClock", di.ShowClock),
                ShowDate: Get(island, "showDate", di.ShowDate),
                ShowBattery: Get(island, "showBattery", di.ShowBattery),
                Media: Get(island, "media", di.Media),
                VolumeEvents: Get(island, "volumeEvents", di.VolumeEvents),
                BrightnessEvents: Get(island, "brightnessEvents", di.BrightnessEvents),
                BatteryEvents: Get(island, "batteryEvents", di.BatteryEvents),
                DeviceEvents: Get(island, "deviceEvents", di.DeviceEvents),
                ExpandOnHover: Get(island, "expandOnHover", di.ExpandOnHover),
                EventSeconds: Get(island, "eventSeconds", di.EventSeconds),
                Size: GetEnum(island, "size", di.Size),
                UseDockColor: Get(island, "useDockColor", di.UseDockColor),
                DevicesPanel: Get(island, "devicesPanel", di.DevicesPanel),
                WeatherPanel: Get(island, "weatherPanel", di.WeatherPanel),
                City: GetString(island, "city") ?? di.City,
                Unit: GetEnum(island, "unit", di.Unit),
                Place: ReadPlace(island?["place"] as JsonObject),
                Mascot: Get(island, "mascot", di.Mascot)),
        };

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
                ["autoHide"] = s.Behavior.AutoHide.ToString(),
                ["bounceOnLaunch"] = s.Behavior.BounceOnLaunch,
                ["startWithWindows"] = s.Behavior.StartWithWindows,
                ["explorerContextMenu"] = s.Behavior.ExplorerContextMenu,
                ["mode"] = s.Behavior.Mode.ToString(),
                ["attention"] = s.Behavior.Attention.ToString(),
                ["attentionRepeat"] = s.Behavior.AttentionRepeat.ToString(),
                ["attentionFromNotifications"] = s.Behavior.AttentionFromNotifications,
                ["showBadgeCounts"] = s.Behavior.ShowBadgeCounts,
            },
            ["pins"] = new JsonArray(s.Pins.Select(p => (JsonNode)new JsonObject { ["path"] = p.Path }).ToArray()),
            ["safety"] = new JsonObject { ["emergencyHotkey"] = s.Safety.EmergencyHotkey },
            ["firstRunCompleted"] = s.FirstRunCompleted,
            ["theme"] = new JsonObject
            {
                ["plateColor"] = s.Theme.PlateColor,
                ["cornerRadius"] = s.Theme.CornerRadius,
                ["showBorder"] = s.Theme.ShowBorder,
                ["indicatorColor"] = s.Theme.IndicatorColor,
                ["accentColor"] = s.Theme.AccentColor,
                ["followWallpaper"] = s.Theme.FollowWallpaper,
            },
            ["status"] = new JsonObject
            {
                ["enabled"] = s.Status.Enabled,
                ["showTray"] = s.Status.ShowTray,
                ["showWifi"] = s.Status.ShowWifi,
                ["showVolume"] = s.Status.ShowVolume,
                ["showBattery"] = s.Status.ShowBattery,
                ["showBatteryPercent"] = s.Status.ShowBatteryPercent,
                ["showLanguage"] = s.Status.ShowLanguage,
                ["showClock"] = s.Status.ShowClock,
                ["clock24Hour"] = s.Status.Clock24Hour,
                ["showDate"] = s.Status.ShowDate,
                ["showSeconds"] = s.Status.ShowSeconds,
                ["showNotifications"] = s.Status.ShowNotifications,
            },
            ["launcher"] = new JsonObject
            {
                ["enabled"] = s.Launcher.Enabled,
                ["hotkey"] = s.Launcher.Hotkey,
                ["maxResults"] = s.Launcher.MaxResults,
                ["includeFiles"] = s.Launcher.IncludeFiles,
                ["layout"] = s.Launcher.Layout.ToString(),
                ["showCategories"] = s.Launcher.ShowCategories,
                ["useWindowsHistory"] = s.Launcher.UseWindowsHistory,
                ["trackLaunches"] = s.Launcher.TrackLaunches,
                ["frequentCount"] = s.Launcher.FrequentCount,
                ["columns"] = s.Launcher.Columns,
                ["categories"] = new JsonArray(s.Launcher.CategoryOverrides
                    .Select(o => (JsonNode)new JsonObject { ["name"] = o.Name, ["category"] = o.Category.ToString() }).ToArray()),
            },
            ["switcher"] = new JsonObject
            {
                ["enabled"] = s.Switcher.Enabled,
                ["groupByApp"] = s.Switcher.GroupByApp,
                ["cardSize"] = s.Switcher.CardSize.ToString(),
                ["currentScreenOnly"] = s.Switcher.CurrentScreenOnly,
                ["livePreviews"] = s.Switcher.LivePreviews,
                ["windowsSwitcherInFullscreen"] = s.Switcher.WindowsSwitcherInFullscreen,
            },
            ["trash"] = new JsonObject
            {
                ["showRecycleBin"] = s.Trash.ShowRecycleBin,
                ["confirmBeforeTrash"] = s.Trash.ConfirmBeforeTrash,
            },
            ["island"] = new JsonObject
            {
                ["enabled"] = s.Island.Enabled,
                ["visibility"] = s.Island.Visibility.ToString(),
                ["showClock"] = s.Island.ShowClock,
                ["showDate"] = s.Island.ShowDate,
                ["showBattery"] = s.Island.ShowBattery,
                ["media"] = s.Island.Media,
                ["volumeEvents"] = s.Island.VolumeEvents,
                ["brightnessEvents"] = s.Island.BrightnessEvents,
                ["batteryEvents"] = s.Island.BatteryEvents,
                ["deviceEvents"] = s.Island.DeviceEvents,
                ["expandOnHover"] = s.Island.ExpandOnHover,
                ["eventSeconds"] = s.Island.EventSeconds,
                ["size"] = s.Island.Size.ToString(),
                ["useDockColor"] = s.Island.UseDockColor,
                ["devicesPanel"] = s.Island.DevicesPanel,
                ["weatherPanel"] = s.Island.WeatherPanel,
                ["city"] = s.Island.City,
                ["unit"] = s.Island.Unit.ToString(),
                ["place"] = s.Island.Place is { } place
                    ? new JsonObject { ["query"] = place.Query, ["name"] = place.Name, ["latitude"] = place.Latitude, ["longitude"] = place.Longitude }
                    : null,
                ["mascot"] = s.Island.Mascot,
            },
            ["hiddenApps"] = new JsonArray(s.HiddenApps.Select(h => (JsonNode)new JsonObject { ["appId"] = h.AppId, ["name"] = h.Name }).ToArray()),
        };
        return root.ToJsonString(Indented);
    }

    /// Earlier versions stored an unused boolean here; true meant "hide", so it maps to Always.
    private static DockAutoHide ReadAutoHide(JsonObject? behavior, DockAutoHide fallback) =>
        behavior?["autoHide"] is JsonValue v && v.TryGetValue(out bool flag)
            ? flag ? DockAutoHide.Always : DockAutoHide.Never
            : GetEnum(behavior, "autoHide", fallback);

    private static WeatherPlace? ReadPlace(JsonObject? place)
    {
        if (GetString(place, "query") is not { } query || GetString(place, "name") is not { } name) return null;
        var lat = Get(place, "latitude", double.NaN);
        var lon = Get(place, "longitude", double.NaN);
        return double.IsFinite(lat) && double.IsFinite(lon) ? new WeatherPlace(query, name, lat, lon) : null;
    }

    private static IReadOnlyList<HiddenApp> ReadHiddenApps(JsonArray? array)
    {
        if (array is null) return Array.Empty<HiddenApp>();
        var apps = new List<HiddenApp>();
        foreach (var item in array)
        {
            var id = GetString(item as JsonObject, "appId");
            if (string.IsNullOrWhiteSpace(id)) continue;
            apps.Add(new HiddenApp(id, GetString(item as JsonObject, "name") ?? id));
        }
        return apps;
    }

    private static IReadOnlyList<LauncherCategoryOverride> ReadCategoryOverrides(JsonArray? array)
    {
        if (array is null) return Array.Empty<LauncherCategoryOverride>();
        var list = new List<LauncherCategoryOverride>();
        foreach (var item in array)
        {
            var o = item as JsonObject;
            if (GetString(o, "name") is not { Length: > 0 } name) continue;
            var category = GetEnum(o, "category", (LauncherCategory)(-1));
            if (Enum.IsDefined(category)) list.Add(new LauncherCategoryOverride(name, category));
        }
        return list;
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
