using System.Runtime.InteropServices;

namespace S2.Taskbar;

internal static class Native
{
    public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    public const uint ABE_BOTTOM = 3;
    public const int ABS_AUTOHIDE = 1;
    public const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public override string ToString() => $"({Left},{Top})-({Right},{Bottom})"; }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? name);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint p, ref RECT r, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
    public static string ForegroundClass() { var sb = new System.Text.StringBuilder(256); GetClassName(GetForegroundWindow(), sb, 256); return sb.ToString(); }

    public static APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<APPBARDATA>() };

    public static RECT WorkArea()
    {
        var r = new RECT();
        SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, ref r, 0);
        return r;
    }

    public static IEnumerable<IntPtr> TaskbarWindows()
    {
        var main = FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        var h = IntPtr.Zero;
        while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) yield return h;
    }
}
