namespace Windock.Core.Settings;

public enum DockPosition { Bottom, Left, Right }

public enum BlurMode { Off, Standard, Acrylic }

public enum TaskbarMode { Hidden, AutoHide, Visible }

/// Windock = dock replaces the taskbar; Windows = everything back to stock Windows, settings kept.
public enum DockMode { Windock, Windows }

public sealed record MagnificationSettings(bool Enabled, int Size);

public sealed record AppearanceSettings(
    DockPosition Position,
    int IconSize,
    MagnificationSettings Magnification,
    double BackgroundOpacity,
    BlurMode Blur,
    bool ShowRunningIndicators);

public sealed record BehaviorSettings(TaskbarMode TaskbarMode, bool AutoHide, bool BounceOnLaunch, bool StartWithWindows, bool ExplorerContextMenu = true, DockMode Mode = DockMode.Windock);

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
    // Pins compare by content, not by list reference.
    public bool Equals(DockSettings? other) =>
        other is not null
        && Version == other.Version
        && Appearance == other.Appearance
        && Behavior == other.Behavior
        && Pins.SequenceEqual(other.Pins)
        && Safety == other.Safety
        && FirstRunCompleted == other.FirstRunCompleted;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        hash.Add(Appearance);
        hash.Add(Behavior);
        foreach (var pin in Pins) hash.Add(pin);
        hash.Add(Safety);
        hash.Add(FirstRunCompleted);
        return hash.ToHashCode();
    }
}
