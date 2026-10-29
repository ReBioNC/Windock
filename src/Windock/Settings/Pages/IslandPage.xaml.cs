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
        PageSync.OnToggle(Mascot, on => Edit(s => s with { Mascot = on }));
        PageSync.OnToggle(Lyrics, on => Edit(s => s with { Lyrics = on }));
        PageSync.OnToggle(FocusPanel, on => Edit(s => s with { FocusPanel = on }));
        PageSync.OnToggle(TasksPanel, on => Edit(s => s with { TasksPanel = on }));
        PageSync.OnToggle(NotesPanel, on => Edit(s => s with { NotesPanel = on }));
        PageSync.OnToggle(ClipboardPanel, on => Edit(s => s with { ClipboardPanel = on }));
        PageSync.OnToggle(ClipboardImages, on => Edit(s => s with { ClipboardImages = on }));
        PageSync.OnToggle(ShelfPanel, on => Edit(s => s with { ShelfPanel = on }));
        PageSync.OnToggle(FocusSound, on => Edit(s => s with { FocusSound = on }));
        FocusMinutes.ValueChanged += (_, e) => Edit(s => s with { FocusMinutes = (int)Math.Round(e.NewValue) });
        ShortBreak.ValueChanged += (_, e) => Edit(s => s with { ShortBreakMinutes = (int)Math.Round(e.NewValue) });
        LongBreak.ValueChanged += (_, e) => Edit(s => s with { LongBreakMinutes = (int)Math.Round(e.NewValue) });
        FocusSessions.ValueChanged += (_, e) => Edit(s => s with { FocusSessions = (int)Math.Round(e.NewValue) });
        AutoHide.Checked += (_, _) => Edit(s => s with { Visibility = IslandVisibility.AutoHide });
        Always.Checked += (_, _) => Edit(s => s with { Visibility = IslandVisibility.Always });
        Small.Checked += (_, _) => Edit(s => s with { Size = IslandSize.Small });
        Medium.Checked += (_, _) => Edit(s => s with { Size = IslandSize.Medium });
        Large.Checked += (_, _) => Edit(s => s with { Size = IslandSize.Large });
        Black.Checked += (_, _) => Edit(s => s with { UseDockColor = false });
        DockColor.Checked += (_, _) => Edit(s => s with { UseDockColor = true });
        EventSeconds.ValueChanged += (_, e) => Edit(s => s with { EventSeconds = (int)Math.Round(e.NewValue) });
        PageSync.OnToggle(DevicesPanel, on => Edit(s => s with { DevicesPanel = on }));
        PageSync.OnToggle(WeatherPanel, on => Edit(s => s with { WeatherPanel = on }));
        Celsius.Checked += (_, _) => Edit(s => s with { Unit = TemperatureUnit.Celsius });
        Fahrenheit.Checked += (_, _) => Edit(s => s with { Unit = TemperatureUnit.Fahrenheit });
        // The city is saved when typing is done (Enter or leaving the box), not on every key: each save looks it up.
        City.LostFocus += (_, _) => SaveCity();
        City.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) SaveCity();
        };
    }

    private void SaveCity()
    {
        var text = City.Text.Trim();
        Edit(s => s.City == text ? s : s with { City = text });
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
        Mascot.IsChecked = s.Mascot;
        Lyrics.IsChecked = s.Lyrics;
        Lyrics.IsEnabled = s.Media;
        FocusPanel.IsChecked = s.FocusPanel;
        TasksPanel.IsChecked = s.TasksPanel;
        NotesPanel.IsChecked = s.NotesPanel;
        ClipboardPanel.IsChecked = s.ClipboardPanel;
        ClipboardImages.IsChecked = s.ClipboardImages;
        ClipboardImages.IsEnabled = s.ClipboardPanel;
        ShelfPanel.IsChecked = s.ShelfPanel;
        FocusOptions.IsEnabled = s.FocusPanel;
        FocusMinutes.Value = s.FocusMinutes;
        ShortBreak.Value = s.ShortBreakMinutes;
        LongBreak.Value = s.LongBreakMinutes;
        FocusSessions.Value = s.FocusSessions;
        FocusSound.IsChecked = s.FocusSound;
        EventSeconds.Value = s.EventSeconds;
        Small.IsChecked = s.Size == IslandSize.Small;
        Medium.IsChecked = s.Size == IslandSize.Medium;
        Large.IsChecked = s.Size == IslandSize.Large;
        Black.IsChecked = !s.UseDockColor;
        DockColor.IsChecked = s.UseDockColor;
        DevicesPanel.IsChecked = s.DevicesPanel;
        WeatherPanel.IsChecked = s.WeatherPanel;
        WeatherOptions.IsEnabled = s.WeatherPanel;
        if (!City.IsKeyboardFocused) City.Text = s.City;
        Celsius.IsChecked = s.Unit == TemperatureUnit.Celsius;
        Fahrenheit.IsChecked = s.Unit == TemperatureUnit.Fahrenheit;
        PlaceHint.Text = s.City.Length == 0
            ? "Type your city, e.g. Jakarta, and press Enter. Only the city name and its coordinates are sent to open-meteo.com."
            : s.Place is { } place && string.Equals(place.Query, s.City, StringComparison.OrdinalIgnoreCase)
                ? $"Showing the weather for {place.Name}."
                : "The city is looked up the next time the island opens.";
    }

    // The city is personal data the user typed, not a look: a reset keeps it.
    private void OnReset(object sender, RoutedEventArgs e) =>
        _sync.Edit(s => s with { Island = IslandSettings.Default with { City = s.Island.City, Place = s.Island.Place } });
}
