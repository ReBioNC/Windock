using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace S1.BlurOverlay;

/// Blur-only window sized to the maximum plate; its visible shape is a per-frame window region.
internal sealed class BackdropWindow : Window
{
    private readonly int _accent;
    private readonly bool _layered;
    public IntPtr Handle { get; private set; }

    public BackdropWindow(int accent, bool layered)
    {
        _accent = accent;
        _layered = layered;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = layered;
        Background = Brushes.Transparent;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        if (!_layered && HwndSource.FromHwnd(Handle) is { } src)
            src.CompositionTarget.BackgroundColor = Colors.Transparent;
        Native.AddExStyle(Handle, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        var r = Native.SetAccent(Handle, _accent, 0x01000000);
        App.Log($"backdrop accent={_accent} layered={_layered} SetWindowCompositionAttribute={r}");
    }
}
