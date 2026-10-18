using System.Runtime.InteropServices;

namespace Windock.Interop;

/// One icon in the Windows notification area (tray).
internal sealed record NotifyAreaIcon(IntPtr Toolbar, int Index, IntPtr Owner, uint Id, IntPtr Icon, string Tooltip);

internal enum TrayClick { Left, Right, Double }

/// Reads the icons in Explorer's notification area (the visible part and the hidden-icons overflow) so Windock
/// can show them in its own popup, and forwards clicks to them through Explorer's own toolbar, so every app
/// gets the click exactly as if the real tray was clicked. Read-only toward Explorer: a scratch buffer in
/// Explorer's memory receives the toolbar's answers and is freed right after.
internal static class TrayIcons
{
    private const int TB_GETBUTTON = 0x0417, TB_BUTTONCOUNT = 0x0418, TB_GETITEMRECT = 0x041D, TB_GETBUTTONTEXTW = 0x044B;
    private const byte TBSTATE_HIDDEN = 0x08;
    private const uint PROCESS_VM_OPERATION = 0x0008, PROCESS_VM_READ = 0x0010, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000, PAGE_READWRITE = 0x04;
    private const int BufferSize = 4096;

    /// Visible tray icons first, then the hidden (overflow) ones; duplicates and hidden buttons skipped.
    public static IReadOnlyList<NotifyAreaIcon> Read()
    {
        var result = new List<NotifyAreaIcon>();
        var seen = new HashSet<(IntPtr, uint)>();
        foreach (var toolbar in Toolbars())
        {
            using var remote = RemoteBuffer.For(toolbar);
            if (remote is null) continue;
            var count = (int)Send(toolbar, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
            for (var i = 0; i < count; i++)
            {
                if (Send(toolbar, TB_GETBUTTON, (IntPtr)i, remote.Address) == IntPtr.Zero) continue;
                var button = remote.Read(32);  // TBBUTTON (x64)
                if (button is null || (button[8] & TBSTATE_HIDDEN) != 0) continue;
                var idCommand = BitConverter.ToInt32(button, 4);
                var dwData = (IntPtr)BitConverter.ToInt64(button, 16);
                var tray = remote.ReadAt(dwData, 32);  // TRAYDATA: hwnd, uID, uCallbackMessage, reserved[2], hIcon
                if (tray is null) continue;
                var owner = (IntPtr)BitConverter.ToInt64(tray, 0);
                var id = BitConverter.ToUInt32(tray, 8);
                var icon = (IntPtr)BitConverter.ToInt64(tray, 24);
                if (owner == IntPtr.Zero || !seen.Add((owner, id))) continue;
                result.Add(new NotifyAreaIcon(toolbar, i, owner, id, icon, ButtonText(toolbar, idCommand, remote)));
            }
        }
        return result;
    }

    /// Posts the mouse messages to Explorer's tray toolbar at the icon's position; Explorer forwards them to the app.
    public static void Click(NotifyAreaIcon icon, TrayClick click)
    {
        var center = ItemCenter(icon);
        if (center is null) return;
        var lp = (IntPtr)((center.Value.Y << 16) | (center.Value.X & 0xFFFF));
        AllowSetForegroundWindow(ASFW_ANY);  // the app may bring up its window or menu
        Native.PostMessage(icon.Toolbar, WM_MOUSEMOVE, IntPtr.Zero, lp);
        switch (click)
        {
            case TrayClick.Left:
                Native.PostMessage(icon.Toolbar, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, lp);
                Native.PostMessage(icon.Toolbar, WM_LBUTTONUP, IntPtr.Zero, lp);
                break;
            case TrayClick.Double:
                Native.PostMessage(icon.Toolbar, WM_LBUTTONDBLCLK, (IntPtr)MK_LBUTTON, lp);
                Native.PostMessage(icon.Toolbar, WM_LBUTTONUP, IntPtr.Zero, lp);
                break;
            case TrayClick.Right:
                Native.PostMessage(icon.Toolbar, WM_RBUTTONDOWN, (IntPtr)MK_RBUTTON, lp);
                Native.PostMessage(icon.Toolbar, WM_RBUTTONUP, IntPtr.Zero, lp);
                break;
        }
    }

    private static (int X, int Y)? ItemCenter(NotifyAreaIcon icon)
    {
        using var remote = RemoteBuffer.For(icon.Toolbar);
        if (remote is null || Send(icon.Toolbar, TB_GETITEMRECT, (IntPtr)icon.Index, remote.Address) == IntPtr.Zero) return null;
        var rect = remote.Read(16);
        if (rect is null) return null;
        int l = BitConverter.ToInt32(rect, 0), t = BitConverter.ToInt32(rect, 4), r = BitConverter.ToInt32(rect, 8), b = BitConverter.ToInt32(rect, 12);
        return ((l + r) / 2, (t + b) / 2);
    }

    private static string ButtonText(IntPtr toolbar, int idCommand, RemoteBuffer remote)
    {
        var length = (int)Send(toolbar, TB_GETBUTTONTEXTW, (IntPtr)idCommand, remote.Address);
        if (length <= 0) return "";
        var bytes = remote.Read(Math.Min(length, BufferSize / 2 - 1) * 2);
        return bytes is null ? "" : System.Text.Encoding.Unicode.GetString(bytes);
    }

    /// The visible tray toolbar and the hidden-icons overflow toolbar (Windows 10).
    private static IEnumerable<IntPtr> Toolbars()
    {
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        var notify = tray == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        var pager = notify == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
        var promoted = pager == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null);
        if (promoted != IntPtr.Zero) yield return promoted;
        var overflow = Native.FindWindow("NotifyIconOverflowWindow", null);
        var overflowBar = overflow == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null);
        if (overflowBar != IntPtr.Zero) yield return overflowBar;
    }

    /// A scratch page in the toolbar owner's (Explorer's) memory for toolbar messages that fill a struct.
    private sealed class RemoteBuffer : IDisposable
    {
        private readonly IntPtr _process;

        private RemoteBuffer(IntPtr process, IntPtr address)
        {
            _process = process;
            Address = address;
        }

        public IntPtr Address { get; }

        public static RemoteBuffer? For(IntPtr window)
        {
            Native.GetWindowThreadProcessId(window, out var pid);
            var process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return null;
            var address = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)BufferSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (address != IntPtr.Zero) return new RemoteBuffer(process, address);
            CloseHandle(process);
            return null;
        }

        public byte[]? Read(int size) => ReadAt(Address, size);

        public byte[]? ReadAt(IntPtr address, int size)
        {
            var bytes = new byte[size];
            return ReadProcessMemory(_process, address, bytes, (UIntPtr)size, out var read) && (int)read == size ? bytes : null;
        }

        public void Dispose()
        {
            VirtualFreeEx(_process, Address, UIntPtr.Zero, MEM_RELEASE);
            CloseHandle(_process);
        }
    }

    /// Never blocks on a hung Explorer.
    private static IntPtr Send(IntPtr window, int msg, IntPtr wParam, IntPtr lParam) =>
        Native.SendMessageTimeout(window, msg, wParam, lParam, 0x2 /* SMTO_ABORTIFHUNG */, 500, out var result) == IntPtr.Zero ? IntPtr.Zero : result;

    private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203,
        WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, MK_LBUTTON = 0x0001, MK_RBUTTON = 0x0002;
    private const uint ASFW_ANY = unchecked((uint)-1);

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
}
