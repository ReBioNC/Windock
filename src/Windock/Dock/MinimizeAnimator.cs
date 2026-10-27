using System.Windows;
using System.Windows.Threading;
using Windock.Core.Animation;
using Windock.Core.Layout;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Interop;

namespace Windock.Dock;

/// Plays the chosen minimize effect into the window's dock icon, and the reverse when the dock restores it.
/// Snapshots come from a press on the window's title bar (TitleBarPressWatcher) or from Windock itself just
/// before a dock click minimizes; a window minimized any other way has no snapshot and simply minimizes.
/// A minimized window keeps a small snapshot for its restore; it is dropped when the window comes back or closes.
internal sealed class MinimizeAnimator : IDisposable
{
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(3), LateSnapshot = TimeSpan.FromMilliseconds(400);
    private const int MaxSnapshots = 10;

    private readonly SettingsStore _settings;
    private readonly DockController _dock;
    private readonly ILog _log;
    private readonly Dispatcher _ui;
    private readonly Func<bool> _active;
    private readonly Dictionary<IntPtr, WindowSnapshot> _snapshots = new();
    private readonly Dictionary<IntPtr, DateTime> _waiting = new();  // minimized before its snapshot was ready
    private readonly HashSet<IntPtr> _minimized = new();
    private TitleBarPressWatcher? _watcher;
    private MinimizeOverlay? _overlay;
    private IntPtr _armed;  // window whose first frame is already up, waiting for its minimize

    /// active: false in Windows mode (no animations, Windows' own animation back).
    public MinimizeAnimator(SettingsStore settings, DockController dock, WindowTracker tracker, ILog log, Dispatcher ui, Func<bool> active)
    {
        _settings = settings;
        _dock = dock;
        _log = log;
        _ui = ui;
        _active = active;
        tracker.RawEvent += OnWindowEvent;
        Apply();
    }

    private AnimationSettings Options => _settings.Current.Animations;
    private bool Enabled => _active() && Options.Effect != MinimizeEffect.Off;

    /// Settings or mode changed: start or stop watching, and switch Windows' own animation accordingly.
    public void Apply()
    {
        if (Enabled && _watcher is null)
        {
            _watcher = new TitleBarPressWatcher();
            _watcher.Captured += (hwnd, snapshot, onMinimize) => _ui.BeginInvoke(() =>
            {
                Store(hwnd, snapshot);
                if (onMinimize) Arm(hwnd);
            });
            _watcher.Released += () => _ui.BeginInvoke(DisarmSoon);
            _overlay = new MinimizeOverlay();
        }
        else if (!Enabled && _watcher is not null)
        {
            _watcher.Dispose();
            _watcher = null;
            _overlay?.Close();
            _overlay = null;
            _snapshots.Clear();
            _minimized.Clear();
        }
        try
        {
            WindowsMinimizeAnimation.Suppress(Enabled && Options.DisableWindowsAnimation);
        }
        catch (Exception ex)
        {
            _log.Error("changing Windows' minimize animation failed", ex);
        }
    }

    /// Before Windock itself minimizes a window (dock click).
    public void CaptureBeforeMinimize(IntPtr hwnd)
    {
        if (!Enabled || WindowSnapshot.Take(hwnd) is not { } snapshot) return;
        Store(hwnd, snapshot);
        Arm(hwnd);
        DisarmSoon();
    }

    /// The minimize button is pressed: put the first frame (the window itself) up now, so the window vanishing
    /// underneath on release can't blink the desktop through before the animation shows.
    private void Arm(IntPtr hwnd)
    {
        if (!Enabled || _overlay is null || _minimized.Contains(hwnd) || !_snapshots.TryGetValue(hwnd, out var snapshot)) return;
        if (Target(hwnd) is not { } icon) return;
        if (!Prepare(snapshot, icon, reverse: false, done: null)) return;
        _overlay.PassClicksAtCursor();
        _armed = hwnd;
    }

    /// Released without a minimize (pressed, then moved off the button): take the first frame down again.
    private void DisarmSoon()
    {
        if (_armed == IntPtr.Zero) return;
        var armed = _armed;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _ui) { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_armed != armed || Native.IsIconic(armed)) return;
            _armed = IntPtr.Zero;
            _overlay?.Cancel();
        };
        timer.Start();
    }

    /// Plays the restore effect, then calls restore. False when there is nothing to play (restore not called).
    public bool TryRestore(IntPtr hwnd, Action restore)
    {
        if (!Enabled || !Options.AnimateRestore || !Native.IsIconic(hwnd) || !_snapshots.TryGetValue(hwnd, out var snapshot)) return false;
        if (Target(hwnd) is not { } icon) return false;
        _snapshots.Remove(hwnd);
        _minimized.Remove(hwnd);
        Play(snapshot, icon, reverse: true, restore);
        return true;
    }

    private void Store(IntPtr hwnd, WindowSnapshot snapshot)
    {
        if (!Enabled) return;
        _snapshots[hwnd] = snapshot;
        while (_snapshots.Count > MaxSnapshots)
            _snapshots.Remove(_snapshots.Where(p => !_minimized.Contains(p.Key)).DefaultIfEmpty(_snapshots.First()).MinBy(p => p.Value.TakenUtc).Key);
        if (_waiting.Remove(hwnd, out var at) && DateTime.UtcNow - at < LateSnapshot && Native.IsIconic(hwnd)) Minimized(hwnd);
    }

    private void OnWindowEvent(uint evt, IntPtr hwnd)
    {
        if (!Enabled) return;
        switch (evt)
        {
            case Native.EVENT_SYSTEM_MINIMIZESTART:
                if (_snapshots.TryGetValue(hwnd, out var s) && DateTime.UtcNow - s.TakenUtc < Fresh) Minimized(hwnd);
                else _waiting[hwnd] = DateTime.UtcNow;
                break;
            case Native.EVENT_OBJECT_HIDE when hwnd == _armed:
                _armed = IntPtr.Zero;
                _overlay?.Cancel();
                break;
            case Native.EVENT_SYSTEM_MINIMIZEEND:
            case Native.EVENT_OBJECT_DESTROY:
                if (hwnd == _armed)
                {
                    _armed = IntPtr.Zero;
                    _overlay?.Cancel();
                }
                _minimized.Remove(hwnd);
                _snapshots.Remove(hwnd);
                _waiting.Remove(hwnd);
                break;
        }
    }

    private void Minimized(IntPtr hwnd)
    {
        if (!_snapshots.TryGetValue(hwnd, out var snapshot) || _minimized.Contains(hwnd)) return;
        if (Target(hwnd) is not { } icon)
        {
            _snapshots.Remove(hwnd);
            return;
        }
        if (Options.AnimateRestore) _minimized.Add(hwnd);
        else _snapshots.Remove(hwnd);
        if (_armed == hwnd && _overlay is not null)
        {
            _armed = IntPtr.Zero;
            _overlay.Start();
            return;
        }
        _armed = IntPtr.Zero;
        Play(snapshot, icon, reverse: false, done: null);
    }

    /// The app's dock icon, or the middle of the bottom edge when the dock isn't showing it (DIPs).
    private RectD? Target(IntPtr hwnd)
    {
        if (_dock.IconScreenBounds(hwnd) is { } icon) return icon;
        var size = 48.0;
        return new RectD(SystemParameters.PrimaryScreenWidth / 2 - size / 2, SystemParameters.PrimaryScreenHeight - size / 3, size, size);
    }

    private void Play(WindowSnapshot snapshot, RectD icon, bool reverse, Action? done)
    {
        if (Prepare(snapshot, icon, reverse, done)) _overlay!.Start();
    }

    private bool Prepare(WindowSnapshot snapshot, RectD icon, bool reverse, Action? done)
    {
        try
        {
            var dpi = _dock.DpiScale;
            var b = snapshot.Bounds;
            var window = new RectD(b.X / dpi, b.Y / dpi, b.Width / dpi, b.Height / dpi);
            _log.Info($"{(reverse ? "restore" : "minimize")} animation: {Options.Effect}, {window.Width:0}x{window.Height:0} -> icon at {icon.X:0},{icon.Y:0}, snapshot took {snapshot.TookMs:0} ms, {(DateTime.UtcNow - snapshot.TakenUtc).TotalMilliseconds:0} ms ago");
            if (_overlay is null)
            {
                done?.Invoke();
                return false;
            }
            _overlay.Prepare(snapshot, Options.Effect, window, icon, TimeSpan.FromMilliseconds(Options.DurationMs), reverse, done,
                stats => _log.Info($"animation timing: {stats}"));
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("minimize animation failed", ex);
            done?.Invoke();
            return false;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _overlay?.Close();
        _overlay = null;
        _snapshots.Clear();
        try { WindowsMinimizeAnimation.Restore(); } catch (Exception ex) { _log.Error("restoring Windows' minimize animation failed", ex); }
    }
}
