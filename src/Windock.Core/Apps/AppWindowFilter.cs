namespace Windock.Core.Apps;

/// Everything the filter needs to know about a top-level window, read by the WindowTracker.
public sealed record WindowFacts(
    bool IsVisible,
    bool IsCloaked,
    bool IsToolWindow,
    bool IsAppWindow,
    bool IsNoActivate,
    bool HasOwner,
    bool HiddenFromTaskbar,
    bool IsMinimized,
    int Width,
    int Height,
    string Title,
    string ClassName,
    string ProcessName,
    bool IsOwnProcess,
    bool IsChild = false);

/// Which windows deserve a dock icon — close to the taskbar's own rules (ARCHITECTURE §6.1).
public static class AppWindowFilter
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    /// Background helpers that sometimes own visible windows but are never apps the user opened.
    private static readonly HashSet<string> HelperProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedgewebview2", "TextInputHost", "ShellExperienceHost", "SearchHost", "SearchApp", "StartMenuExperienceHost",
        "LockApp", "RuntimeBroker", "SystemSettingsBroker", "ctfmon", "dwm",
    };

    /// explorer.exe also owns many shell windows; only its folder windows are "File Explorer".
    private static readonly HashSet<string> ExplorerFolderClasses = new(StringComparer.Ordinal) { "CabinetWClass", "ExploreWClass" };

    public static bool IsAppWindow(WindowFacts w)
    {
        if (!w.IsVisible || w.IsCloaked || w.HiddenFromTaskbar) return false;
        // Child windows live inside another window (e.g. Electron's "Chrome Legacy Window"); never their own app.
        if (w.IsChild) return false;
        if (!w.IsAppWindow && (w.IsToolWindow || w.HasOwner || w.IsNoActivate)) return false;
        if (!w.IsAppWindow && w.Title.Length == 0) return false;
        if (!w.IsMinimized && (w.Width <= 1 || w.Height <= 1)) return false;
        if (ShellClasses.Contains(w.ClassName)) return false;
        if (HelperProcesses.Contains(w.ProcessName)) return false;
        if (w.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && !ExplorerFolderClasses.Contains(w.ClassName)) return false;
        return true;
    }
}
