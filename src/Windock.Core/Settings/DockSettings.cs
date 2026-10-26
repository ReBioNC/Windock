using Windock.Core.Launcher;

namespace Windock.Core.Settings;

public enum DockPosition { Bottom, Left, Right }

public enum BlurMode { Off, Standard, Acrylic }

public enum TaskbarMode { Hidden, AutoHide, Visible }

/// Windock = dock replaces the taskbar; Windows = everything back to stock Windows, settings kept.
public enum DockMode { Windock, Windows }

/// When the dock slides out of sight: never, while a window covers it (e.g. maximized), or always (Mac-style).
public enum DockAutoHide { Never, WhenCovered, Always }

/// How a window asking for attention (flashing) is shown on its dock icon.
public enum AttentionStyle { Bounce, Dot, Off }

public enum AttentionRepeat { UntilOpened, Once }

public sealed record MagnificationSettings(bool Enabled, int Size);

/// Colors are "#RRGGBB".
public sealed record ThemeSettings(string PlateColor, int CornerRadius, bool ShowBorder, string IndicatorColor, string AccentColor, bool FollowWallpaper)
{
    public static readonly ThemeSettings Default = new("#1E1E1E", 16, true, "#FFFFFF", "#2F6FE4", false);
}

public sealed record StatusSettings(
    bool Enabled, bool ShowTray, bool ShowWifi, bool ShowVolume, bool ShowBattery, bool ShowBatteryPercent,
    bool ShowLanguage, bool ShowClock, bool Clock24Hour, bool ShowDate, bool ShowSeconds, bool ShowNotifications)
{
    public static readonly StatusSettings Default = new(
        Enabled: true, ShowTray: true, ShowWifi: true, ShowVolume: true, ShowBattery: true, ShowBatteryPercent: false,
        ShowLanguage: true, ShowClock: true, Clock24Hour: true, ShowDate: true, ShowSeconds: false, ShowNotifications: true);
}

/// When the Dynamic Island slides out of view: while the window in front overlaps it, or never.
public enum IslandVisibility { AutoHide, Always }

public enum IslandSize { Small, Medium, Large }

public enum TemperatureUnit { Celsius, Fahrenheit }

/// The weather panel's city as found by the geocoder. Query is the text it was found from: when the City
/// setting no longer matches it, the city is looked up again.
public sealed record WeatherPlace(string Query, string Name, double Latitude, double Longitude);

/// The Dynamic Island at the top centre of the screen. ShowClock/ShowDate/ShowBattery move those items
/// out of the dock's status area (see IslandOwnership).
public sealed record IslandSettings(
    bool Enabled, IslandVisibility Visibility, bool ShowClock, bool ShowDate, bool ShowBattery,
    bool Media, bool VolumeEvents, bool BrightnessEvents, bool BatteryEvents, bool DeviceEvents,
    bool ExpandOnHover, int EventSeconds, IslandSize Size, bool UseDockColor,
    bool DevicesPanel = true, bool WeatherPanel = true, string City = "", TemperatureUnit Unit = TemperatureUnit.Celsius,
    WeatherPlace? Place = null, bool Mascot = true)
{
    public static readonly IslandSettings Default = new(
        Enabled: true, Visibility: IslandVisibility.AutoHide, ShowClock: true, ShowDate: true, ShowBattery: true,
        Media: true, VolumeEvents: true, BrightnessEvents: true, BatteryEvents: true, DeviceEvents: true,
        ExpandOnHover: true, EventSeconds: 2, Size: IslandSize.Medium, UseDockColor: false);
}

public enum SwitcherCardSize { Small, Medium, Large }

/// Windock's Alt+Tab switcher (off = Windows' own Alt+Tab).
public sealed record SwitcherSettings(bool Enabled, bool GroupByApp, SwitcherCardSize CardSize, bool CurrentScreenOnly, bool LivePreviews,
    bool WindowsSwitcherInFullscreen)
{
    public static readonly SwitcherSettings Default = new(false, true, SwitcherCardSize.Medium, false, true, true);
}

public enum LauncherLayout { Grid, List }

/// MaxResults applies to the List layout. The Grid layout shows tabs (Frequent, All, categories) and fills
/// Frequent from launch history.
public sealed record LauncherSettings(bool Enabled, string Hotkey, int MaxResults, bool IncludeFiles)
{
    public static readonly LauncherSettings Default = new(true, "Alt+Space", 8, false);

    public LauncherLayout Layout { get; init; } = LauncherLayout.Grid;
    public bool ShowCategories { get; init; } = true;
    public bool UseWindowsHistory { get; init; } = true;
    public bool TrackLaunches { get; init; } = true;
    public int FrequentCount { get; init; } = 18;
    public int Columns { get; init; } = 6;
    public IReadOnlyList<LauncherCategoryOverride> CategoryOverrides { get; init; } = Array.Empty<LauncherCategoryOverride>();

    public bool Equals(LauncherSettings? other) =>
        other is not null && Enabled == other.Enabled && Hotkey == other.Hotkey && MaxResults == other.MaxResults
        && IncludeFiles == other.IncludeFiles && Layout == other.Layout && ShowCategories == other.ShowCategories
        && UseWindowsHistory == other.UseWindowsHistory && TrackLaunches == other.TrackLaunches && FrequentCount == other.FrequentCount
        && Columns == other.Columns && CategoryOverrides.SequenceEqual(other.CategoryOverrides);

    public override int GetHashCode() => HashCode.Combine(Enabled, Hotkey, MaxResults, IncludeFiles, Layout, FrequentCount, Columns, CategoryOverrides.Count);
}

public sealed record TrashSettings(bool ShowRecycleBin, bool ConfirmBeforeTrash)
{
    public static readonly TrashSettings Default = new(true, true);
}

/// A running app the user chose not to show in the dock.
public sealed record HiddenApp(string AppId, string Name);

public sealed record AppearanceSettings(
    DockPosition Position,
    int IconSize,
    MagnificationSettings Magnification,
    double BackgroundOpacity,
    BlurMode Blur,
    bool ShowRunningIndicators);

public sealed record BehaviorSettings(TaskbarMode TaskbarMode, DockAutoHide AutoHide, bool BounceOnLaunch, bool StartWithWindows,
    bool ExplorerContextMenu = true, DockMode Mode = DockMode.Windock,
    AttentionStyle Attention = AttentionStyle.Bounce, AttentionRepeat AttentionRepeat = AttentionRepeat.UntilOpened,
    bool AttentionFromNotifications = true, bool ShowBadgeCounts = true);

public sealed record SafetySettings(string EmergencyHotkey);

public sealed record PinEntry(string Path);

public sealed record DockSettings(
    int Version,
    AppearanceSettings Appearance,
    BehaviorSettings Behavior,
    IReadOnlyList<PinEntry> Pins,
    SafetySettings Safety,
    bool FirstRunCompleted)
{
    public ThemeSettings Theme { get; init; } = ThemeSettings.Default;
    public StatusSettings Status { get; init; } = StatusSettings.Default;
    public LauncherSettings Launcher { get; init; } = LauncherSettings.Default;
    public SwitcherSettings Switcher { get; init; } = SwitcherSettings.Default;
    public TrashSettings Trash { get; init; } = TrashSettings.Default;
    public IslandSettings Island { get; init; } = IslandSettings.Default;
    public IReadOnlyList<HiddenApp> HiddenApps { get; init; } = Array.Empty<HiddenApp>();

    // Lists compare by content, not by reference.
    public bool Equals(DockSettings? other) =>
        other is not null
        && Version == other.Version
        && Appearance == other.Appearance
        && Behavior == other.Behavior
        && Pins.SequenceEqual(other.Pins)
        && Safety == other.Safety
        && FirstRunCompleted == other.FirstRunCompleted
        && Theme == other.Theme
        && Status == other.Status
        && Launcher == other.Launcher
        && Switcher == other.Switcher
        && Trash == other.Trash
        && Island == other.Island
        && HiddenApps.SequenceEqual(other.HiddenApps);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        hash.Add(Appearance);
        hash.Add(Behavior);
        foreach (var pin in Pins) hash.Add(pin);
        hash.Add(Safety);
        hash.Add(FirstRunCompleted);
        hash.Add(Theme);
        hash.Add(Status);
        hash.Add(Launcher);
        hash.Add(Switcher);
        hash.Add(Trash);
        hash.Add(Island);
        foreach (var app in HiddenApps) hash.Add(app);
        return hash.ToHashCode();
    }
}
