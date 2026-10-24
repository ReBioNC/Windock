using System.Globalization;

namespace Windock.Core.Status;

public enum NetworkKind { None, Wifi, Ethernet }

/// Glyphs from the Segoe MDL2 Assets font (built into Windows 10).
public static class StatusGlyphs
{
    public const string ChevronUp = "";
    public const string Wifi = "";
    public const string Ethernet = "";
    public const string NoNetwork = "";
    public const string Mute = "";
    public const string Notifications = "";
    private static readonly string[] VolumeLevels = { "", "", "", "" };
    private static readonly string[] Battery = { "", "", "", "", "", "", "", "", "", "", "" };
    private static readonly string[] Charging = { "", "", "", "", "", "", "", "", "", "", "" };

    public static string Network(NetworkKind kind) => kind switch
    {
        NetworkKind.Wifi => Wifi,
        NetworkKind.Ethernet => Ethernet,
        _ => NoNetwork,
    };

    /// level 0..1; muted or 0 shows the crossed speaker.
    public static string Volume(double level, bool muted)
    {
        if (muted || level <= 0) return Mute;
        return VolumeLevels[level < 0.34 ? 1 : level < 0.67 ? 2 : 3];
    }

    public static string BatteryLevel(int percent, bool charging)
    {
        var index = Math.Clamp((int)Math.Round(Math.Clamp(percent, 0, 100) / 10.0), 0, 10);
        return charging ? Charging[index] : Battery[index];
    }
}

/// Texts shown in the status area.
public static class StatusFormat
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static string Clock(DateTime now, bool use24Hour, bool showSeconds)
    {
        var pattern = (use24Hour ? "HH:mm" : "h:mm") + (showSeconds ? ":ss" : "") + (use24Hour ? "" : " tt");
        return now.ToString(pattern, use24Hour ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("en-US"));
    }

    /// "Sun, Oct 18"
    public static string ShortDate(DateTime now) => now.ToString("ddd, MMM d", Culture);

    /// "Sunday, October 18, 2026"
    public static string LongDate(DateTime now) => now.ToString("dddd, MMMM d, yyyy", Culture);

    /// Time until the clock text changes next.
    public static TimeSpan UntilNextTick(DateTime now, bool showSeconds)
    {
        var next = showSeconds
            ? new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind).AddSeconds(1)
            : new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind).AddMinutes(1);
        return next - now;
    }

    public static string BatteryTooltip(int percent, bool charging, bool pluggedIn, TimeSpan? remaining)
    {
        var text = $"Battery {percent}%";
        if (charging) return text + " · charging";
        if (pluggedIn) return percent >= 100 ? "Battery full · plugged in" : text + " · plugged in";
        if (remaining is { } r && r > TimeSpan.Zero) return text + $" · {(int)r.TotalHours} h {r.Minutes} min left";
        return text;
    }

    public static string VolumeTooltip(double level, bool muted) =>
        muted ? "Volume: muted" : $"Volume {(int)Math.Round(level * 100)}%";

    public static string NetworkTooltip(NetworkKind kind, string? name) => kind switch
    {
        NetworkKind.Wifi => "Wi-Fi connected" + (string.IsNullOrEmpty(name) ? "" : $" ({name})"),
        NetworkKind.Ethernet => "Ethernet connected" + (string.IsNullOrEmpty(name) ? "" : $" ({name})"),
        _ => "No network connection",
    };

    /// Two-letter code of a keyboard layout's language, e.g. 0x0409 → "EN", 0x0421 → "ID".
    public static string LanguageCode(int langId)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(langId & 0xFFFF).TwoLetterISOLanguageName;
            return string.IsNullOrEmpty(name) || name == "iv" ? "??" : name.ToUpperInvariant();
        }
        catch (CultureNotFoundException)
        {
            return "??";
        }
    }
}
