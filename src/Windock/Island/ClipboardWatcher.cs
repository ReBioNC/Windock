using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windock.Core.Logging;
using Windock.Interop;

namespace Windock.Island;

/// Text copied anywhere, for the island's Clipboard panel (WM_CLIPBOARDUPDATE; nothing is polled). Content that
/// asks not to be recorded is skipped: password managers mark it with ExcludeClipboardContentFromMonitorProcessing
/// or CanIncludeInClipboardHistory = 0, the same signals Windows' own clipboard history obeys.
internal sealed class ClipboardWatcher : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    private readonly MessageWindow _messages;
    private readonly Dispatcher _ui;
    private readonly ILog _log;
    private readonly uint _exclude = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private readonly uint _viewerIgnore = RegisterClipboardFormat("Clipboard Viewer Ignore");
    private readonly uint _history = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private readonly bool _listening;

    public ClipboardWatcher(MessageWindow messages, Dispatcher ui, ILog log)
    {
        _messages = messages;
        _ui = ui;
        _log = log;
        _listening = AddClipboardFormatListener(messages.Handle);
        if (!_listening) log.Info("clipboard notifications unavailable");
        messages.Message += OnMessage;
    }

    /// Raised on the UI thread with the copied text.
    public event Action<string>? TextCopied;

    /// Puts text back on the clipboard (the clipboard can be briefly locked by another app: tried a few times).
    public static bool Copy(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }

    private void OnMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_CLIPBOARDUPDATE) return;
        if (IsClipboardFormatAvailable(_exclude) || IsClipboardFormatAvailable(_viewerIgnore) || HistoryForbidden()) return;
        _ui.BeginInvoke(DispatcherPriority.Background, Read);
    }

    /// CanIncludeInClipboardHistory present with the value 0.
    private bool HistoryForbidden()
    {
        if (!IsClipboardFormatAvailable(_history) || !OpenClipboard(_messages.Handle)) return false;
        try
        {
            var data = GetClipboardData(_history);
            if (data == IntPtr.Zero) return false;
            var p = GlobalLock(data);
            if (p == IntPtr.Zero) return false;
            try { return Marshal.ReadInt32(p) == 0; }
            finally { GlobalUnlock(data); }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private void Read()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsText()) return;
                var text = System.Windows.Clipboard.GetText();
                TextCopied?.Invoke(text);
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }
        _log.Info("clipboard: could not read the copied text");
    }

    public void Dispose()
    {
        _messages.Message -= OnMessage;
        if (_listening) RemoveClipboardFormatListener(_messages.Handle);
    }

    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
}
