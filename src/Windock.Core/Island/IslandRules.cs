using Windock.Core.Settings;
using Windock.Core.Status;

namespace Windock.Core.Island;

/// Whether the island is on screen (DESIGN: Dynamic Island, visibility).
public static class IslandVisibilityPolicy
{
    /// suppressed: a fullscreen app is in front, or Windows mode. covered: the window in front overlaps the island.
    /// attention: the pointer is at the top edge or on the island, a pop-up event is showing, or the card is expanded.
    public static bool ShouldShow(bool enabled, bool suppressed, IslandVisibility visibility, bool covered, bool desktopInFront, bool attention) =>
        enabled && !suppressed && (visibility == IslandVisibility.Always || !covered || desktopInFront || attention);
}

public sealed record PowerReading(int Percent, bool Charging, bool PluggedIn);

/// Turns two power readings into an island event: plugged in, unplugged, or dropped below a low mark.
public static class BatteryEvents
{
    public static readonly int[] LowMarks = { 20, 10 };

    public static IslandEvent? Diff(PowerReading? before, PowerReading? after)
    {
        if (before is null || after is null) return null;
        var glyph = StatusGlyphs.BatteryLevel(after.Percent, after.Charging);
        var percent = $"{after.Percent}%";
        if (after.PluggedIn && !before.PluggedIn)
            return new IslandEvent(IslandEventKind.Battery, glyph, after.Charging || after.Percent < 100 ? "Charging" : "Plugged in", Detail: percent);
        if (!after.PluggedIn && before.PluggedIn)
            return new IslandEvent(IslandEventKind.Battery, glyph, "On battery", Detail: percent);
        if (!after.PluggedIn && LowMarks.Any(m => before.Percent > m && after.Percent <= m))
            return new IslandEvent(IslandEventKind.Battery, glyph, after.Percent <= LowMarks[^1] ? "Battery very low" : "Battery low", Detail: percent);
        return null;
    }
}

public enum DeviceKind { Keyboard, Mouse, Display, Audio, Bluetooth }

/// One physical device announces several interfaces at once: a USB receiver is a keyboard and a mouse, many mice
/// also expose a keyboard collection. Arrivals (or removals) within a short burst become one event: the mouse if
/// one is in the burst, else the keyboard, else the display. The app collects for BurstWindow, then calls Resolve.
public sealed class DeviceBurst
{
    public static readonly TimeSpan BurstWindow = TimeSpan.FromMilliseconds(500);

    private readonly List<(DeviceKind Kind, bool Arrived, string? Name)> _items = new();

    public bool IsEmpty => _items.Count == 0;

    public void Add(DeviceKind kind, bool arrived, string? name) => _items.Add((kind, arrived, name));

    /// The event for the burst (null when empty); clears it.
    public IslandEvent? Resolve()
    {
        if (_items.Count == 0) return null;
        var arrived = _items[^1].Arrived;
        var same = _items.Where(i => i.Arrived == arrived).ToList();
        var kind = same.Any(i => i.Kind == DeviceKind.Mouse) ? DeviceKind.Mouse
            : same.Any(i => i.Kind == DeviceKind.Keyboard) ? DeviceKind.Keyboard : DeviceKind.Display;
        var name = same.Where(i => i.Kind == kind).Select(i => i.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                   ?? same.Select(i => i.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        _items.Clear();
        return new IslandEvent(IslandEventKind.Device, Glyph(kind), Text(kind, arrived), Detail: name);
    }

    public static string Glyph(DeviceKind kind) => kind switch
    {
        DeviceKind.Keyboard => IslandGlyphs.Keyboard,
        DeviceKind.Mouse => IslandGlyphs.Mouse,
        DeviceKind.Audio => IslandGlyphs.Speaker,
        DeviceKind.Bluetooth => IslandGlyphs.Bluetooth,
        _ => IslandGlyphs.Display,
    };

    public static string Text(DeviceKind kind, bool arrived) => $"{kind} {(arrived ? "connected" : "disconnected")}";
}

/// A readable player name from a media session's app id ("Spotify.exe", "chrome", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic").
public static class MediaAppName
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["spotify"] = "Spotify", ["opera"] = "Opera",
        ["brave"] = "Brave", ["vlc"] = "VLC", ["ZuneMusic"] = "Groove Music", ["ZuneVideo"] = "Movies & TV",
        ["SpotifyMusic"] = "Spotify", ["foobar2000"] = "foobar2000", ["wmplayer"] = "Media Player",
    };

    public static string From(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return "";
        var name = appId.Trim();
        if (name.Contains('!')) name = name[(name.IndexOf('!') + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        name = name[(name.LastIndexOfAny(new[] { '\\', '.' }) + 1)..];
        if (name.Length == 0) return "";
        return Known.TryGetValue(name, out var known) ? known : char.ToUpperInvariant(name[0]) + name[1..];
    }
}

/// Segoe MDL2 Assets glyphs used only by the island.
public static class IslandGlyphs
{
    public const string Keyboard = "";
    public const string Mouse = "";
    public const string Display = "";
    public const string Brightness = "";
    public const string Play = "";
    public const string Pause = "";
    public const string Previous = "";
    public const string Next = "";
    public const string Music = "";
    public const string Speaker = "";
    public const string Bluetooth = "";
}
