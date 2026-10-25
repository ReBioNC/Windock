using System.Net.Http;
using Windows.Devices.Bluetooth;
using Windows.Devices.Display;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using Windock.Core.Island;
using Windock.Core.Logging;
using Windock.Core.Settings;

namespace Windock.Island;

/// Weather for the island's Weather panel from Open-Meteo. Fetched only when the panel is shown and the last
/// report is older than RefreshAfter; the last good report stays on screen while offline.
internal sealed class WeatherService(ILog log)
{
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    private HttpClient? _http;
    private bool _busy;
    private DateTime _lastAttempt = DateTime.MinValue;
    private (WeatherPlace Place, TemperatureUnit Unit)? _reportFor;

    public WeatherReport? Report { get; private set; }

    /// The last fetch failed (offline, service down); Report may still hold an older result.
    public bool Failed { get; private set; }

    public bool Busy => _busy;

    /// Raised on the UI thread when Report, Failed or Busy changed.
    public event Action? Changed;

    /// Looks the city up; null when not found or offline.
    public async Task<WeatherPlace?> FindAsync(string city)
    {
        try
        {
            var json = await Http().GetStringAsync(OpenMeteo.GeocodeUrl(city));
            var place = OpenMeteo.ParseGeocode(city, json);
            log.Info($"weather: '{city}' -> {place?.Name ?? "not found"}");
            return place;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.Error($"weather: city lookup failed for '{city}'", ex);
            return null;
        }
    }

    public async void RefreshIfStale(WeatherPlace place, TemperatureUnit unit)
    {
        var now = DateTime.Now;
        var sameTarget = _reportFor == (place, unit);
        if (_busy) return;
        if (sameTarget && Report is { } r && now - r.FetchedAt < RefreshAfter) return;
        if (sameTarget && Failed && now - _lastAttempt < RetryAfter) return;
        if (!sameTarget)
        {
            Report = null;
            Failed = false;
        }
        _busy = true;
        _lastAttempt = now;
        Changed?.Invoke();
        try
        {
            var json = await Http().GetStringAsync(OpenMeteo.ForecastUrl(place, unit));
            var report = OpenMeteo.ParseForecast(json, DateTime.Now);
            Failed = report is null;
            if (report is not null)
            {
                Report = report;
                _reportFor = (place, unit);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.Error("weather: forecast failed", ex);
            Failed = true;
            _reportFor = (place, unit);
        }
        finally
        {
            _busy = false;
        }
        Changed?.Invoke();
    }

    private HttpClient Http() => _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
}

/// What is plugged in or paired right now, for the island's Devices panel. Read on demand (the panel opens,
/// a device arrives or leaves); nothing is watched in the background.
internal static class DeviceInventory
{
    private const string KeyboardClass = "{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";
    private const string MouseClass = "{378de44c-56ef-11d1-bc8c-00a0c91405dd}";
    // DEVPKEY_Bluetooth_Battery (DEVPKEY_Device_BatteryLevel on some drivers): percent, when the device reports it.
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public static async Task<IReadOnlyList<DeviceEntry>> ReadAsync(ILog log)
    {
        var raw = new List<DeviceEntry>();
        await Try(log, "keyboards", async () => raw.AddRange(await HidAsync(KeyboardClass, DeviceKind.Keyboard)));
        await Try(log, "mice", async () => raw.AddRange(await HidAsync(MouseClass, DeviceKind.Mouse)));
        await Try(log, "displays", async () => raw.AddRange(await DisplaysAsync()));
        await Try(log, "audio", async () => raw.AddRange(await AudioAsync()));
        await Try(log, "bluetooth", async () => raw.AddRange(await BluetoothAsync()));
        return DeviceList.Build(raw);
    }

    private static async Task<IEnumerable<DeviceEntry>> HidAsync(string interfaceClass, DeviceKind kind)
    {
        var aqs = $"System.Devices.InterfaceClassGuid:=\"{interfaceClass}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
        var found = await DeviceInformation.FindAllAsync(aqs);
        return found.Select(d => new DeviceEntry(kind, DeviceNames.Find(d.Id))).ToList();
    }

    private static async Task<IEnumerable<DeviceEntry>> DisplaysAsync()
    {
        var list = new List<DeviceEntry>();
        foreach (var d in await DeviceInformation.FindAllAsync(DisplayMonitor.GetDeviceSelector()))
        {
            var monitor = await DisplayMonitor.FromInterfaceIdAsync(d.Id);
            var name = monitor?.ConnectionKind == DisplayMonitorConnectionKind.Internal ? "Built-in display" : monitor?.DisplayName;
            list.Add(new DeviceEntry(DeviceKind.Display, string.IsNullOrWhiteSpace(name) ? null : name.Trim()));
        }
        return list;
    }

    private static async Task<IEnumerable<DeviceEntry>> AudioAsync()
    {
        var defaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
        var found = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        return found.Select(d => new DeviceEntry(DeviceKind.Audio, d.Name, Note: string.Equals(d.Id, defaultId, StringComparison.OrdinalIgnoreCase) ? "Output" : null)).ToList();
    }

    private static async Task<IEnumerable<DeviceEntry>> BluetoothAsync()
    {
        var list = new List<DeviceEntry>();
        var properties = new[] { BatteryKey };
        foreach (var selector in new[]
                 {
                     BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                     BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                 })
        {
            foreach (var d in await DeviceInformation.FindAllAsync(selector, properties))
                list.Add(new DeviceEntry(DeviceKind.Bluetooth, d.Name, Battery(d)));
        }
        // Classic Bluetooth headsets report their battery on the device node, not on the Bluetooth entry.
        if (list.All(b => b.Battery is not null)) return list;
        foreach (var d in await DeviceInformation.FindAllAsync($"System.Devices.Present:=System.StructuredQueryType.Boolean#True", properties, DeviceInformationKind.Device))
            if (Battery(d) is { } level && list.FirstOrDefault(b => b.Battery is null && Same(b.Name, d.Name)) is { } match)
                list[list.IndexOf(match)] = match with { Battery = level };
        return list;
    }

    private static int? Battery(DeviceInformation d) =>
        d.Properties.TryGetValue(BatteryKey, out var value) && value is byte or int or uint ? Convert.ToInt32(value) : null;

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && (a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase));

    private static async Task Try(ILog log, string what, Func<Task> read)
    {
        try
        {
            await read();
        }
        catch (Exception ex)
        {
            log.Error($"devices: reading {what} failed", ex);
        }
    }
}
