using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windock.Core.Layout;

namespace Windock.Interop;

/// A still picture of a window and where it was on screen (physical pixels, visible frame only), taken just
/// before it minimizes: once minimized, Windows no longer has a picture of it to show.
internal sealed record WindowSnapshot(BitmapSource Image, RectD Bounds, DateTime TakenUtc)
{
    /// Kept at most this wide; the animation is quick and small, and a minimized window keeps its snapshot.
    private const int MaxWidth = 1280;
    private const uint PW_RENDERFULLCONTENT = 0x2;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// Any thread. Null when the window cannot be drawn (gone, minimized, zero size, or the copy failed).
    public static WindowSnapshot? Take(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd) || !Native.GetWindowRect(hwnd, out var window)) return null;
        var frame = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT f, Marshal.SizeOf<Native.RECT>()) == 0 ? f : window;
        int ww = window.Right - window.Left, wh = window.Bottom - window.Top;
        int fw = frame.Right - frame.Left, fh = frame.Bottom - frame.Top;
        if (ww <= 0 || wh <= 0 || fw <= 0 || fh <= 0 || ww * (long)wh > 40_000_000) return null;

        var screen = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, ww, wh);
        var old = SelectObject(dc, bitmap);
        try
        {
            if (!PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT)) return null;
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero,
                new Int32Rect(frame.Left - window.Left, frame.Top - window.Top, Math.Min(fw, ww), Math.Min(fh, wh)),
                BitmapSizeOptions.FromEmptyOptions());
            BitmapSource image = source;
            if (source.PixelWidth > MaxWidth)
            {
                var k = (double)MaxWidth / source.PixelWidth;
                image = new TransformedBitmap(source, new ScaleTransform(k, k));
            }
            // Opaque copy: PrintWindow leaves the alpha channel undefined, and a fixed copy frees the GDI bitmap.
            var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
            var copy = new WriteableBitmap(pixels);
            copy.Freeze();
            return new WindowSnapshot(copy, new RectD(frame.Left, frame.Top, fw, fh), DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bitmap);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Native.RECT value, int size);
}
