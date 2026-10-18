using Windock.Core.Layout;
using Windock.Core.Settings;
using Windock.Interop;

namespace Windock.Dock;

/// Blur behind the plate. A raw Win32 popup (not WPF) because it is moved and resized every frame
/// and DWM blur ignores window regions on a fixed-size window (spike S1).
internal sealed class BackdropWindow : IDisposable
{
    private const string ClassName = "WindockBackdrop";
    private static readonly Native.WndProc Proc = (h, m, w, l) => Native.DefWindowProc(h, m, w, l);
    private static bool _registered;

    private (int X, int Y, int W, int H) _last;
    private bool _visible;
    private bool _enabled = true;

    public BackdropWindow()
    {
        var instance = Native.GetModuleHandle(null);
        if (!_registered)
        {
            var wc = new Native.WNDCLASSEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.WNDCLASSEX>(),
                lpfnWndProc = Proc,
                hInstance = instance,
                hbrBackground = Native.GetStockObject(4 /* BLACK_BRUSH */),
                lpszClassName = ClassName,
            };
            Native.RegisterClassEx(ref wc);
            _registered = true;
        }
        Handle = Native.CreateWindowEx(Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
            ClassName, "", Native.WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
    }

    public IntPtr Handle { get; }

    private double _radius = DockMetrics.PlateRadius;

    /// Corner radius of the plate (DIP); the blur region follows it.
    public void SetRadius(double radius)
    {
        if (_radius == radius) return;
        _radius = radius;
        _last = default;  // force a new region on the next update
    }

    public void SetBlur(BlurMode mode)
    {
        _enabled = mode != BlurMode.Off;
        WindowEffects.SetBlur(Handle, mode);
        if (!_enabled) Hide();
    }

    /// plate is in dock-window DIP; origin is the dock window's top-left on screen in physical pixels.
    public void Update(RectD plate, double dpiScale, int originX, int originY, IntPtr dockHwnd)
    {
        if (!_enabled) return;
        var rect = (X: originX + (int)Math.Round(plate.X * dpiScale), Y: originY + (int)Math.Round(plate.Y * dpiScale),
            W: (int)Math.Round(plate.Width * dpiScale), H: (int)Math.Round(plate.Height * dpiScale));
        if (rect == _last && _visible) return;
        _last = rect;
        Native.SetWindowPos(Handle, dockHwnd, rect.X, rect.Y, rect.W, rect.H, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        WindowEffects.SetRoundRectRegion(Handle, new RectD(0, 0, rect.W, rect.H), _radius * dpiScale);
        _visible = true;
    }

    public void Hide()
    {
        if (!_visible) return;
        Native.ShowWindow(Handle, Native.SW_HIDE);
        _visible = false;
    }

    public void Dispose() => Native.DestroyWindow(Handle);
}
