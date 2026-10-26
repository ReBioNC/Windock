using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Windock.Interop;

/// Notices left presses on another app's title bar (where its minimize button is) and takes a snapshot of that
/// window right away, before a minimize can happen on release. Installed only while minimize animations are on.
/// The low-level mouse hook lives on its own thread and only records the point: the snapshot is taken on the
/// thread pool, so mouse input is never held up. Captured is raised on the thread pool.
internal sealed class TitleBarPressWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x0201, WM_QUIT = 0x0012;
    private const uint GA_ROOT = 2;
    /// Title bars (including custom ones like Chrome's or Discord's) are within this many DIPs of the top.
    private const double TitleBarDip = 48;

    private readonly HookProc _proc;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private uint _threadId;
    private IntPtr _hook;
    private int _busy;

    public event Action<IntPtr, WindowSnapshot>? Captured;

    public TitleBarPressWatcher()
    {
        _proc = OnMouse;
        _thread = new Thread(Run) { IsBackground = true, Name = "Windock title-bar watcher", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        _started.Set();
        if (_hook == IntPtr.Zero) return;
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) DispatchMessage(ref msg);
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam == WM_LBUTTONDOWN && Interlocked.Exchange(ref _busy, 1) == 0)
        {
            var point = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).pt;
            ThreadPool.UnsafeQueueUserWorkItem(_ => Inspect(point), null);
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void Inspect(POINT point)
    {
        try
        {
            var hwnd = GetAncestor(WindowFromPoint(point), GA_ROOT);
            if (hwnd == IntPtr.Zero || !Native.IsWindowVisible(hwnd)) return;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == _ownPid || pid == 0) return;
            if (!Native.GetWindowRect(hwnd, out var rect)) return;
            var titleBar = TitleBarDip * GetDpiForWindow(hwnd) / 96.0;
            if (point.Y < rect.Top || point.Y > rect.Top + titleBar + 8) return;  // + the invisible resize border
            if (WindowSnapshot.Take(hwnd) is { } snapshot) Captured?.Invoke(hwnd, snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
