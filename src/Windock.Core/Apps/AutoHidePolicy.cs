using Windock.Core.Settings;

namespace Windock.Core.Apps;

/// Decides whether the dock slides out of sight (DESIGN §4, dock auto-hide).
public static class AutoHidePolicy
{
    /// pointerUsingDock: the pointer is on the dock, its bottom edge trigger, a preview, a menu, or dragging.
    /// desktopInFront: no app window is active (desktop clicked, Win+D, everything minimized) — the dock stays up.
    public static bool ShouldHide(DockAutoHide mode, bool windowCoversDock, bool pointerUsingDock, bool desktopInFront = false) =>
        !desktopInFront && mode switch
    {
        DockAutoHide.Always => !pointerUsingDock,
        DockAutoHide.WhenCovered => windowCoversDock && !pointerUsingDock,
        _ => false,
    };

    /// A dock that can hide leaves the whole screen to windows; only an always-visible dock reserves its strip.
    public static bool ReservesScreenSpace(DockAutoHide mode) => mode == DockAutoHide.Never;

    /// Rectangles in screen pixels (right/bottom exclusive).
    public static bool Covers((int Left, int Top, int Right, int Bottom) window, (int Left, int Top, int Right, int Bottom) dock) =>
        window.Left < dock.Right && window.Right > dock.Left && window.Top < dock.Bottom && window.Bottom > dock.Top;
}
