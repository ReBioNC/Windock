using System.Runtime.InteropServices;

namespace S1.BlurOverlay;

/// Plain Win32 popup window (no WPF rendering) used only as a DWM blur surface.
internal sealed class RawBackdrop : IDisposable
{
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize; public uint style; public WndProc lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
        public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground;
        public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(long exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int i);

    private const uint WS_POPUP = 0x80000000;
    private const long WS_EX_TOPMOST = 0x8;
    private static readonly WndProc Proc = (h, m, w, l) => DefWindowProc(h, m, w, l);
    private static bool _registered;

    public IntPtr Handle { get; }

    public RawBackdrop(int accent)
    {
        var inst = GetModuleHandle(null);
        if (!_registered)
        {
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Proc, hInstance = inst,
                hbrBackground = GetStockObject(4 /* BLACK_BRUSH */), lpszClassName = "WindockSpikeBackdrop",
            };
            RegisterClassEx(ref wc);
            _registered = true;
        }
        Handle = CreateWindowEx(WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
            "WindockSpikeBackdrop", "", WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        var r = Native.SetAccent(Handle, accent, 0x01000000);
        ShowWindow(Handle, 4 /* SW_SHOWNOACTIVATE */);
        App.Log($"raw backdrop hwnd=0x{Handle:X} accent={accent} result={r}");
    }

    public void Dispose() => DestroyWindow(Handle);
}
