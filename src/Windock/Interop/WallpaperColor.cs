using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windock.Core.Settings;

namespace Windock.Interop;

/// The dock plate color for "Ikuti warna wallpaper": the wallpaper's average color, darkened.
internal static class WallpaperColor
{
    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    private const int COLOR_DESKTOP = 1;

    /// Reads and averages the wallpaper on a worker thread (decoded at 48 px wide, so this stays cheap).
    public static Task<string> ComputeAsync() => Task.Run(Compute);

    private static string Compute()
    {
        foreach (var path in Candidates())
        {
            try
            {
                if (!File.Exists(path)) continue;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                image.UriSource = new Uri(path);
                image.DecodePixelWidth = 48;
                image.EndInit();
                var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
                bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
                if (WallpaperTint.Average(pixels) is { } average) return WallpaperTint.ToPlate(average).ToHex();
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or COMException)
            {
                // Not a readable image: try the next candidate, then the solid desktop color.
            }
        }
        var c = GetSysColor(COLOR_DESKTOP);
        return WallpaperTint.ToPlate(new ThemeColor((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF))).ToHex();
    }

    private static IEnumerable<string> Candidates()
    {
        var buffer = new StringBuilder(520);
        if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)buffer.Capacity, buffer, 0) && buffer.Length > 0) yield return buffer.ToString();
        // Windows keeps its own copy, which still exists after the original file is moved or deleted.
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint winIni);

    [DllImport("user32.dll")] private static extern uint GetSysColor(int index);
}
