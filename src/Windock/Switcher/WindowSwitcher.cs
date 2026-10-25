using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windock.Core.Apps;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Interop;

namespace Windock.Switcher;

/// Windock's Alt+Tab: owns the keyboard hook while the option is on, builds the window list on Alt+Tab,
/// shows the panel after a short moment (a quick Alt+Tab tap just switches, like Windows) and switches on Alt release.
internal sealed class WindowSwitcher : IDisposable
{
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(110);

    private readonly SettingsStore _settings;
    private readonly WindowTracker _windows;
    private readonly IconProvider _icons;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _showTimer;
    private AltTabHook? _hook;
    private SwitcherWindow? _window;
    private List<SwitcherEntry> _mru = new();
    private List<SwitcherEntry> _all = new();
    private IReadOnlyList<SwitcherEntry> _shown = Array.Empty<SwitcherEntry>();
    private Dictionary<nint, WindowFacts> _facts = new();
    private string _query = "";
    private int _selected = -1;
    private bool _open;
    private IntPtr _monitor;

    public WindowSwitcher(SettingsStore settings, WindowTracker windows, IconProvider icons, ILog log, Dispatcher ui)
    {
        _settings = settings;
        _windows = windows;
        _icons = icons;
        _log = log;
        _ui = ui;
        _showTimer = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = ShowDelay };
        _showTimer.Tick += (_, _) =>
        {
            _showTimer.Stop();
            if (_open) Render(rebuild: true);
        };
        Sync();
    }

    /// Installs or removes the keyboard hook to match the setting.
    public void Sync()
    {
        var on = _settings.Current.Switcher.Enabled;
        if (on && _hook is null)
        {
            _hook = new AltTabHook(ShouldTakeOver);
            if (!_hook.Installed)
            {
                _log.Error("Alt+Tab hook could not be installed; Windows' Alt+Tab stays");
                _hook = null;
                return;
            }
            // Raised on the hook thread, in key order; the UI thread handles them in that same order.
            _hook.Key += key => _ui.BeginInvoke(DispatcherPriority.Send, () => OnKey(key));
            _hook.Typed += ch => _ui.BeginInvoke(DispatcherPriority.Send, () => OnTyped(ch));
            _hook.Released += () => _ui.BeginInvoke(DispatcherPriority.Send, Commit);
            _log.Info("Windock Alt+Tab on");
        }
        else if (!on && _hook is not null)
        {
            Close();
            _hook.Dispose();
            _hook = null;
            _log.Info("Windock Alt+Tab off");
        }
    }

    /// Called on the hook thread at each Alt+Tab. Windows keeps Alt+Tab only for exclusive fullscreen games
    /// (Direct3D owning the screen), where no other window can show; borderless "fullscreen" games get Windock's.
    private bool ShouldTakeOver()
    {
        if (!_settings.Current.Switcher.WindowsSwitcherInFullscreen) return true;
        if (SHQueryUserNotificationState(out var state) != 0 || state != QUNS_RUNNING_D3D_FULL_SCREEN) return true;
        _log.Info("Alt+Tab handed to Windows: exclusive fullscreen game in front");
        return false;
    }

    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

    private void OnKey(SwitcherKey key)
    {
        if (!_open)
        {
            if (key is SwitcherKey.Next or SwitcherKey.Previous) Open(key == SwitcherKey.Previous);
            return;
        }
        switch (key)
        {
            case SwitcherKey.Next or SwitcherKey.Right or SwitcherKey.Down: Move(1); break;
            case SwitcherKey.Previous or SwitcherKey.Left or SwitcherKey.Up: Move(-1); break;
            case SwitcherKey.Commit: Commit(); break;
            case SwitcherKey.Cancel: Close(); break;
            case SwitcherKey.CloseWindow: CloseSelectedWindow(); break;
            case SwitcherKey.Backspace when _query.Length > 0: SetQuery(_query[..^1]); break;
        }
    }

    private void OnTyped(char ch)
    {
        if (!_open || (ch == ' ' && _query.Length == 0)) return;
        SetQuery(_query + ch);
    }

    private void Open(bool backwards)
    {
        _open = true;
        _query = "";
        var options = _settings.Current.Switcher;
        GetCursorPos(out var cursor);
        _monitor = MonitorFromPoint(cursor, 2 /* MONITOR_DEFAULTTONEAREST */);
        _mru = WindowsInZOrder(options.CurrentScreenOnly ? _monitor : IntPtr.Zero);
        _all = SwitcherList.Arrange(_mru, options.GroupByApp).ToList();
        _shown = _all;
        _selected = backwards ? _shown.Count - 1 : SwitcherList.InitialSelection(_shown, _mru);
        _showTimer.Start();
    }

    private void Move(int delta)
    {
        _selected = SwitcherList.Step(_selected, delta, _shown.Count);
        _window?.Select(_selected);
    }

    private void SetQuery(string query)
    {
        _query = query;
        _shown = SwitcherList.Filter(_all, query);
        _selected = _shown.Count > 0 ? 0 : -1;
        Render(rebuild: true);
    }

    private void Render(bool rebuild)
    {
        if (_window is null)
        {
            _window = new SwitcherWindow(_icons, _settings.Current.Switcher, _monitor);
            _window.CardClicked += index =>
            {
                _selected = index;
                Commit();
            };
        }
        if (rebuild) _window.Show(_shown, _facts, _selected, _query);
        else _window.Select(_selected);
    }

    private void Commit()
    {
        if (!_open) return;
        var target = _selected >= 0 && _selected < _shown.Count ? _shown[_selected].Handle : IntPtr.Zero;
        _log.Info($"Alt+Tab: picked {_selected} of {_shown.Count}");
        Close();
        if (target == IntPtr.Zero) return;
        Native.Restore(target);
        if (!BringToFront(target)) _log.Info($"Alt+Tab could not focus 0x{target:X} '{Native.WindowText(target)}'");
    }

    /// Windows only lets the app that owns the focus give it away. Joining the focused window's input queue for
    /// a moment makes Windock count as that app; SwitchToThisWindow (Windows' own Alt+Tab call) is the last resort.
    private static bool BringToFront(IntPtr target)
    {
        if (Native.SetForegroundWindow(target) && Native.GetForegroundWindow() == target) return true;
        var foreground = Native.GetForegroundWindow();
        var theirs = Native.GetWindowThreadProcessId(foreground, out _);
        var mine = GetCurrentThreadId();
        var attached = theirs != 0 && theirs != mine && AttachThreadInput(mine, theirs, true);
        try
        {
            BringWindowToTop(target);
            Native.SetForegroundWindow(target);
        }
        finally
        {
            if (attached) AttachThreadInput(mine, theirs, false);
        }
        if (Native.GetForegroundWindow() == target) return true;
        SwitchToThisWindow(target, true);
        return Native.GetForegroundWindow() == target;
    }

    private void CloseSelectedWindow()
    {
        if (_selected < 0 || _selected >= _shown.Count) return;
        var entry = _shown[_selected];
        Native.Close(entry.Handle);
        _all.Remove(entry);
        _mru.Remove(entry);
        _shown = SwitcherList.Filter(_all, _query);
        _selected = Math.Min(_selected, _shown.Count - 1);
        if (_window is not null) Render(rebuild: true);
    }

    private void Close()
    {
        _showTimer.Stop();
        _open = false;
        _hook?.EndSession();
        var window = _window;
        _window = null;
        if (window is not null) _ui.BeginInvoke(window.Close);  // closing a window is slow-ish; keep the hook quick
    }

    /// Tracked app windows (the same set the dock shows), most recently used first (top of the z-order).
    private List<SwitcherEntry> WindowsInZOrder(IntPtr onlyMonitor)
    {
        var tracked = _windows.Snapshot().ToDictionary(w => w.Handle);
        var list = new List<SwitcherEntry>();
        _facts = new Dictionary<nint, WindowFacts>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!tracked.TryGetValue(hwnd, out var w)) return true;
            if (onlyMonitor != IntPtr.Zero && Native.MonitorFromWindow(hwnd, 2) != onlyMonitor) return true;
            list.Add(new SwitcherEntry(hwnd, w.AppId.Value, w.DisplayName, w.Title));
            _facts[hwnd] = new WindowFacts(w.ExePath, Native.IsIconic(hwnd));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public void Dispose()
    {
        Close();
        _hook?.Dispose();
        _hook = null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
