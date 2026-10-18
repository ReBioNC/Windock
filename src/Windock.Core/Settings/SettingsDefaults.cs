namespace Windock.Core.Settings;

/// Default values from DESIGN.md §6.
public static class SettingsDefaults
{
    public static DockSettings Create() => new(
        Version: 1,
        Appearance: new AppearanceSettings(
            Position: DockPosition.Bottom,
            IconSize: 48,
            Magnification: new MagnificationSettings(Enabled: true, Size: 80),
            BackgroundOpacity: 0.55,
            Blur: BlurMode.Standard,
            ShowRunningIndicators: true),
        Behavior: new BehaviorSettings(
            TaskbarMode: TaskbarMode.Hidden,
            AutoHide: DockAutoHide.Never,
            BounceOnLaunch: true,
            StartWithWindows: false,
            ExplorerContextMenu: true,
            Mode: DockMode.Windock),
        Pins: Array.Empty<PinEntry>(),
        Safety: new SafetySettings(EmergencyHotkey: "Ctrl+Alt+Shift+W"),
        FirstRunCompleted: false);
}
