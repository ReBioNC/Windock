namespace Windock.Core.Settings;

/// Clamps settings into the valid ranges from DESIGN.md §6.
public static class SettingsNormalizer
{
    public const int MinIconSize = 32;
    public const int MaxIconSize = 96;
    public const int MaxMagnificationSize = 128;
    public const double MinOpacityWithoutBlur = 0.30;

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
        return appearance == a ? s : s with { Appearance = appearance };
    }
}
