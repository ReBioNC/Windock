using System.Windows;
using System.Windows.Controls;
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
        Clock24.IsEnabled = Clock12.IsEnabled = ShowDate.IsEnabled = ShowSeconds.IsEnabled = s.ShowClock;
        BatteryPercent.IsEnabled = s.ShowBattery;
    }

    private void OnReset(object sender, RoutedEventArgs e) => _sync.Edit(s => s with { Status = StatusSettings.Default });
}
