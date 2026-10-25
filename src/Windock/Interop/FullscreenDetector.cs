using Windock.Core.Apps;

namespace Windock.Interop;

/// Reads the foreground window and applies FullscreenRule (Core).
internal static class FullscreenDetector
{
    public static bool IsFullscreenApp(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var r)) return false;
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref info)) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var m = info.rcMonitor;
        return FullscreenRule.IsFullscreenApp((r.Left, r.Top, r.Right, r.Bottom), (m.Left, m.Top, m.Right, m.Bottom),
            Native.IsZoomed(hwnd), Native.IsWindowVisible(hwnd), Native.ClassName(hwnd), pid == Environment.ProcessId,
            (Native.ExStyle(hwnd) & Native.WS_EX_LAYERED) != 0);
    }
}
