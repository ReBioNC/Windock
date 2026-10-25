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
        MakeTransparent();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// Callable from any thread (crash handlers restore the taskbar off the UI thread).
    public void ShowTaskbarWindows()
    {
        _keepHidden = false;
        if (_hideTimer.Dispatcher.CheckAccess()) _hideTimer.Stop();
        else _hideTimer.Dispatcher.BeginInvoke(_hideTimer.Stop);
        foreach (var h in TaskbarWindows())
        {
            var ex = Native.ExStyle(h);
            if ((ex & Native.WS_EX_LAYERED) != 0) Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex & ~Native.WS_EX_LAYERED));
            Native.ShowWindow(h, Native.SW_SHOW);
        }
    }

    /// Explorer re-shows the auto-hidden taskbar on its own (dozens of times while Task Manager starts), and
    /// each time its 2 px strip flashed at the screen edge until HideNow caught up. Fully transparent, those
    /// re-shows are invisible. Windows never makes the taskbar layered itself, so ShowTaskbarWindows simply
    /// clears the style, also after a killed session; a restarted Explorer starts opaque anyway.
    private static void MakeTransparent()
    {
        foreach (var h in TaskbarWindows())
        {
            Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(Native.ExStyle(h) | Native.WS_EX_LAYERED));
            Native.SetLayeredWindowAttributes(h, 0, 0, Native.LWA_ALPHA);
        }
    }

    private void OnWindowEvent(uint evt, IntPtr hwnd)
    {
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
