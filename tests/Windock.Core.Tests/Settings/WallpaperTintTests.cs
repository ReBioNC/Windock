using Windock.Core.Settings;

namespace Windock.Core.Tests.Settings;

public class WallpaperTintTests
{
    [Fact]
    public void Averages_bgra_pixels()
    {
        byte[] pixels = { 0, 0, 200, 255, /* red */ 100, 0, 0, 255 /* blue */ };

        Assert.Equal(new ThemeColor(100, 0, 50), WallpaperTint.Average(pixels));
    }

    [Fact]
    public void No_pixels_no_color() => Assert.Null(WallpaperTint.Average(ReadOnlySpan<byte>.Empty));

    [Fact]
    public void Bright_colors_are_darkened_keeping_the_hue()
    {
        var plate = WallpaperTint.ToPlate(new ThemeColor(200, 100, 50));

        Assert.True(WallpaperTint.Brightness(plate) <= WallpaperTint.MaxBrightness + 0.5);
        Assert.True(plate.R > plate.G && plate.G > plate.B);
    }

    [Fact]
    public void Dark_colors_are_kept()
    {
        var dark = new ThemeColor(20, 30, 40);

        Assert.Equal(dark, WallpaperTint.ToPlate(dark));
    }
}
