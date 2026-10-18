using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Windock.Interop;

/// Watches mouse presses anywhere on screen while a dock menu is open and reports the ones outside it.
/// The dock never takes focus, so its menus cannot rely on losing focus to close. The low-level mouse hook
/// exists only while the menu is open.
internal sealed class OutsideClickWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;

    private readonly Func<IntPtr> _window;
    private readonly Action _onOutside;
    private readonly Dispatcher _dispatcher;
    private readonly HookProc _proc;  // kept alive while the hook is installed
    private IntPtr _hook;

    public OutsideClickWatcher(Func<IntPtr> window, Action onOutside)
    {
        _window = window;
        _onOutside = onOutside;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = OnMouse;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
        {
            var point = Marshal.PtrToStructure<POINT>(lParam);  // MSLLHOOKSTRUCT starts with pt
            var window = _window();
            if (window == IntPtr.Zero || !GetWindowRect(window, out var r) || point.X < r.Left || point.X >= r.Right || point.Y < r.Top || point.Y >= r.Bottom)
                _dispatcher.BeginInvoke(_onOutside);  // never block the hook
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
