using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Windock.Composition;
using Windock.Core.Apps;
using Windock.Core.Island;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Core.Status;
using Windock.Interop;

namespace Windock.Island;

/// Owns the Dynamic Island: feeds media, volume, brightness, battery and device signals into IslandModel,
/// draws the model with IslandWindow, and slides the window out of view per the visibility setting.
/// The window and every signal source exist only while their setting is on.
internal sealed class IslandController : IDisposable
{
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(600);
    /// The pointer must rest on the pill this long before it opens: passing over it, or the pill sliding in
    /// under a pointer parked at the top edge, does not open it.
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(180);
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    private readonly SettingsStore _settings;
    private readonly Status.StatusArea _status;
    private readonly WindowTracker _tracker;
    private readonly MessageWindow _messages;
    private readonly AppLauncher _launcher;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly IslandModel _model;
    private readonly DispatcherDelayScheduler _scheduler;
    private readonly DispatcherTimer _clockTimer, _progressTimer, _hideTimer;
    private readonly DeviceBurst _burst = new();
    private IslandWindow? _window;
    private MediaSource? _media;
    private BrightnessWatcher? _brightness;
    private DeviceWatcher? _devices;
    private IDisposable? _burstTimer, _hoverTimer;
    private readonly WeatherService _weather;
    private IReadOnlyList<DeviceEntry>? _deviceList;
    private bool _devicesReading, _devicesDirty = true;
    /// City text the geocoder did not find (so it is not asked again until the setting changes).
    private string? _cityNotFound;
    private bool _cityLookup, _wasExpanded;
    private PowerReading? _power;
    private (double Level, bool Muted)? _volume;
    private bool _suppressed, _edgeHover, _pillHover, _evaluationQueued;

    /// suppressed: the starting value of SetSuppressed, so Windows mode never flashes the island at startup.
    public IslandController(SettingsStore settings, Status.StatusArea status, WindowTracker tracker, MessageWindow messages,
        AppLauncher launcher, ILog log, Dispatcher ui, bool suppressed)
    {
        _suppressed = suppressed;
        _settings = settings;
        _status = status;
        _tracker = tracker;
        _messages = messages;
        _launcher = launcher;
        _log = log;
        _ui = ui;
        _scheduler = new DispatcherDelayScheduler(ui);
        _model = new IslandModel(_scheduler, () => TimeSpan.FromSeconds(_settings.Current.Island.EventSeconds));
        _weather = new WeatherService(log);
        _weather.Changed += Render;
        _model.Changed += OnModelChanged;
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background, ui);
        _clockTimer.Tick += (_, _) => OnClockTick();
        _progressTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(1) };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _hideTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = HideDelay };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!WantsShown()) _window?.SetShown(false);
        };

        _status.Audio.Changed += OnVolumeChanged;
        if (_status.Audio.Available) _volume = (_status.Audio.Level, _status.Audio.Muted);
        _power = ReadPower();
        SystemEvents.PowerModeChanged += OnPowerChanged;
        _tracker.ForegroundChanged += OnForegroundChanged;
        _tracker.RawEvent += OnRawWindowEvent;
        Apply();
    }

    /// Settings changed (any island option, the clock format, the dock color).
    public void Apply()
    {
        var s = _settings.Current.Island;
        if (!s.Enabled)
        {
            Teardown();
            return;
        }
        if (_window is null)
        {
            _window = new IslandWindow();
            _window.PointerEntered += OnPillEntered;
            _window.PointerLeft += OnPillLeft;
            _window.EdgeEntered += () => { _edgeHover = true; Evaluate(); };
            _window.EdgeLeft += () => { _edgeHover = false; QueueEvaluate(); };
            _window.CoverClicked += ShowPlayer;
            _window.PlayPauseClicked += () => _media?.TogglePlayPause();
            _window.NextClicked += () => _media?.Next();
            _window.PreviousClicked += () => _media?.Previous();
            _window.PageTurnRequested += delta => _model.TurnPage(delta, Panels().Count);
            if (!_suppressed) _window.Show();
            _log.Info("dynamic island on");
        }
        var scale = s.Size switch { IslandSize.Small => 0.85, IslandSize.Large => 1.2, _ => 1.0 };
        var color = s.UseDockColor && ThemeColor.TryParse(_settings.Current.Theme.PlateColor, out var c)
            ? System.Windows.Media.Color.FromRgb(c.R, c.G, c.B)
            : System.Windows.Media.Colors.Black;
        _window.SetAppearance(scale, color);
        if (!s.ExpandOnHover) _model.Collapse();

        SyncSources(s);
        ScheduleClock();
        Render();
        Evaluate();
    }

    private void OnPillEntered()
    {
        _pillHover = true;
        _hoverTimer?.Dispose();
        _hoverTimer = _scheduler.Schedule(HoverDelay, () =>
        {
            _hoverTimer = null;
            if (_pillHover) _model.PointerEntered(_settings.Current.Island.ExpandOnHover);
        });
        QueueEvaluate();
    }

    private void OnPillLeft()
    {
        _pillHover = false;
        _hoverTimer?.Dispose();
        _hoverTimer = null;
        _model.PointerLeft();
        QueueEvaluate();
    }

    /// A fullscreen app is in front, or Windows mode: the island stays out of sight.
    public void SetSuppressed(bool suppressed)
    {
        if (suppressed == _suppressed) return;
        _suppressed = suppressed;
        if (_window is null) return;
        if (suppressed)
        {
            _model.Collapse();
            _window.Hide();
        }
        else
        {
            _window.Show();
            Evaluate();
        }
    }

    /// Display or DPI change: back to the top centre of the primary screen.
    public void Relayout() => _window?.Place();

    private void SyncSources(IslandSettings s)
    {
        if (s.Media && _media is null)
        {
            _media = new MediaSource(_ui, _log);
            _media.Changed += OnMediaChanged;
        }
        else if (!s.Media && _media is not null)
        {
            _media.Changed -= OnMediaChanged;
            _media.Dispose();
            _media = null;
            _model.SetMedia(null);
        }
        if (s.BrightnessEvents && _brightness is null)
        {
            _brightness = new BrightnessWatcher(_messages, _log);
            _brightness.Changed += OnBrightnessChanged;
        }
        else if (!s.BrightnessEvents && _brightness is not null)
        {
            _brightness.Dispose();
            _brightness = null;
        }
        if (s.DeviceEvents && _devices is null)
        {
            _devices = new DeviceWatcher(_messages, _log);
            _devices.Changed += OnDeviceChanged;
        }
        else if (!s.DeviceEvents && _devices is not null)
        {
            _devices.Dispose();
            _devices = null;
        }
        _model.ClearEvents(e => !EventAllowed(e.Kind, s));
    }

    private static bool EventAllowed(IslandEventKind kind, IslandSettings s) => kind switch
    {
        IslandEventKind.Volume => s.VolumeEvents,
        IslandEventKind.Brightness => s.BrightnessEvents,
        IslandEventKind.Battery => s.BatteryEvents,
        _ => s.DeviceEvents,
    };

    private void Teardown()
    {
        _clockTimer.Stop();
        _progressTimer.Stop();
        _hideTimer.Stop();
        _model.ClearEvents();
        _model.Collapse();
        _media?.Dispose();
        _media = null;
        _model.SetMedia(null);
        _brightness?.Dispose();
        _brightness = null;
        _devices?.Dispose();
        _devices = null;
        if (_window is null) return;
        _window.Close();
        _window = null;
        _log.Info("dynamic island off");
        MemoryTrim.After(TimeSpan.FromSeconds(2));
    }

    // ---- signals ----

    private void Show(IslandEvent e)
    {
        if (_window is null || !EventAllowed(e.Kind, _settings.Current.Island)) return;
        _model.Show(e);
    }

    private void OnVolumeChanged()
    {
        if (!_status.Audio.Available) return;
        var now = (_status.Audio.Level, _status.Audio.Muted);
        var before = _volume;
        _volume = now;
        if (before is null || (Math.Abs(before.Value.Level - now.Level) < 0.005 && before.Value.Muted == now.Muted)) return;
        Show(new IslandEvent(IslandEventKind.Volume, StatusGlyphs.Volume(now.Level, now.Muted), "Volume",
            now.Muted ? 0 : now.Level, now.Muted ? "Muted" : $"{Math.Round(now.Level * 100)}"));
    }

    private void OnBrightnessChanged(int percent) =>
        Show(new IslandEvent(IslandEventKind.Brightness, IslandGlyphs.Brightness, "Brightness", percent / 100.0, $"{percent}"));

    private void OnDeviceChanged(DeviceKind kind, bool arrived, string? name)
    {
        _log.Info($"device {kind} {(arrived ? "arrived" : "removed")} '{name}'");
        _devicesDirty = true;
        _burst.Add(kind, arrived, name);
        _burstTimer ??= _scheduler.Schedule(DeviceBurst.BurstWindow, () =>
        {
            _burstTimer = null;
            if (_burst.Resolve() is { } e) Show(e);
        });
    }

    private void OnPowerChanged(object? sender, PowerModeChangedEventArgs e) => _ui.BeginInvoke(() =>
    {
        CheckPower();
        Render();
    });

    private void CheckPower()
    {
        var now = ReadPower();
        var before = _power;
        _power = now;
        if (BatteryEvents.Diff(before, now) is { } e) Show(e);
    }

    private static PowerReading? ReadPower() =>
        SystemStatus.ReadBattery() is { } b ? new PowerReading(b.Percent, b.Charging, b.PluggedIn) : null;

    private void OnMediaChanged()
    {
        _model.SetMedia(_media?.State);
        Render();
    }

    /// The player's window to the front (cover or title clicked).
    private void ShowPlayer()
    {
        if (_media?.AppId is not { Length: > 0 } id) return;
        var exe = id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;
        var aumid = AppId.FromAumid(id);
        var window = _tracker.Snapshot().FirstOrDefault(w => w.AppId == aumid
            || (w.ExePath is { } path && string.Equals(Path.GetFileNameWithoutExtension(path), exe, StringComparison.OrdinalIgnoreCase)));
        _log.Info($"island: show player {id} -> 0x{window?.Handle ?? 0:X}");
        if (window is not null) _launcher.Activate(window.Handle);
    }

    // ---- drawing ----

    private void OnModelChanged()
    {
        if (_model.Expanded && !_wasExpanded) LoadPanels();
        _wasExpanded = _model.Expanded;
        Render();
        QueueEvaluate();
    }

    private IReadOnlyList<IslandPanel> Panels()
    {
        var s = _settings.Current.Island;
        var list = new List<IslandPanel> { IslandPanel.Home };
        if (s.DevicesPanel) list.Add(IslandPanel.Devices);
        if (s.WeatherPanel) list.Add(IslandPanel.Weather);
        return list;
    }

    /// The card opened: fetch what its other pages show, so sliding to them finds the data ready.
    private void LoadPanels()
    {
        var s = _settings.Current.Island;
        if (s.DevicesPanel) ReadDevices();
        if (s.WeatherPanel) RefreshWeather(s);
    }

    private async void ReadDevices()
    {
        if (_devicesReading || (!_devicesDirty && _deviceList is not null)) return;
        _devicesReading = true;
        try
        {
            _deviceList = await DeviceInventory.ReadAsync(_log);
            // Without the device watcher (device pop-ups off) arrivals go unseen: read again each time the card opens.
            _devicesDirty = _devices is null;
        }
        finally
        {
            _devicesReading = false;
        }
        Render();
    }

    private async void RefreshWeather(IslandSettings s)
    {
        if (s.City.Length == 0) return;
        if (s.Place is { } place && string.Equals(place.Query, s.City, StringComparison.OrdinalIgnoreCase))
        {
            _weather.RefreshIfStale(place, s.Unit);
            return;
        }
        if (_cityLookup || string.Equals(_cityNotFound, s.City, StringComparison.OrdinalIgnoreCase)) return;
        _cityLookup = true;
        Render();
        var found = await _weather.FindAsync(s.City);
        _cityLookup = false;
        if (found is null)
        {
            _cityNotFound = s.City;
            Render();
            return;
        }
        // Saving the place triggers Apply, which renders; the forecast follows on the next open or right here.
        _settings.Update(x => x with { Island = x.Island with { Place = found } });
        _weather.RefreshIfStale(found, _settings.Current.Island.Unit);
    }

    private WeatherView WeatherState(IslandSettings s)
    {
        if (s.City.Length == 0) return new WeatherView(null, null, "Type your city in Settings › Dynamic Island to see the weather here.", false);
        var placeOk = s.Place is { } p && string.Equals(p.Query, s.City, StringComparison.OrdinalIgnoreCase);
        if (!placeOk)
        {
            if (string.Equals(_cityNotFound, s.City, StringComparison.OrdinalIgnoreCase))
                return new WeatherView(null, null, $"Couldn't find “{s.City}”. Check the city name in Settings, or your internet connection.", false);
            return new WeatherView(null, null, null, false);
        }
        if (_weather.Report is { } r) return new WeatherView(r, s.Place!.Name, null, _weather.Failed);
        return new WeatherView(null, null, _weather.Failed ? "Weather is unavailable right now. Check your internet connection." : null, false);
    }

    private void OnClockTick()
    {
        ScheduleClock();
        CheckPower();  // remaining time drifts and low-battery marks are crossed without a power event
        Render();
    }

    private void ScheduleClock()
    {
        _clockTimer.Stop();
        if (_window is null) return;
        var seconds = IslandOwnership.IslandShowsClock(_settings.Current) && _settings.Current.Status.ShowSeconds;
        _clockTimer.Interval = StatusFormat.UntilNextTick(DateTime.Now, seconds) + TimeSpan.FromMilliseconds(seconds ? 20 : 200);
        _clockTimer.Start();
    }

    private void Render()
    {
        if (_window is null) return;
        var settings = _settings.Current;
        var status = settings.Status;
        var now = DateTime.Now;
        var battery = SystemStatus.ReadBattery();
        var batteryGlyph = battery is { } b ? StatusGlyphs.BatteryLevel(b.Percent, b.Charging) : null;
        var showBattery = IslandOwnership.IslandShowsBattery(settings) && battery is not null;
        var view = new IslandView(
            _model.Mode, _model.Expanded,
            IslandOwnership.IslandShowsClock(settings) ? StatusFormat.Clock(now, status.Clock24Hour, status.ShowSeconds) : null,
            IslandOwnership.IslandShowsDate(settings) ? StatusFormat.ShortDate(now) : null,
            showBattery ? batteryGlyph : null,
            showBattery && status.ShowBatteryPercent ? $"{battery!.Percent}%" : null,
            StatusFormat.Clock(now, status.Clock24Hour, false),
            StatusFormat.LongDate(now),
            battery is { } bt ? StatusFormat.BatteryTooltip(bt.Percent, bt.Charging, bt.PluggedIn, bt.Remaining) : null,
            _model.MediaActive ? _model.Media : null, _model.MediaActive ? _media?.Cover : null,
            _model.CurrentEvent,
            Panels(), _model.Page, _deviceList, WeatherState(settings.Island));
        _window.Render(view);

        var progress = view is { Mode: IslandMode.Media, Expanded: true } && _media is not null;
        if (progress) UpdateProgress();
        if (progress && view.Media?.Playing == true) _progressTimer.Start();
        else _progressTimer.Stop();
    }

    private void UpdateProgress()
    {
        if (_window is null || _media is null) return;
        _window.UpdateProgress(_media.CurrentPosition(), _media.Duration);
    }

    // ---- visibility ----

    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        _window?.RaiseToTop();  // Windows can leave a topmost window under others; keep the island above
        QueueEvaluate();
    }

    private void OnRawWindowEvent(uint evt, IntPtr hwnd)
    {
        if (evt is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_SYSTEM_MINIMIZESTART or Native.EVENT_SYSTEM_MINIMIZEEND
            && hwnd == Native.GetForegroundWindow())
            QueueEvaluate();
    }

    private void QueueEvaluate()
    {
        if (_evaluationQueued) return;
        _evaluationQueued = true;
        _ui.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _evaluationQueued = false;
            Evaluate();
        });
    }

    private void Evaluate()
    {
        if (_window is null || _suppressed) return;
        if (WantsShown())
        {
            _hideTimer.Stop();
            _window.SetShown(true);
        }
        else if (_window.IsShown && !_hideTimer.IsEnabled)
        {
            _hideTimer.Start();
        }
    }

    private bool WantsShown()
    {
        var s = _settings.Current.Island;
        var attention = _edgeHover || _pillHover || _model.Expanded || _model.Mode == IslandMode.Event;
        var covered = s.Visibility == IslandVisibility.AutoHide && ForegroundCoversIsland();
        return IslandVisibilityPolicy.ShouldShow(s.Enabled, _suppressed, s.Visibility, covered, AutoHideController.DesktopInFront(), attention);
    }

    private bool ForegroundCoversIsland()
    {
        if (_window is null) return false;
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || Native.IsIconic(hwnd) || ShellClasses.Contains(Native.ClassName(hwnd))) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId || !Native.GetWindowRect(hwnd, out var r)) return false;
        return AutoHidePolicy.Covers((r.Left, r.Top, r.Right, r.Bottom), _window.CompactAreaPx);
    }

    public void Dispose()
    {
        Teardown();
        _status.Audio.Changed -= OnVolumeChanged;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        _tracker.ForegroundChanged -= OnForegroundChanged;
        _tracker.RawEvent -= OnRawWindowEvent;
    }
}
