namespace Windock.Core.Apps;

/// Is a window a real fullscreen app (game, video, F11)? Such windows hide the whole dock.
public static class FullscreenRule
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    /// Rectangles in screen pixels. A maximized window is never fullscreen: when the dock auto-hides
    /// the work area is the whole monitor, so maximized windows cover it too (v0.5.0 bug).
    public static bool IsFullscreenApp((int Left, int Top, int Right, int Bottom) window, (int Left, int Top, int Right, int Bottom) monitor,
        bool isMaximized, bool isVisible, string className, bool isOwnProcess)
    {
        if (!isVisible || isMaximized || isOwnProcess || ShellClasses.Contains(className)) return false;
        return window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }
}
