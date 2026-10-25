using System.Windows;
using System.Windows.Controls;
using Windock.Core.Island;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class StatusPage : UserControl
{
    private readonly PageSync _sync;

    public StatusPage(SettingsContext context)
    {
        InitializeComponent();
        _sync = new PageSync(this, context.Store, Refresh);

        PageSync.OnToggle(Enabled, on => Edit(s => s with { Enabled = on }));
        PageSync.OnToggle(Tray, on => Edit(s => s with { ShowTray = on }));
        PageSync.OnToggle(Wifi, on => Edit(s => s with { ShowWifi = on }));
        PageSync.OnToggle(Volume, on => Edit(s => s with { ShowVolume = on }));
        PageSync.OnToggle(Battery, on => Edit(s => s with { ShowBattery = on }));
        PageSync.OnToggle(LanguageBox, on => Edit(s => s with { ShowLanguage = on }));
        PageSync.OnToggle(Clock, on => Edit(s => s with { ShowClock = on }));
        PageSync.OnToggle(Notifications, on => Edit(s => s with { ShowNotifications = on }));
        PageSync.OnToggle(ShowDate, on => Edit(s => s with { ShowDate = on }));
        PageSync.OnToggle(ShowSeconds, on => Edit(s => s with { ShowSeconds = on }));
        PageSync.OnToggle(BatteryPercent, on => Edit(s => s with { ShowBatteryPercent = on }));
        Clock24.Checked += (_, _) => Edit(s => s with { Clock24Hour = true });
        Clock12.Checked += (_, _) => Edit(s => s with { Clock24Hour = false });
    }

    private void Edit(Func<StatusSettings, StatusSettings> change) => _sync.Edit(s => s with { Status = change(s.Status) });

    private void Refresh(DockSettings settings)
    {
        var s = settings.Status;
        Enabled.IsChecked = s.Enabled;
        Options.IsEnabled = s.Enabled;
        Tray.IsChecked = s.ShowTray;
        Wifi.IsChecked = s.ShowWifi;
        Volume.IsChecked = s.ShowVolume;
        Battery.IsChecked = s.ShowBattery;
        LanguageBox.IsChecked = s.ShowLanguage;
        Clock.IsChecked = s.ShowClock;
        Notifications.IsChecked = s.ShowNotifications;
        Clock24.IsChecked = s.Clock24Hour;
        Clock12.IsChecked = !s.Clock24Hour;
        ShowDate.IsChecked = s.ShowDate;
        ShowSeconds.IsChecked = s.ShowSeconds;
        BatteryPercent.IsChecked = s.ShowBatteryPercent;
        // Items the Dynamic Island shows are not in the dock, so their dock toggles go away.
        var islandClock = IslandOwnership.IslandShowsClock(settings);
        var islandDate = IslandOwnership.IslandShowsDate(settings);
        var islandBattery = IslandOwnership.IslandShowsBattery(settings);
        Clock.Visibility = islandClock ? Visibility.Collapsed : Visibility.Visible;
        ShowDate.Visibility = islandDate ? Visibility.Collapsed : Visibility.Visible;
        Battery.Visibility = islandBattery ? Visibility.Collapsed : Visibility.Visible;
        var moved = new[] { (islandClock, "clock"), (islandDate, "date"), (islandBattery, "battery") }.Where(m => m.Item1).Select(m => m.Item2).ToList();
        IslandHint.Visibility = moved.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        IslandHint.Text = moved.Count > 0
            ? $"The {Join(moved)} {(moved.Count == 1 ? "is" : "are")} shown in the Dynamic Island. Change that on the Dynamic Island page."
            : "";
        var clockShown = islandClock || s.ShowClock;
        Clock24.IsEnabled = Clock12.IsEnabled = ShowSeconds.IsEnabled = clockShown;
        ShowDate.IsEnabled = s.ShowClock;
        BatteryPercent.IsEnabled = islandBattery || s.ShowBattery;
    }

    private static string Join(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    private void OnReset(object sender, RoutedEventArgs e) => _sync.Edit(s => s with { Status = StatusSettings.Default });
}
