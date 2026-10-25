using System.Globalization;
using System.Text.Json;
using Windock.Core.Settings;

namespace Windock.Core.Island;

public sealed record WeatherHour(DateTime Time, double Temperature, int Code, bool IsDay);

/// Times are local to the city (Open-Meteo "timezone=auto").
public sealed record WeatherReport(double Temperature, int Code, bool IsDay, double High, double Low, IReadOnlyList<WeatherHour> Hours, DateTime FetchedAt);

/// The free Open-Meteo API (no key): city lookup and forecast. Parsing is tolerant: anything missing → null.
public static class OpenMeteo
{
    public const int HoursShown = 5;

    public static string GeocodeUrl(string city) =>
        $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json";

    public static string ForecastUrl(WeatherPlace place, TemperatureUnit unit) =>
        string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude:0.####}&longitude={place.Longitude:0.####}")
        + "&current=temperature_2m,weather_code,is_day&hourly=temperature_2m,weather_code,is_day"
        + "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=2"
        + (unit == TemperatureUnit.Fahrenheit ? "&temperature_unit=fahrenheit" : "");

    /// "Jakarta, Indonesia" from the first result; null when nothing matched.
    public static WeatherPlace? ParseGeocode(string query, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                return null;
            var first = results[0];
            var name = Str(first, "name");
            if (name is null || !first.TryGetProperty("latitude", out var lat) || !first.TryGetProperty("longitude", out var lon)) return null;
            var country = Str(first, "country");
            return new WeatherPlace(query, country is null ? name : $"{name}, {country}", lat.GetDouble(), lon.GetDouble());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static WeatherReport? ParseForecast(string json, DateTime fetchedAt)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var current = root.GetProperty("current");
            var now = ParseTime(current.GetProperty("time").GetString());
            var daily = root.GetProperty("daily");
            var hourly = root.GetProperty("hourly");
            var times = hourly.GetProperty("time");
            var temps = hourly.GetProperty("temperature_2m");
            var codes = hourly.GetProperty("weather_code");
            var days = hourly.GetProperty("is_day");
            var hours = new List<WeatherHour>();
            for (var i = 0; i < times.GetArrayLength() && hours.Count < HoursShown; i++)
            {
                var t = ParseTime(times[i].GetString());
                if (t <= now || temps[i].ValueKind != JsonValueKind.Number) continue;
                hours.Add(new WeatherHour(t, temps[i].GetDouble(), codes[i].GetInt32(), days[i].GetInt32() == 1));
            }
            return new WeatherReport(
                current.GetProperty("temperature_2m").GetDouble(), current.GetProperty("weather_code").GetInt32(), current.GetProperty("is_day").GetInt32() == 1,
                daily.GetProperty("temperature_2m_max")[0].GetDouble(), daily.GetProperty("temperature_2m_min")[0].GetDouble(), hours, fetchedAt);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTime ParseTime(string? text) =>
        DateTime.ParseExact(text ?? "", "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
}

/// WMO weather codes (as used by Open-Meteo) → a short text and a Segoe UI Symbol glyph.
public static class WeatherCodes
{
    public const string Sun = "☀", Moon = "☾", Cloud = "☁", PartlyCloudy = "⛅", Rain = "☂", Snow = "❄", Storm = "⚡";

    public static (string Text, string Glyph) Describe(int code, bool isDay) => code switch
    {
        0 => ("Clear", isDay ? Sun : Moon),
        1 => ("Mainly clear", isDay ? Sun : Moon),
        2 => ("Partly cloudy", isDay ? PartlyCloudy : Moon),
        3 => ("Cloudy", Cloud),
        45 or 48 => ("Fog", Cloud),
        >= 51 and <= 57 => ("Drizzle", Rain),
        >= 61 and <= 67 => (code <= 61 ? "Light rain" : code >= 65 ? "Heavy rain" : "Rain", Rain),
        >= 71 and <= 77 => ("Snow", Snow),
        >= 80 and <= 82 => ("Rain showers", Rain),
        85 or 86 => ("Snow showers", Snow),
        >= 95 => ("Thunderstorm", Storm),
        _ => ("—", Cloud),
    };

    public static string Temperature(double value) => $"{Math.Round(value).ToString(CultureInfo.InvariantCulture)}°";
}

/// One row of the island's Devices panel. Battery is a percentage when the device reports one.
public sealed record DeviceEntry(DeviceKind Kind, string? Name, int? Battery = null, string? Note = null);

/// Cleans the raw device list for the Devices panel: generic entries ("HID Keyboard Device", no name) of a kind
/// are dropped when a named device of that kind exists and otherwise shown once by kind name; duplicates merge
/// (keeping a battery level); a Bluetooth device already listed as a keyboard, mouse or headset lends it its battery.
/// A device that is both keyboard and mouse (receivers, gaming gear) is listed once, by its name or as a mouse.
/// Of the audio outputs only the current one is listed (plus Bluetooth headsets), without Windows' "1 - " prefix.
public static class DeviceList
{
    private static readonly DeviceKind[] Order = { DeviceKind.Mouse, DeviceKind.Keyboard, DeviceKind.Audio, DeviceKind.Display, DeviceKind.Bluetooth };

    public static IReadOnlyList<DeviceEntry> Build(IEnumerable<DeviceEntry> raw)
    {
        var all = raw.Select(e => e.Kind == DeviceKind.Audio && e.Name is { } n ? e with { Name = AudioName(n) } : e).ToList();
        var keyboards = Names(all, DeviceKind.Keyboard);
        var mice = Names(all, DeviceKind.Mouse);
        all.RemoveAll(e => e.Name is { } n && keyboards.Contains(n) && mice.Contains(n)
                           && e.Kind != (n.Contains("keyboard", StringComparison.OrdinalIgnoreCase) ? DeviceKind.Keyboard : DeviceKind.Mouse));
        var bluetooth = all.Where(e => e.Kind == DeviceKind.Bluetooth && !string.IsNullOrWhiteSpace(e.Name)).ToList();
        var result = new List<DeviceEntry>();
        foreach (var kind in Order)
        {
            var ofKind = all.Where(e => e.Kind == kind).ToList();
            var named = ofKind.Where(e => !string.IsNullOrWhiteSpace(e.Name)).ToList();
            if (named.Count == 0)
            {
                if (ofKind.Count > 0 && kind is not DeviceKind.Bluetooth and not DeviceKind.Audio) result.Add(new DeviceEntry(kind, kind.ToString()));
                continue;
            }
            foreach (var group in named.GroupBy(e => e.Name!.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                if (kind == DeviceKind.Bluetooth && result.Any(r => SameDevice(r.Name, group.Key))) continue;
                if (kind == DeviceKind.Audio && group.All(e => e.Note is null) && !bluetooth.Any(b => SameDevice(b.Name, group.Key))) continue;
                var battery = group.Select(e => e.Battery).FirstOrDefault(b => b is not null)
                              ?? (kind == DeviceKind.Bluetooth ? null : bluetooth.Where(b => SameDevice(b.Name, group.Key)).Select(b => b.Battery).FirstOrDefault(b => b is not null));
                result.Add(new DeviceEntry(kind, group.Key, battery, group.Select(e => e.Note).FirstOrDefault(n => n is not null)));
            }
        }
        return result;
    }

    private static readonly System.Text.RegularExpressions.Regex AudioPrefix = new(@"^\d+\s*-\s*");
    /// The driver part Windows appends: "(AMD High Definition Audio Device)", "(Realtek(R) Audio)".
    private static readonly System.Text.RegularExpressions.Regex AudioDriver =
        new(@"\s*\((?:[^()]|\([^()]*\))*audio(?:[^()]|\([^()]*\))*\)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string AudioName(string name)
    {
        var bare = AudioPrefix.Replace(name, "");
        var trimmed = AudioDriver.Replace(bare, "");
        return trimmed.Length > 0 ? trimmed : bare;
    }

    private static HashSet<string> Names(IEnumerable<DeviceEntry> all, DeviceKind kind) =>
        all.Where(e => e.Kind == kind && !string.IsNullOrWhiteSpace(e.Name)).Select(e => e.Name!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// "WH-1000XM4" and "Headphones (WH-1000XM4)" are the same device.
    private static bool SameDevice(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && (a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase));
}
