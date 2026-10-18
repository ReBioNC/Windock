using System.Runtime.InteropServices;

namespace Windock.Interop;

/// Shell hook messages for our message window: windows asking for attention (FlashWindow) and activations.
internal sealed class ShellHook : IDisposable
{
    private const int HSHELL_WINDOWACTIVATED = 4, HSHELL_LANGUAGE = 8, HSHELL_RUDEAPPACTIVATED = 0x8004, HSHELL_FLASH = 0x8006;

    private readonly MessageWindow _window;
    private readonly uint _message;
    private readonly bool _registered;

    public ShellHook(MessageWindow window)
    {
        _window = window;
        _message = Native.RegisterWindowMessage("SHELLHOOK");
        _window.Message += OnMessage;
        _registered = _message != 0 && RegisterShellHookWindow(window.Handle);
    }

    /// A top-level window flashed to ask for attention.
    public event Action<IntPtr>? Flashed;

    public event Action<IntPtr>? Activated;

    /// The keyboard language of some window changed.
    public event Action? LanguageChanged;

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_message == 0 || (uint)msg != _message) return;
        switch (wParam.ToInt32())
        {
            case HSHELL_FLASH: Flashed?.Invoke(lParam); break;
            case HSHELL_WINDOWACTIVATED or HSHELL_RUDEAPPACTIVATED: Activated?.Invoke(lParam); break;
            case HSHELL_LANGUAGE: LanguageChanged?.Invoke(); break;
        }
    }

    public void Dispose()
    {
        _window.Message -= OnMessage;
        if (_registered) DeregisterShellHookWindow(_window.Handle);
    }

    /// The top-level owner of a window (dialogs flash on behalf of their app's main window).
    public static IntPtr RootOwner(IntPtr hwnd) => GetAncestor(hwnd, GA_ROOTOWNER);

    private const uint GA_ROOTOWNER = 3;

    [DllImport("user32.dll")] private static extern bool RegisterShellHookWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool DeregisterShellHookWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
}
