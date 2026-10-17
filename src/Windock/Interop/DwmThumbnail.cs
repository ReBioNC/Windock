using System.Runtime.InteropServices;

namespace Windock.Interop;

/// A live DWM thumbnail of another window drawn inside one of ours — what the taskbar previews use.
internal sealed class DwmThumbnail : IDisposable
{
    private const int DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public Native.RECT rcDestination;
        public Native.RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref DWM_THUMBNAIL_PROPERTIES properties);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out SIZE size);

    private readonly IntPtr _handle;

    private DwmThumbnail(IntPtr handle) => _handle = handle;

    public static DwmThumbnail? Create(IntPtr destination, IntPtr source) =>
        DwmRegisterThumbnail(destination, source, out var handle) == 0 ? new DwmThumbnail(handle) : null;

    public (int Width, int Height) SourceSize =>
        DwmQueryThumbnailSourceSize(_handle, out var size) == 0 ? (size.cx, size.cy) : (0, 0);

    /// Draws the source, keeping its aspect ratio, centred in the given rectangle (destination client pixels).
    public void Show(int x, int y, int width, int height)
    {
        var (sw, sh) = SourceSize;
        if (sw > 0 && sh > 0)
        {
            var scale = Math.Min((double)width / sw, (double)height / sh);
            var w = (int)(sw * scale);
            var h = (int)(sh * scale);
            x += (width - w) / 2;
            y += (height - h) / 2;
            width = w;
            height = h;
        }
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new Native.RECT { Left = x, Top = y, Right = x + width, Bottom = y + height },
            opacity = 255,
            fVisible = true,
            fSourceClientAreaOnly = false,
        };
        DwmUpdateThumbnailProperties(_handle, ref props);
    }

    public void Dispose() => DwmUnregisterThumbnail(_handle);
}
