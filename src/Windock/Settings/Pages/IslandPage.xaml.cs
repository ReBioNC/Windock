using System.Windows;
using System.Windows.Controls;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class IslandPage : UserControl
{
    private readonly PageSync _sync;

    public IslandPage(SettingsContext context)
    {
        InitializeComponent();
        _sync = new PageSync(this, context.Store, Refresh);

        PageSync.OnToggle(Enabled, on => Edit(s => s with { Enabled = on }));
        PageSync.OnToggle(Clock, on => Edit(s => s with { ShowClock = on }));
        PageSync.OnToggle(Date, on => Edit(s => s with { ShowDate = on }));
        PageSync.OnToggle(Battery, on => Edit(s => s with { ShowBattery = on }));
        PageSync.OnToggle(Media, on => Edit(s => s with { Media = on }));
        PageSync.OnToggle(Volume, on => Edit(s => s with { VolumeEvents = on }));
        PageSync.OnToggle(Brightness, on => Edit(s => s with { BrightnessEvents = on }));
        PageSync.OnToggle(PowerEvents, on => Edit(s => s with { BatteryEvents = on }));
        PageSync.OnToggle(Devices, on => Edit(s => s with { DeviceEvents = on }));
        PageSync.OnToggle(ExpandOnHover, on => Edit(s => s with { ExpandOnHover = on }));
        AutoHide.Checked += (_, _) => Edit(s => s with { Visibility = IslandVisibility.AutoHide });
        Always.Checked += (_, _) => Edit(s => s with { Visibility = IslandVisibility.Always });
        Small.Checked += (_, _) => Edit(s => s with { Size = IslandSize.Small });
        Medium.Checked += (_, _) => Edit(s => s with { Size = IslandSize.Medium });
        Large.Checked += (_, _) => Edit(s => s with { Size = IslandSize.Large });
        Black.Checked += (_, _) => Edit(s => s with { UseDockColor = false });
        DockColor.Checked += (_, _) => Edit(s => s with { UseDockColor = true });
        EventSeconds.ValueChanged += (_, e) => Edit(s => s with { EventSeconds = (int)Math.Round(e.NewValue) });
    }

    private void Edit(Func<IslandSettings, IslandSettings> change) => _sync.Edit(s => s with { Island = change(s.Island) });

    private void Refresh(DockSettings settings)
    {
        var s = settings.Island;
        Enabled.IsChecked = s.Enabled;
        Options.IsEnabled = s.Enabled;
        AutoHide.IsChecked = s.Visibility == IslandVisibility.AutoHide;
        Always.IsChecked = s.Visibility == IslandVisibility.Always;
        Clock.IsChecked = s.ShowClock;
        Date.IsChecked = s.ShowDate;
        Battery.IsChecked = s.ShowBattery;
        Media.IsChecked = s.Media;
        Volume.IsChecked = s.VolumeEvents;
        Brightness.IsChecked = s.BrightnessEvents;
        PowerEvents.IsChecked = s.BatteryEvents;
        Devices.IsChecked = s.DeviceEvents;
        ExpandOnHover.IsChecked = s.ExpandOnHover;
        EventSeconds.Value = s.EventSeconds;
        Small.IsChecked = s.Size == IslandSize.Small;
        Medium.IsChecked = s.Size == IslandSize.Medium;
        Large.IsChecked = s.Size == IslandSize.Large;
        Black.IsChecked = !s.UseDockColor;
        DockColor.IsChecked = s.UseDockColor;
    }

    private void OnReset(object sender, RoutedEventArgs e) => _sync.Edit(s => s with { Island = IslandSettings.Default });
}
