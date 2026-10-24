using System.Net.NetworkInformation;
using System.Windows.Threading;
using Microsoft.Win32;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Core.Status;
using Windock.Interop;

namespace Windock.Status;

/// One button in the status area. Glyph uses Segoe MDL2 Assets; Text/SubText are plain text lines.
internal sealed record StatusButton(string Key, string? Glyph, string? Text, string? SubText, string Tooltip);

/// The fixed segment at the right end of the dock: tray, network, volume, battery, language, clock, notifications.
/// Updates come from events (audio callbacks, network/power/shell notifications); only the clock ticks,
/// once per minute (or per second when seconds are shown).
internal sealed class StatusArea : IDisposable
{
    public const string Tray = "status:tray", Network = "status:wifi", Volume = "status:volume", Battery = "status:battery",
        Language = "status:lang", Clock = "status:clock", Notifications = "status:notif";

    private readonly SettingsStore _settings;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _clockTimer;
    private SystemStatus.Battery? _battery;
    private (NetworkKind Kind, string? Name) _network;
    private int _languageId;

    public StatusArea(SettingsStore settings, Dispatcher ui, ILog log)
    {
        _settings = settings;
        _ui = ui;
        Audio = new AudioEndpoint(ui, log);
        Audio.Changed += Rebuild;
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background, ui);
        _clockTimer.Tick += (_, _) =>
        {
            _battery = SystemStatus.ReadBattery();  // remaining time drifts; refresh with the clock
            ScheduleClock();
            Rebuild();
        };
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        _battery = SystemStatus.ReadBattery();
        _network = SystemStatus.ReadNetwork();
        _languageId = SystemStatus.ForegroundLanguageId();
        ScheduleClock();
        Rebuild();
    }

    public AudioEndpoint Audio { get; }

    /// What to show, in order; empty when the area is off.
    public IReadOnlyList<StatusButton> Buttons { get; private set; } = Array.Empty<StatusButton>();

    public event Action? Changed;

    public void ApplySettings()
    {
        ScheduleClock();
        Rebuild();
    }

    /// Foreground window or its keyboard layout changed.
    public void RefreshLanguage()
    {
        var id = SystemStatus.ForegroundLanguageId();
        if (id == _languageId || id == 0) return;
        _languageId = id;
        Rebuild();
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _ui.BeginInvoke(() =>
    {
        _network = SystemStatus.ReadNetwork();
        Rebuild();
    });

    private void OnPowerChanged(object? sender, PowerModeChangedEventArgs e) => _ui.BeginInvoke(() =>
    {
        _battery = SystemStatus.ReadBattery();
        if (e.Mode == PowerModes.Resume) _network = SystemStatus.ReadNetwork();
        ScheduleClock();
        Rebuild();
    });

    private void ScheduleClock()
    {
        var s = _settings.Current.Status;
        _clockTimer.Stop();
        if (!s.Enabled) return;
        var seconds = s.ShowClock && s.ShowSeconds;
        _clockTimer.Interval = StatusFormat.UntilNextTick(DateTime.Now, seconds) + TimeSpan.FromMilliseconds(seconds ? 20 : 200);
        _clockTimer.Start();
    }

    private void Rebuild()
    {
        var s = _settings.Current.Status;
        var list = new List<StatusButton>();
        if (s.Enabled)
        {
            var now = DateTime.Now;
            if (s.ShowTray) list.Add(new StatusButton(Tray, StatusGlyphs.ChevronUp, null, null, "Tray icons"));
            if (s.ShowWifi)
                list.Add(new StatusButton(Network, StatusGlyphs.Network(_network.Kind), null, null, StatusFormat.NetworkTooltip(_network.Kind, _network.Name)));
            if (s.ShowVolume && Audio.Available)
                list.Add(new StatusButton(Volume, StatusGlyphs.Volume(Audio.Level, Audio.Muted), null, null, StatusFormat.VolumeTooltip(Audio.Level, Audio.Muted)));
            if (s.ShowBattery && _battery is { } b)
                list.Add(new StatusButton(Battery, StatusGlyphs.BatteryLevel(b.Percent, b.Charging), s.ShowBatteryPercent ? $"{b.Percent}%" : null, null,
                    StatusFormat.BatteryTooltip(b.Percent, b.Charging, b.PluggedIn, b.Remaining)));
            if (s.ShowLanguage)
                list.Add(new StatusButton(Language, null, StatusFormat.LanguageCode(_languageId), null, "Input language (click to switch, Win+Space)"));
            if (s.ShowClock)
                list.Add(new StatusButton(Clock, null, StatusFormat.Clock(now, s.Clock24Hour, s.ShowSeconds), s.ShowDate ? StatusFormat.ShortDate(now) : null,
                    StatusFormat.LongDate(now)));
            if (s.ShowNotifications) list.Add(new StatusButton(Notifications, StatusGlyphs.Notifications, null, null, "Notification center"));
        }
        if (list.SequenceEqual(Buttons)) return;
        Buttons = list;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _clockTimer.Stop();
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        Audio.Dispose();
    }
}
