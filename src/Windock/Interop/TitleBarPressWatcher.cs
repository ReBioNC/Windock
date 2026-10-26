using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Windock.Interop;

/// Notices left presses on another app's title bar (where its minimize button is) and takes a snapshot of that
/// window right away, before a minimize can happen on release. Installed only while minimize animations are on.
/// The low-level mouse hook lives on its own thread and only records the point: the snapshot is taken on the
/// thread pool, so mouse input is never held up. Captured is raised on the thread pool.
internal sealed class TitleBarPressWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_QUIT = 0x0012, WM_NCHITTEST = 0x0084, HTMINBUTTON = 8;
    private const uint SMTO_ABORTIFHUNG = 0x2;
    private const long WS_MINIMIZEBOX = 0x00020000;
    private const int DWMWA_CAPTION_BUTTON_BOUNDS = 5;
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

    /// The window, its snapshot, and whether the press was on its minimize button (a minimize is likely on release).
    public event Action<IntPtr, WindowSnapshot, bool>? Captured;

    /// The left button went up (raised on the hook thread: hand the work over).
    public event Action? Released;

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
        else if (code >= 0 && (int)wParam == WM_LBUTTONUP)
        {
            Released?.Invoke();
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
            var onMinimize = OnMinimizeButton(hwnd, rect, point);
            if (WindowSnapshot.Take(hwnd) is { } snapshot) Captured?.Invoke(hwnd, snapshot, onMinimize);
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

    /// Windows 10 draws and handles the wide caption buttons itself, and many apps' own hit-test still answers
    /// "title bar" over them, so the button area Windows reports decides: minimize is the first of the three.
    /// The app's own answer covers custom title bars (Chrome and the like).
    private static bool OnMinimizeButton(IntPtr hwnd, Native.RECT window, POINT point)
    {
        if ((Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64() & WS_MINIMIZEBOX) == 0) return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CAPTION_BUTTON_BOUNDS, out var b, Marshal.SizeOf<Native.RECT>()) == 0)
        {
            int width = b.Right - b.Left, height = b.Bottom - b.Top;
            if (width >= height * 2.5 && height > 0)
            {
                int x = point.X - window.Left, y = point.Y - window.Top;
                if (y >= b.Top && y < b.Bottom && x >= b.Left && x < b.Left + width / 3) return true;
            }
        }
        var lParam = (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));
        return SendMessageTimeout(hwnd, WM_NCHITTEST, IntPtr.Zero, lParam, SMTO_ABORTIFHUNG, 50, out var hit) != IntPtr.Zero && (int)hit == HTMINBUTTON;
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
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Native.RECT value, int size);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
