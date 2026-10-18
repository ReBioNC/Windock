using System.Windows.Threading;
using Windock.Core.Abstractions;
using Windock.Core.Logging;

namespace Windock.Interop;

/// The real Windows taskbar. Hiding waits for the auto-hide transition (which re-shows the taskbar)
/// and then keeps it hidden whenever Start/Search is not in front (spike S2).
internal sealed class Win32TaskbarController : ITaskbarController
{
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(700);

    private readonly ILog _log;
    private readonly DispatcherTimer _hideTimer;
    private bool _keepHidden;
    private bool _peeking;

    /// Windows that belong to the tray while it is peeked: the taskbar, the hidden-icons overflow, shell flyouts, menus.
    private static readonly HashSet<string> TrayClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow", "#32768",
    };

    public Win32TaskbarController(ILog log, Dispatcher dispatcher, WindowTracker tracker)
    {
        _log = log;
        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = HideDelay };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _keepHidden = true;
            HideNow();
            // Back-to-back auto-hide changes (e.g. recovery then re-apply) can re-show the taskbar late; check again.
            foreach (var delay in new[] { 1000, 3000 })
            {
                var check = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(delay) };
                check.Tick += (_, _) =>
                {
                    check.Stop();
                    if (_keepHidden && !StartMenuInFront() && TaskbarWindows().Any(Native.IsWindowVisible)) HideNow();
                };
                check.Start();
            }
        };
        tracker.RawEvent += OnWindowEvent;
    }

    public int GetAppBarState()
    {
        var data = Native.APPBARDATA.Create();
        return (int)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref data);
    }

    public void SetAppBarState(int state)
    {
        var data = Native.APPBARDATA.Create();
        data.lParam = new IntPtr(state);
        Native.SHAppBarMessage(Native.ABM_SETSTATE, ref data);
        _log.Info($"taskbar state set to {state}");
    }

    /// A killed app bar keeps its screen space until the taskbar state changes; flipping auto-hide
    /// off and on (or on and off) makes the shell recompute the work area. Used only after an unclean exit.
    public void RefreshWorkArea()
    {
        var state = GetAppBarState();
        SetAppBarState(state ^ TaskbarStates.AutoHide);
        Thread.Sleep(300);
        SetAppBarState(state);
    }

    public void HideTaskbarWindows()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// Callable from any thread (crash handlers restore the taskbar off the UI thread).
    public void ShowTaskbarWindows()
    {
        _keepHidden = false;
        if (_hideTimer.Dispatcher.CheckAccess()) _hideTimer.Stop();
        else _hideTimer.Dispatcher.BeginInvoke(_hideTimer.Stop);
        foreach (var h in TaskbarWindows()) Native.ShowWindow(h, Native.SW_SHOW);
    }

    /// "Cara A": shows the real Windows taskbar for a moment with its hidden-icons tray open; it goes
    /// back to the chosen mode as soon as something outside the tray takes the focus.
    public void PeekTray()
    {
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main == IntPtr.Zero) return;
        _peeking = true;
        Native.ShowWindow(main, Native.SW_SHOW);
        Native.SetForegroundWindow(main);
        var notify = Native.FindWindowEx(main, IntPtr.Zero, "TrayNotifyWnd", null);
        var chevron = notify == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(notify, IntPtr.Zero, "Button", null);
        if (chevron != IntPtr.Zero) Native.PostMessage(chevron, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        _log.Info($"tray peek (overflow button {(chevron != IntPtr.Zero ? "clicked" : "not found")})");
    }

    private const int BM_CLICK = 0x00F5;

    private void OnWindowEvent(uint evt, IntPtr hwnd)
    {
        if (_peeking)
        {
            if (evt != Native.EVENT_SYSTEM_FOREGROUND || TrayClasses.Contains(Native.ClassName(Native.GetForegroundWindow()))) return;
            _peeking = false;
            _log.Info("tray peek ended");
        }
        if (!_keepHidden || (evt != Native.EVENT_OBJECT_SHOW && evt != Native.EVENT_SYSTEM_FOREGROUND)) return;
        if (StartMenuInFront()) return;  // Start/Search in use; hidden again when focus moves on
        if (TaskbarWindows().Any(Native.IsWindowVisible)) HideNow();
    }

    private static bool StartMenuInFront() => Native.ClassName(Native.GetForegroundWindow()) == "Windows.UI.Core.CoreWindow";

    private static void HideNow()
    {
        foreach (var h in TaskbarWindows()) Native.ShowWindow(h, Native.SW_HIDE);
    }

    private static IEnumerable<IntPtr> TaskbarWindows()
    {
        var main = Native.FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        var h = IntPtr.Zero;
        while ((h = Native.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) yield return h;
    }
}
