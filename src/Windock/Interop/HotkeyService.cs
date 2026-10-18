using Windock.Core.Input;

namespace Windock.Interop;

/// Global hotkeys keyed by id (emergency exit, launcher).
internal sealed class HotkeyService : IDisposable
{
    public const int EmergencyId = 1;
    public const int LauncherId = 2;

    private readonly MessageWindow _window;
    private readonly HashSet<int> _registered = new();

    public HotkeyService(MessageWindow window)
    {
        _window = window;
        _window.Message += OnMessage;
    }

    /// Raised with the id of the hotkey that was pressed.
    public event Action<int>? Pressed;

    public bool TryRegister(int id, HotkeyGesture gesture)
    {
        Unregister(id);
        if (!Native.RegisterHotKey(_window.Handle, id, (uint)gesture.Modifiers | Native.MOD_NOREPEAT, (uint)gesture.VirtualKey)) return false;
        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (!_registered.Remove(id)) return;
        Native.UnregisterHotKey(_window.Handle, id);
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_HOTKEY || !_registered.Contains(wParam.ToInt32())) return;
        handled = true;
        Pressed?.Invoke(wParam.ToInt32());
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.ToList()) Unregister(id);
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.Message -= OnMessage;
    }
}
