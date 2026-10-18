using System.Diagnostics;
using System.IO;
using Windock.Core.Abstractions;
using Windock.Core.Apps;
using Windock.Core.Logging;

namespace Windock.Interop;

/// Keeps the list of taskbar-worthy top-level windows current from WinEvent hooks — no polling (ARCHITECTURE §6.1).
internal sealed class WindowTracker : IWindowSource, IDisposable
{
    private readonly ILog _log;
    private readonly Native.WinEventProc _proc;  // field keeps the delegate alive for the hooks
    private readonly List<IntPtr> _hooks = new();
    private readonly Dictionary<IntPtr, WindowInfo> _windows = new();

    public WindowTracker(ILog log)
    {
        _log = log;
        _proc = OnWinEvent;
        Foreground = Native.GetForegroundWindow();

        Native.EnumWindows((h, _) =>
        {
            if (IsAppWindow(h)) _windows[h] = Describe(h);
            return true;
        }, IntPtr.Zero);

        foreach (var (min, max) in new[]
                 {
                     (Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND),
                     (Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND),
                     (Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_HIDE),
                     (Native.EVENT_OBJECT_NAMECHANGE, Native.EVENT_OBJECT_NAMECHANGE),
                     (Native.EVENT_OBJECT_CLOAKED, Native.EVENT_OBJECT_UNCLOAKED),
                 })
        {
            _hooks.Add(Native.SetWinEventHook(min, max, IntPtr.Zero, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT));
        }
    }

    public nint Foreground { get; private set; }

    public event EventHandler<WindowInfo>? WindowAdded;
    public event EventHandler<nint>? WindowRemoved;
    public event EventHandler<WindowInfo>? WindowChanged;
    public event EventHandler<nint>? ForegroundChanged;

    /// Raised for raw events on any window; TaskbarService uses it to keep the taskbar hidden.
    public event Action<uint, IntPtr>? RawEvent;

    public IReadOnlyList<WindowInfo> Snapshot() => _windows.Values.ToList();

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != Native.OBJID_WINDOW || idChild != Native.CHILDID_SELF || hwnd == IntPtr.Zero) return;
        try
        {
            RawEvent?.Invoke(evt, hwnd);
            var tracked = _windows.TryGetValue(hwnd, out var current);
            switch (evt)
            {
                case Native.EVENT_OBJECT_DESTROY:
                case Native.EVENT_OBJECT_HIDE:
                case Native.EVENT_OBJECT_CLOAKED:
                    if (tracked) Remove(hwnd);
                    break;
                case Native.EVENT_SYSTEM_FOREGROUND:
                    AddIfApp(hwnd, tracked);
                    Foreground = hwnd;
                    ForegroundChanged?.Invoke(this, hwnd);
                    break;
                case Native.EVENT_OBJECT_SHOW:
                case Native.EVENT_OBJECT_UNCLOAKED:
                    AddIfApp(hwnd, tracked);
                    break;
                case Native.EVENT_OBJECT_NAMECHANGE:
                case Native.EVENT_SYSTEM_MINIMIZESTART:
                case Native.EVENT_SYSTEM_MINIMIZEEND:
                    if (!tracked) AddIfApp(hwnd, false);
                    else Change(hwnd, current!);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"window event 0x{evt:X} for 0x{hwnd:X} failed", ex);
        }
    }

    private void AddIfApp(IntPtr hwnd, bool tracked)
    {
        if (tracked || !IsAppWindow(hwnd)) return;
        var info = Describe(hwnd);
        // Describe() makes COM calls that can pump messages and re-enter this hook for the same window.
        if (!_windows.TryAdd(hwnd, info)) return;
        WindowAdded?.Invoke(this, info);
    }

    private void Remove(IntPtr hwnd)
    {
        _windows.Remove(hwnd);
        WindowRemoved?.Invoke(this, hwnd);
    }

    private void Change(IntPtr hwnd, WindowInfo current)
    {
        if (!IsAppWindow(hwnd))
        {
            Remove(hwnd);
            return;
        }
        var updated = current with { Title = Native.WindowText(hwnd), IsMinimized = Native.IsIconic(hwnd) };
        if (updated == current) return;
        _windows[hwnd] = updated;
        WindowChanged?.Invoke(this, updated);
    }

    /// Close to the taskbar's own rules: visible, unowned (or WS_EX_APPWINDOW), not a tool window, not cloaked.
    /// Reads the window's facts and applies the shared rules in Core (AppWindowFilter).
    private static bool IsAppWindow(IntPtr h)
    {
        if (!Native.IsWindowVisible(h)) return false;  // cheap early exit for the many hidden windows
        var ex = Native.ExStyle(h);
        Native.GetWindowThreadProcessId(h, out var pid);
        Native.GetWindowRect(h, out var rect);
        var facts = new WindowFacts(
            IsVisible: true,
            IsCloaked: Native.IsCloaked(h),
            IsToolWindow: (ex & Native.WS_EX_TOOLWINDOW) != 0,
            IsAppWindow: (ex & Native.WS_EX_APPWINDOW) != 0,
            IsNoActivate: (ex & Native.WS_EX_NOACTIVATE) != 0,
            HasOwner: Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero,
            HiddenFromTaskbar: Native.GetProp(h, "ITaskList_Deleted") != IntPtr.Zero,
            IsMinimized: Native.IsIconic(h),
            Width: rect.Right - rect.Left,
            Height: rect.Bottom - rect.Top,
            Title: Native.WindowText(h),
            ClassName: Native.ClassName(h),
            ProcessName: ProcessNameOf(pid),
            IsOwnProcess: pid == Environment.ProcessId,
            IsChild: (Native.GetWindowLongPtr(h, Native.GWL_STYLE).ToInt64() & Native.WS_CHILD) != 0);
        return AppWindowFilter.IsAppWindow(facts);
    }

    private static string ProcessNameOf(uint pid) =>
        Native.ProcessImagePath(pid) is { } path ? Path.GetFileNameWithoutExtension(path) : "";

    private static WindowInfo Describe(IntPtr h)
    {
        var pid = ProcessOf(h);
        var exe = Native.ProcessImagePath(pid);
        var title = Native.WindowText(h);
        string? processName = null;
        try { processName = Process.GetProcessById((int)pid).ProcessName; }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }

        // Store/UWP apps are identified by AppUserModelID; classic apps by exe path so shortcuts match them.
        var isPackaged = exe is not null && exe.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
        var aumid = isPackaged ? ShellInterop.WindowAppUserModelId(h) ?? Native.PackagedAppUserModelId(pid) : null;
        var id = AppId.Choose(aumid, exe, processName, h);

        var name = isPackaged && title.Length > 0 ? title : DisplayNameOf(exe) ?? processName ?? title;
        return new WindowInfo(h, id, name, title, exe, Native.IsIconic(h));
    }

    /// UWP windows are hosted by ApplicationFrameHost; the real app is the CoreWindow child's process.
    private static uint ProcessOf(IntPtr h)
    {
        Native.GetWindowThreadProcessId(h, out var pid);
        if (Native.ClassName(h) != "ApplicationFrameWindow") return pid;
        var hostPid = pid;
        Native.EnumChildWindows(h, (child, _) =>
        {
            Native.GetWindowThreadProcessId(child, out var childPid);
            if (childPid == hostPid) return true;
            pid = childPid;
            return false;
        }, IntPtr.Zero);
        return pid;
    }

    private static string? DisplayNameOf(string? exe)
    {
        if (exe is null) return null;
        try
        {
            var description = FileVersionInfo.GetVersionInfo(exe).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch (FileNotFoundException) { }
        return Path.GetFileNameWithoutExtension(exe);
    }

    public void Dispose()
    {
        foreach (var hook in _hooks) Native.UnhookWinEvent(hook);
        _hooks.Clear();
    }
}
