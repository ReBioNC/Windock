namespace Windock.Core.Settings;

/// "Ikuti warna wallpaper": the wallpaper's average color, darkened so white icons and dots stay readable.
public static class WallpaperTint
{
    /// Brightest allowed plate (perceived brightness 0–255).
    public const double MaxBrightness = 64;

    /// Average of 32-bit BGRA pixels (alpha ignored); null when there are none.
    public static ThemeColor? Average(ReadOnlySpan<byte> bgra)
    {
        var count = bgra.Length / 4;
        if (count == 0) return null;
        long b = 0, g = 0, r = 0;
        for (var i = 0; i < count * 4; i += 4)
        {
            b += bgra[i];
            g += bgra[i + 1];
            r += bgra[i + 2];
        }
        return new ThemeColor((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    /// Keeps the hue, scales the color down until it is no brighter than MaxBrightness.
    public static ThemeColor ToPlate(ThemeColor average)
    {
        var brightness = Brightness(average);
        return brightness <= MaxBrightness ? average : average.Darken(1 - MaxBrightness / brightness);
    }

    public static double Brightness(ThemeColor c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
}
