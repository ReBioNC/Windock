using System.Windows;
using System.Windows.Threading;
using Windock.Core.Apps;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Dock;
using Windock.Interop;

namespace Windock.Composition;

/// Dock auto-hide: slides the dock away per the chosen mode and brings it back when the pointer
/// touches the bottom edge (or a file is dragged there). Never/WhenCovered/Always (DockAutoHide).
internal sealed class AutoHideController
{
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(600);
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    private readonly DockController _dock;
    private readonly Func<DockAutoHide> _mode;
    private readonly ILog _log;
    private readonly EdgeTriggerWindow _edge = new();
    private readonly DispatcherTimer _hideTimer;
    private bool _suspended;
    private bool _edgeHover;
    private bool _evaluationQueued;

    public AutoHideController(DockController dock, WindowTracker tracker, Func<DockAutoHide> mode, ILog log)
    {
        _dock = dock;
        _mode = mode;
        _log = log;
        _hideTimer = new DispatcherTimer { Interval = HideDelay };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (WantsHidden()) _dock.SetSlidOut(true);
        };

        _edge.MouseEnter += (_, _) => { _edgeHover = true; Reveal(); };
        _edge.MouseLeave += (_, _) => { _edgeHover = false; QueueEvaluate(); };
        _edge.DragEnter += (_, _) => Reveal();
        _dock.InteractionEnded += QueueEvaluate;
        tracker.ForegroundChanged += (_, _) => QueueEvaluate();
        tracker.RawEvent += (evt, hwnd) =>
        {
            // A window moved, was maximized/restored or minimized: re-check whether it covers the dock.
            if (evt is Native.EVENT_OBJECT_LOCATIONCHANGE or Native.EVENT_SYSTEM_MINIMIZESTART or Native.EVENT_SYSTEM_MINIMIZEEND
                && hwnd == Native.GetForegroundWindow())
                QueueEvaluate();
        };
    }

    /// Fullscreen apps and Windows mode hide the whole dock; auto-hide stands aside meanwhile.
    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        Apply();
    }

    /// Call after the mode setting changed.
    public void Apply()
    {
        var active = !_suspended && _mode() != DockAutoHide.Never;
        if (!active)
        {
            _hideTimer.Stop();
            _edge.Hide();
            // Suspended (a fullscreen window, including the Alt+Tab switcher) only pauses auto-hide: a hidden dock
            // stays hidden instead of popping up for a moment when the pause ends. Mode Never shows it for good.
            if (_mode() == DockAutoHide.Never) _dock.SetSlidOut(false);
            else if (_suspended && !_dock.IsPointerUsingDock) _dock.SetSlidOut(true);  // e.g. a game opened from the dock
            return;
        }
        _edge.Place();
        _edge.Show();
        _edge.RaiseToTop();
        _log.Info($"auto-hide: {_mode()}");
        Evaluate();
    }

    private void Reveal()
    {
        _hideTimer.Stop();
        _dock.RaiseToTop();
        _dock.SetSlidOut(false);
    }

    private void QueueEvaluate()
    {
        if (_evaluationQueued) return;
        _evaluationQueued = true;
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _evaluationQueued = false;
            Evaluate();
        });
    }

    private void Evaluate()
    {
        if (_suspended || _mode() == DockAutoHide.Never) return;
        _edge.RaiseToTop();  // runs on every foreground change: keeps the edge strip reachable
        if (!WantsHidden())
        {
            Reveal();
            return;
        }
        if (!_dock.IsSlidOut && !_hideTimer.IsEnabled) _hideTimer.Start();
    }

    private bool WantsHidden()
    {
        var mode = _mode();
        var covered = mode == DockAutoHide.WhenCovered && ForegroundCoversDock();
        return AutoHidePolicy.ShouldHide(mode, covered, _dock.IsPointerUsingDock || _edgeHover, DesktopInFront());
    }

    /// The desktop (or nothing) has focus: clicking the wallpaper, Win+D, or the last window minimized.
    private static bool DesktopInFront()
    {
        var hwnd = Native.GetForegroundWindow();
        return hwnd == IntPtr.Zero || Native.ClassName(hwnd) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private bool ForegroundCoversDock()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || Native.IsIconic(hwnd) || ShellClasses.Contains(Native.ClassName(hwnd))) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId || !Native.GetWindowRect(hwnd, out var r)) return false;
        return AutoHidePolicy.Covers((r.Left, r.Top, r.Right, r.Bottom), _dock.DockAreaPx);
    }
}
