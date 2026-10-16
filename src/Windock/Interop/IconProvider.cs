using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windock.Core.Logging;

namespace Windock.Interop;

/// Loads shell icons at the size the dock needs on a dedicated STA thread and caches them (ARCHITECTURE §6.5).
internal sealed class IconProvider : IDisposable
{
    private static readonly int[] Sizes = { 32, 48, 64, 96, 128, 256 };

    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _worker;
    private readonly Dictionary<(string Key, int Px), ImageSource> _cache = new();

    public IconProvider(ILog log, Dispatcher ui)
    {
        _log = log;
        _ui = ui;
        Generic = CreateGeneric();
        _worker = new Thread(() =>
        {
            foreach (var work in _queue.GetConsumingEnumerable()) work();
        }) { IsBackground = true, Name = "Windock icons" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public ImageSource Generic { get; }

    /// Smallest standard size that covers the requested physical pixel size.
    public static int PixelSizeFor(double dip, double dpiScale) =>
        Sizes.FirstOrDefault(s => s >= Math.Ceiling(dip * dpiScale), Sizes[^1]);

    /// Calls onReady on the UI thread — immediately when cached. key is the dock item key.
    public void Request(string key, string? iconPath, IntPtr hwndFallback, int pixelSize, Action<string, ImageSource> onReady)
    {
        if (_cache.TryGetValue((key, pixelSize), out var cached))
        {
            onReady(key, cached);
            return;
        }

        var parsingName = key.StartsWith("aumid:", StringComparison.Ordinal) ? @"shell:AppsFolder\" + key["aumid:".Length..] : iconPath;
        // Window icons must be read on the UI thread (message to the owning window).
        var windowIcon = parsingName is null && hwndFallback != IntPtr.Zero ? WindowIcon(hwndFallback) : null;

        _queue.Add(() =>
        {
            var image = windowIcon ?? PackageLogo(iconPath) ?? (parsingName is null ? null : ShellImage(parsingName, pixelSize));
            _ui.BeginInvoke(() =>
            {
                var result = image ?? Generic;
                if (image is null) _log.Info($"no icon for {key} ({parsingName ?? "window"}), using generic");
                _cache[(key, pixelSize)] = result;
                onReady(key, result);
            });
        });
    }

    /// Store apps: the shell draws their icon on a coloured tile; the package's unplated logo looks like a real app icon.
    private BitmapSource? PackageLogo(string? exePath)
    {
        if (exePath is null) return null;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(exePath);
            for (var i = 0; dir is not null && i < 4 && !System.IO.File.Exists(System.IO.Path.Combine(dir, "AppxManifest.xml")); i++)
                dir = System.IO.Path.GetDirectoryName(dir);
            if (dir is null) return null;

            var manifest = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(dir, "AppxManifest.xml"));
            var logo = manifest.Descendants().Where(e => e.Name.LocalName == "VisualElements")
                .Select(e => (string?)e.Attribute("Square44x44Logo")).FirstOrDefault(v => v is not null);
            if (logo is null) return null;

            var assetDir = System.IO.Path.Combine(dir, System.IO.Path.GetDirectoryName(logo) ?? "");
            var stem = System.IO.Path.GetFileNameWithoutExtension(logo);
            var candidates = new[]
                {
                    ".targetsize-256_altform-unplated", ".targetsize-256_altform-lightunplated", ".targetsize-96_altform-unplated",
                    ".targetsize-64_altform-unplated", ".targetsize-48_altform-unplated", ".targetsize-256", ".scale-400", ".scale-200", "",
                }
                .Select(q => System.IO.Path.Combine(assetDir, stem + q + ".png"));
            var file = candidates.FirstOrDefault(System.IO.File.Exists);
            if (file is null) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Xml.XmlException or NotSupportedException)
        {
            _log.Error($"package logo failed for {exePath}", ex);
            return null;
        }
    }

    private BitmapSource? ShellImage(string parsingName, int px)
    {
        try
        {
            if (ShellInterop.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ShellInterop.IID_IShellItemImageFactory, out var factory) != 0) return null;
            try
            {
                if (factory.GetImage(new ShellInterop.SIZE { cx = px, cy = px }, ShellInterop.SIIGBF_ICONONLY, out var hBitmap) != 0) return null;
                try { return ToBitmapSource(hBitmap); }
                finally { ShellInterop.DeleteObject(hBitmap); }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            _log.Error($"icon load failed for {parsingName}", ex);
            return null;
        }
    }

    private static BitmapSource? WindowIcon(IntPtr hwnd)
    {
        Native.SendMessageTimeout(hwnd, Native.WM_GETICON, Native.ICON_BIG, IntPtr.Zero, 0x2 /* SMTO_ABORTIFHUNG */, 200, out var icon);
        if (icon == IntPtr.Zero) icon = Native.GetClassLongPtr(hwnd, Native.GCLP_HICON);
        if (icon == IntPtr.Zero) return null;
        var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    /// Copies a 32bpp premultiplied HBITMAP keeping its alpha (CreateBitmapSourceFromHBitmap drops it).
    private static BitmapSource? ToBitmapSource(IntPtr hBitmap)
    {
        if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.bmBitsPixel != 32) return null;
        var info = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = bm.bmWidth, biHeight = -bm.bmHeight, biPlanes = 1, biBitCount = 32,
        };
        var pixels = new byte[bm.bmWidth * bm.bmHeight * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hBitmap, 0, (uint)bm.bmHeight, pixels, ref info, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
        var source = BitmapSource.Create(bm.bmWidth, bm.bmHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, bm.bmWidth * 4);
        source.Freeze();
        return source;
    }

    private static ImageSource CreateGeneric()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new LinearGradientBrush(Color.FromRgb(0x9A, 0xA4, 0xB2), Color.FromRgb(0x5C, 0x66, 0x75), 90);
            dc.DrawRoundedRectangle(brush, null, new Rect(16, 16, 224, 224), 52, 52);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), null, new Rect(72, 92, 112, 76), 12, 12);
        }
        var bmp = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    public void Dispose() => _queue.CompleteAdding();

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr obj, int size, out BITMAP bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
