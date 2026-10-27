using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windock.Core.Animation;
using Windock.Core.Layout;
using Windock.Interop;

namespace Windock.Dock;

/// The click-through, see-through window minimize and restore effects play in. One is kept ready while
/// animations are on (building a window per animation left a visible gap at the start), hidden and empty
/// between animations, and moved over the area each animation passes through.
/// It is not a layered window: those are copied back from the GPU every frame, which made big animations drop
/// frames. Instead DWM composes it with per-pixel alpha (frame extended over the whole client area).
internal sealed class MinimizeOverlay : Window
{
    private readonly EffectCanvas _canvas = new();
    private readonly Stopwatch _clock = new();
    private MinimizeEffect _effect;
    private RectD _window, _icon;  // overlay-local DIPs
    private TimeSpan _duration;
    private const double HoleDip = 40;
    private bool _reverse, _playing, _holed;
    private Action? _done;
    private Action<string>? _report;
    private int _frames, _generation;
    private double _firstFrameMs, _worstGapMs, _lastFrameMs;

    public MinimizeOverlay()
    {
        Title = "Windock animation";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;
        Width = 1;
        Height = 1;
        Content = _canvas;
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.Linear);
        new WindowInteropHelper(this).EnsureHandle();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    /// windowDip / iconDip are screen DIPs. reverse = restore (icon → window); done runs on the last frame.
    /// A newer animation finishes the one still playing at once.
    public void Play(WindowSnapshot snapshot, MinimizeEffect effect, RectD windowDip, RectD iconDip, TimeSpan duration, bool reverse,
        Action? done, Action<string>? report)
    {
        Prepare(snapshot, effect, windowDip, iconDip, duration, reverse, done, report);
        Start();
    }

    /// Shows the first frame and waits for Start. For a minimize, the first frame is the window itself, exactly
    /// where it is: put up while the minimize button is pressed, the window can vanish under it without a blink.
    public void Prepare(WindowSnapshot snapshot, MinimizeEffect effect, RectD windowDip, RectD iconDip, TimeSpan duration, bool reverse,
        Action? done, Action<string>? report)
    {
        if (_playing || IsVisible) Finish(report: false);
        ClearHole();
        _generation++;
        _effect = effect;
        _duration = duration;
        _reverse = reverse;
        _done = done;
        _report = report;
        var margin = Math.Max(24, windowDip.Height * 0.12);
        var left = Math.Min(windowDip.X, iconDip.X) - margin;
        var top = Math.Min(windowDip.Y, iconDip.Y) - margin;
        var right = Math.Max(windowDip.Right, iconDip.Right) + margin;
        var bottom = Math.Max(windowDip.Bottom, iconDip.Bottom) + margin;
        _window = new RectD(windowDip.X - left, windowDip.Y - top, windowDip.Width, windowDip.Height);
        _icon = new RectD(iconDip.X - left, iconDip.Y - top, iconDip.Width, iconDip.Height);
        Left = left;
        Top = top;
        Width = right - left;
        Height = bottom - top;
        _canvas.Start(snapshot.Image, MinimizeEffects.Frame(_effect, _window, _icon, _reverse ? 1 : 0));
        _frames = 0;
        _firstFrameMs = -1;
        _worstGapMs = 0;
        _clock.Restart();
        Show();
        // Above the dock too (it re-raises itself, and showing a hidden window does not bring it forward), so the
        // part of the animation at the icon isn't hidden behind the dock.
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// While the first frame waits for the release on the minimize button, the release must reach the app: apps
    /// that handle their own caption buttons (WinUI ones like WhatsApp) minimize on button-up, and this window is
    /// not click-through (only layered windows are). A hole around the pointer lets it through.
    public void PassClicksAtCursor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!IsVisible || !GetCursorPos(out var cursor) || !Native.GetWindowRect(hwnd, out var rect)) return;
        var r = (int)(HoleDip * GetDpiForWindow(hwnd) / 96.0);
        int x = cursor.X - rect.Left, y = cursor.Y - rect.Top;
        var region = CreateRectRgn(0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var hole = CreateRectRgn(x - r, y - r, x + r, y + r);
        CombineRgn(region, region, hole, RGN_DIFF);
        DeleteObject(hole);
        if (SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);  // on success the region belongs to the window
        _holed = true;
    }

    private void ClearHole()
    {
        if (!_holed) return;
        _holed = false;
        SetWindowRgn(new WindowInteropHelper(this).Handle, IntPtr.Zero, true);
    }

    public void Start()
    {
        if (_playing || !IsVisible) return;
        ClearHole();
        _playing = true;
        _clock.Restart();
        CompositionTarget.Rendering += OnFrame;
    }

    /// A prepared animation that will not play (the minimize did not happen): take it down.
    public void Cancel()
    {
        if (_playing || !IsVisible) return;
        _done = null;
        Hide();
        ClearHole();
        _canvas.Clear();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalMilliseconds;
        if (_firstFrameMs < 0) _firstFrameMs = now;
        else _worstGapMs = Math.Max(_worstGapMs, now - _lastFrameMs);
        _lastFrameMs = now;
        _frames++;
        // Time counts from the first frame on screen, so a slow start never skips the beginning.
        var k = Math.Min(1, (now - _firstFrameMs) / _duration.TotalMilliseconds);
        _canvas.Pieces = MinimizeEffects.Frame(_effect, _window, _icon, _reverse ? 1 - k : k);
        if (k >= 1) Finish(report: true);
    }

    private void Finish(bool report)
    {
        CompositionTarget.Rendering -= OnFrame;
        _playing = false;
        if (report) _report?.Invoke($"{_frames} frames, first after {_firstFrameMs:0} ms, worst gap {_worstGapMs:0} ms, total {_clock.Elapsed.TotalMilliseconds:0} ms");
        var done = _done;
        _done = null;
        done?.Invoke();
        // Leave the last picture up for one more frame over the restored window, then hide and let the snapshot go.
        var generation = _generation;
        Dispatcher.BeginInvoke(() =>
        {
            if (_playing || generation != _generation) return;  // a newer animation took over
            Hide();
            _canvas.Clear();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private const int RGN_DIFF = 4;

    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr dest, IntPtr a, IntPtr b, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    /// Draws the effect's pieces; the snapshot is cut into its strips once per animation, not every frame.
    private sealed class EffectCanvas : FrameworkElement
    {
        private readonly Dictionary<RectD, BitmapSource> _crops = new();
        private static readonly Brush[] Shades = Enumerable.Range(0, 101)
            .Select(i => (Brush)Freeze(new SolidColorBrush(Color.FromArgb((byte)(i * 255 / 100), 0, 0, 0)))).ToArray();
        private BitmapSource? _image;
        private IReadOnlyList<EffectPiece> _pieces = Array.Empty<EffectPiece>();

        public void Start(BitmapSource image, IReadOnlyList<EffectPiece> pieces)
        {
            _crops.Clear();
            _image = image;
            Pieces = pieces;
        }

        public void Clear()
        {
            _crops.Clear();
            _image = null;
            Pieces = Array.Empty<EffectPiece>();
        }

        public IReadOnlyList<EffectPiece> Pieces
        {
            set
            {
                _pieces = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_image is null) return;
            foreach (var p in _pieces)
            {
                if (p.Opacity <= 0 || p.Destination.Width <= 0 || p.Destination.Height <= 0) continue;
                var dest = new Rect(p.Destination.X, p.Destination.Y, p.Destination.Width, p.Destination.Height);
                var faded = p.Opacity < 1;
                if (faded) dc.PushOpacity(p.Opacity);
                dc.DrawImage(Crop(_image, p.Source), dest);
                if (p.Shade > 0) dc.DrawRectangle(Shades[(int)Math.Round(Math.Min(1, p.Shade) * 100)], null, dest);
                if (faded) dc.Pop();
            }
        }

        private BitmapSource Crop(BitmapSource image, RectD source)
        {
            if (source.Y <= 0 && source.Height >= 1) return image;
            if (_crops.TryGetValue(source, out var crop)) return crop;
            int w = image.PixelWidth, h = image.PixelHeight;
            var y = Math.Clamp((int)Math.Floor(source.Y * h), 0, h - 1);
            var bottom = Math.Clamp((int)Math.Ceiling(source.Bottom * h), y + 1, h);
            crop = new CroppedBitmap(image, new Int32Rect(0, y, w, bottom - y));
            crop.Freeze();
            return _crops[source] = crop;
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
