using Windock.Core.Layout;
using Windock.Core.Settings;

namespace Windock.Interop;

internal static class WindowEffects
{
    /// Not in Alt+Tab, never takes focus when clicked.
    public static void MakeToolNoActivate(IntPtr hwnd) => Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);

    public static void SetBlur(IntPtr hwnd, BlurMode mode) =>
        Native.SetAccent(hwnd, mode == BlurMode.Off ? Native.ACCENT_DISABLED : Native.ACCENT_ENABLE_BLURBEHIND);

    /// Dark caption on Windows 10 20H1+ (attribute 20; older builds used 19). Ignored where unsupported.
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        var on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// Region in window-relative physical pixels.
    public static void SetRoundRectRegion(IntPtr hwnd, RectD physicalRect, double radiusPx)
    {
        var d = (int)Math.Round(radiusPx * 2);
        var region = Native.CreateRoundRectRgn(
            (int)Math.Round(physicalRect.X), (int)Math.Round(physicalRect.Y),
            (int)Math.Round(physicalRect.Right) + 1, (int)Math.Round(physicalRect.Bottom) + 1, d, d);
        Native.SetWindowRgn(hwnd, region, true);  // the window owns the region from here on
    }
}
