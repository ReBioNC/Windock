namespace Windock.Core.Settings;

/// Clamps settings into the valid ranges from DESIGN.md §6.
public static class SettingsNormalizer
{
    public const int MinIconSize = 32;
    public const int MaxIconSize = 96;
    public const int MaxMagnificationSize = 128;
    public const double MinOpacityWithoutBlur = 0.30;
    public const int MinCornerRadius = 0, MaxCornerRadius = 30;
    public const int MinLauncherResults = 5, MaxLauncherResults = 12;
    public const int MinAnimationMs = 200, MaxAnimationMs = 1500;
    public const int MinLauncherFrequent = 8, MaxLauncherFrequent = 24, MinLauncherColumns = 4, MaxLauncherColumns = 8;
    public const int MinIslandEventSeconds = 1, MaxIslandEventSeconds = 5;

    public static DockSettings Normalize(DockSettings s)
    {
        var a = s.Appearance;
        var icon = Math.Clamp(a.IconSize, MinIconSize, MaxIconSize);
        var mag = Math.Clamp(a.Magnification.Size, icon, MaxMagnificationSize);
        var minOpacity = a.Blur == BlurMode.Off ? MinOpacityWithoutBlur : 0.0;
        var opacity = double.IsFinite(a.BackgroundOpacity) ? Math.Clamp(a.BackgroundOpacity, minOpacity, 1.0) : 0.55;

        var appearance = a with
        {
            IconSize = icon,
            Magnification = a.Magnification.Size == mag ? a.Magnification : a.Magnification with { Size = mag },
            BackgroundOpacity = opacity,
        };
        var t = s.Theme;
        var theme = t with
        {
            PlateColor = ThemeColor.Normalize(t.PlateColor) ?? ThemeSettings.Default.PlateColor,
            IndicatorColor = ThemeColor.Normalize(t.IndicatorColor) ?? ThemeSettings.Default.IndicatorColor,
            AccentColor = ThemeColor.Normalize(t.AccentColor) ?? ThemeSettings.Default.AccentColor,
            CornerRadius = Math.Clamp(t.CornerRadius, MinCornerRadius, MaxCornerRadius),
        };
        var launcher = s.Launcher with
        {
            MaxResults = Math.Clamp(s.Launcher.MaxResults, MinLauncherResults, MaxLauncherResults),
            FrequentCount = Math.Clamp(s.Launcher.FrequentCount, MinLauncherFrequent, MaxLauncherFrequent),
            Columns = Math.Clamp(s.Launcher.Columns, MinLauncherColumns, MaxLauncherColumns),
        };
        var island = s.Island with
        {
            EventSeconds = Math.Clamp(s.Island.EventSeconds, MinIslandEventSeconds, MaxIslandEventSeconds),
            City = (s.Island.City ?? "").Trim(),
            FocusMinutes = Math.Clamp(s.Island.FocusMinutes, 5, 90),
            ShortBreakMinutes = Math.Clamp(s.Island.ShortBreakMinutes, 1, 30),
            LongBreakMinutes = Math.Clamp(s.Island.LongBreakMinutes, 5, 60),
            FocusSessions = Math.Clamp(s.Island.FocusSessions, 2, 8),
        };

        var animations = s.Animations with { DurationMs = Math.Clamp(s.Animations.DurationMs, MinAnimationMs, MaxAnimationMs) };

        if (appearance == a && theme == t && launcher == s.Launcher && island == s.Island && animations == s.Animations) return s;
        return s with { Appearance = appearance, Theme = theme, Launcher = launcher, Island = island, Animations = animations };
    }
}
