using System.Windows.Interop;

namespace Windock.Interop;

public delegate void MessageHandler(int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

/// Invisible top-level window that receives broadcast and callback messages (TaskbarCreated, hotkeys, display changes).
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public MessageWindow(string name)
    {
        _source = new HwndSource(new HwndSourceParameters(name)
        {
            WindowStyle = unchecked((int)Native.WS_POPUP),
            ExtendedWindowStyle = (int)Native.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(Hook);
    }

    public IntPtr Handle => _source.Handle;

    public event MessageHandler? Message;

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        Message?.Invoke(msg, wParam, lParam, ref handled);
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}
