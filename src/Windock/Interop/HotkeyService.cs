using Windock.Core.Input;

namespace Windock.Interop;

/// One global hotkey (the emergency "turn Windock off" gesture).
internal sealed class HotkeyService : IDisposable
{
    private const int Id = 1;
    private readonly MessageWindow _window;
    private bool _registered;

    public HotkeyService(MessageWindow window)
    {
        _window = window;
        _window.Message += OnMessage;
    }

    public event Action? Pressed;

    public bool TryRegister(HotkeyGesture gesture)
    {
        Unregister();
        _registered = Native.RegisterHotKey(_window.Handle, Id, (uint)gesture.Modifiers | Native.MOD_NOREPEAT, (uint)gesture.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        Native.UnregisterHotKey(_window.Handle, Id);
        _registered = false;
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_HOTKEY || wParam.ToInt32() != Id) return;
        handled = true;
        Pressed?.Invoke();
    }

    public void Dispose()
    {
        Unregister();
        _window.Message -= OnMessage;
    }
}
