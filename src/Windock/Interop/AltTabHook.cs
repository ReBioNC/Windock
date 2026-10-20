using System.Runtime.InteropServices;

namespace Windock.Interop;

/// What the switcher should do for a key pressed while Alt is held.
internal enum SwitcherKey { Next, Previous, Left, Right, Up, Down, Commit, Cancel, CloseWindow, Backspace }

/// Low-level keyboard hook that takes Alt+Tab away from Windows while Windock's switcher is on.
/// Installed only while that option is on; no administrator rights needed. Keys are only swallowed
/// while Alt+Tab is in use, everything else passes through untouched.
/// The hook lives on its own thread with its own message loop: on the UI thread a busy frame could delay it past
/// Windows' hook timeout, and Windows would then hand that Tab to its own Alt+Tab. Events are raised on that
/// thread; handlers must hand the work to the UI thread.
internal sealed class AltTabHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_ALTDOWN = 0x20;
    private static readonly IntPtr OwnKeyMarker = new(0x57444B);  // "WDK" in dwExtraInfo
    private const int VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_BACK = 0x08, VK_DELETE = 0x2E,
        VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_MENU = 0x12, VK_LMENU = 0xA4, VK_RMENU = 0xA5,
        VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private readonly Func<bool> _shouldTakeOver;
    private readonly HookProc _proc;  // kept alive while installed
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private IntPtr _hook;
    private volatile bool _active;    // Alt+Tab is in progress: our switcher owns the keys until Alt goes up

    /// shouldTakeOver: asked on each Alt+Tab, on the hook thread (false = let Windows show its own).
    public AltTabHook(Func<bool> shouldTakeOver)
    {
        _shouldTakeOver = shouldTakeOver;
        _proc = OnKey;
        _thread = new Thread(Run) { IsBackground = true, Name = "Windock Alt+Tab hook", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(2));
    }

    public bool Installed => _hook != IntPtr.Zero;

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        _started.Set();
        if (_hook == IntPtr.Zero) return;
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// Alt+Tab (shift = backwards) or another key while the switcher is open. Raised inside the hook: handlers must be quick.
    public event Action<SwitcherKey>? Key;

    /// A letter, digit or space typed while the switcher is open (search).
    public event Action<char>? Typed;

    /// Alt went up: the switcher should switch to its selection.
    public event Action? Released;

    /// The switcher closed on its own (mouse click, Esc): stop owning the keys.
    public void EndSession() => _active = false;

    private IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(_hook, code, wParam, lParam);
        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if (data.dwExtraInfo == OwnKeyMarker) return CallNextHookEx(_hook, code, wParam, lParam);  // our own menu-suppressing key
        var msg = (int)wParam;
        var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        var vk = (int)data.vkCode;

        if (_active && !down && vk is VK_MENU or VK_LMENU or VK_RMENU)
        {
            _active = false;
            Released?.Invoke();
            return CallNextHookEx(_hook, code, wParam, lParam);  // Windows must still see Alt go up
        }

        if (down && vk == VK_TAB && (data.flags & LLKHF_ALTDOWN) != 0 && !IsDown(VK_CONTROL) && !IsDown(VK_LWIN) && !IsDown(VK_RWIN))
        {
            if (!_active)
            {
                if (!_shouldTakeOver()) return CallNextHookEx(_hook, code, wParam, lParam);
                _active = true;
                SuppressAltMenu();
            }
            Key?.Invoke(IsDown(VK_SHIFT) ? SwitcherKey.Previous : SwitcherKey.Next);
            return (IntPtr)1;
        }

        if (!_active) return CallNextHookEx(_hook, code, wParam, lParam);

        // While the switcher is open it owns the keyboard (keys up included, so apps never see half a key press).
        if (vk is VK_SHIFT or 0xA0 or 0xA1) return CallNextHookEx(_hook, code, wParam, lParam);
        if (!down) return (IntPtr)1;
        switch (vk)
        {
            case VK_ESCAPE: _active = false; Key?.Invoke(SwitcherKey.Cancel); break;
            case VK_RETURN: _active = false; Key?.Invoke(SwitcherKey.Commit); break;
            case VK_DELETE: Key?.Invoke(SwitcherKey.CloseWindow); break;
            case VK_BACK: Key?.Invoke(SwitcherKey.Backspace); break;
            case VK_LEFT: Key?.Invoke(SwitcherKey.Left); break;
            case VK_RIGHT: Key?.Invoke(SwitcherKey.Right); break;
            case VK_UP: Key?.Invoke(SwitcherKey.Up); break;
            case VK_DOWN: Key?.Invoke(SwitcherKey.Down); break;
            case VK_SPACE: Typed?.Invoke(' '); break;
            case >= 0x41 and <= 0x5A: Typed?.Invoke((char)('a' + vk - 0x41)); break;
            case >= 0x30 and <= 0x39: Typed?.Invoke((char)vk); break;
        }
        return (IntPtr)1;
    }

    /// Alt pressed and released with nothing in between opens the app's menu bar. Since Windows never saw the Tab,
    /// a harmless unassigned key is sent so the Alt release is not taken as a lone Alt tap.
    private static void SuppressAltMenu()
    {
        keybd_event(0xE8, 0, 0, OwnKeyMarker);
        keybd_event(0xE8, 0, 2, OwnKeyMarker);
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_threadId == 0) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);  // the loop unhooks on its own thread
        _thread.Join(TimeSpan.FromSeconds(1));
        _threadId = 0;
    }

    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int ptX, ptY;
    }

    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
