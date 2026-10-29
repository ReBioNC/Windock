using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windock.Core.Logging;
using Windock.Interop;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;

namespace Windock.Island;

/// A copied picture as the Clipboard panel keeps it: PNG-compressed (a raw full-screen bitmap is 8 MB), plus a
/// tiny thumbnail for the list.
internal sealed record ClipPicture(byte[] Png, BitmapSource Thumbnail, string Key, int Width, int Height);

/// Text and pictures copied anywhere, for the island's Clipboard panel (WM_CLIPBOARDUPDATE; nothing is polled). Content that
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
    private readonly DispatcherTimer _readSoon;

    public ClipboardWatcher(MessageWindow messages, Dispatcher ui, ILog log)
    {
        _messages = messages;
        _ui = ui;
        _log = log;
        _readSoon = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(60) };
        _readSoon.Tick += (_, _) =>
        {
            _readSoon.Stop();
            Read();
        };
        _listening = AddClipboardFormatListener(messages.Handle);
        if (!_listening) log.Info("clipboard notifications unavailable");
        messages.Message += OnMessage;
    }

    private const int ThumbnailHeight = 40;
    private const long MaxPixels = 40_000_000;

    /// Raised on the UI thread with the copied text.
    public event Action<string>? TextCopied;

    /// Raised on the UI thread with a copied picture (only while IncludeImages is on).
    public event Action<ClipPicture>? ImageCopied;

    public bool IncludeImages { get; set; }

    /// Puts text back on the clipboard (the clipboard can be briefly locked by another app: tried a few times).
    public static bool Copy(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }

    /// Puts a picture back on the clipboard: as PNG (keeps transparency, what browsers and editors prefer) and as a
    /// plain bitmap for everything else.
    public static bool Copy(ClipPicture picture)
    {
        BitmapSource image;
        try
        {
            image = BitmapFrame.Create(new MemoryStream(picture.Png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        catch (Exception)
        {
            return false;
        }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var data = new DataObject();
                data.SetImage(image);
                data.SetData("PNG", new MemoryStream(picture.Png), false);
                Clipboard.SetDataObject(data, true);
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
        // Apps often announce one copy twice (set, then flush): read once it settles.
        _readSoon.Stop();
        _readSoon.Start();
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
                // Text first: apps such as Excel and Word put a picture of the selection next to the text.
                if (Clipboard.ContainsText())
                {
                    TextCopied?.Invoke(Clipboard.GetText());
                    return;
                }
                if (IncludeImages) ReadImage();
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
        }
        _log.Info("clipboard: could not read what was copied");
    }

    /// The PNG an app put on the clipboard is kept as it is; a plain bitmap is compressed to PNG off the UI thread.
    private void ReadImage()
    {
        byte[]? png = null;
        BitmapSource? bitmap = null;
        if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream stream) png = stream.ToArray();
        else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
        {
            if ((long)image.PixelWidth * image.PixelHeight > MaxPixels) return;
            bitmap = image.IsFrozen ? image : (BitmapSource)image.GetCurrentValueAsFrozen();
        }
        if (png is null && bitmap is null) return;
        Task.Run(() =>
        {
            try
            {
                if (png is null)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap!));
                    using var output = new MemoryStream();
                    encoder.Save(output);
                    png = output.ToArray();
                }
                var size = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if ((long)size.PixelWidth * size.PixelHeight > MaxPixels) return;
                var thumbnail = new BitmapImage();
                thumbnail.BeginInit();
                thumbnail.StreamSource = new MemoryStream(png);
                thumbnail.DecodePixelHeight = Math.Min(ThumbnailHeight, size.PixelHeight);
                thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                thumbnail.EndInit();
                thumbnail.Freeze();
                var key = Convert.ToHexString(SHA1.HashData(png));
                var picture = new ClipPicture(png, thumbnail, key, size.PixelWidth, size.PixelHeight);
                _log.Info($"clipboard: kept a {picture.Width}x{picture.Height} image ({png.Length / 1024} KB)");
                _ui.BeginInvoke(() => { if (IncludeImages) ImageCopied?.Invoke(picture); });
            }
            catch (Exception ex)
            {
                _log.Error("clipboard: could not keep the copied image", ex);
            }
        });
    }

    public void Dispose()
    {
        _messages.Message -= OnMessage;
        _readSoon.Stop();
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
