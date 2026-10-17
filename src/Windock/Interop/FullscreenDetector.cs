namespace Windock.Interop;

/// Is the window in front a real fullscreen app (game, video)? The desktop and shell surfaces cover the
/// whole screen too, and the shell's ABN_FULLSCREENAPP reports them as fullscreen — they must not hide the dock.
internal static class FullscreenDetector
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    public static bool IsFullscreenApp(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindowVisible(hwnd) || ShellClasses.Contains(Native.ClassName(hwnd))) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return false;
        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref info)) return false;
        var m = info.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}
