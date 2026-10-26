using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windock.Core.Animation;
using Windock.Core.Layout;
using Windock.Interop;

namespace Windock.Dock;

/// A click-through, see-through window that plays one minimize or restore effect with a window snapshot and
/// closes itself. It only covers the area the animation passes through, and exists only while it plays.
internal sealed class MinimizeOverlay : Window
{
    private readonly EffectCanvas _canvas;
    private readonly MinimizeEffect _effect;
    private readonly RectD _window, _icon;  // overlay-local DIPs
    private readonly TimeSpan _duration;
    private readonly bool _reverse;
    private readonly Action? _done;
    private readonly Stopwatch _clock = new();

    /// windowDip / iconDip are screen DIPs. reverse = restore (icon → window).
    public MinimizeOverlay(WindowSnapshot snapshot, MinimizeEffect effect, RectD windowDip, RectD iconDip, TimeSpan duration, bool reverse, Action? done)
    {
        _effect = effect;
        _duration = duration;
        _reverse = reverse;
        _done = done;
        var margin = Math.Max(24, windowDip.Height * 0.12);
        var left = Math.Min(windowDip.X, iconDip.X) - margin;
        var top = Math.Min(windowDip.Y, iconDip.Y) - margin;
        var right = Math.Max(windowDip.Right, iconDip.Right) + margin;
        var bottom = Math.Max(windowDip.Bottom, iconDip.Bottom) + margin;
        _window = new RectD(windowDip.X - left, windowDip.Y - top, windowDip.Width, windowDip.Height);
        _icon = new RectD(iconDip.X - left, iconDip.Y - top, iconDip.Width, iconDip.Height);

        Title = "Windock animation";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
        Width = right - left;
        Height = bottom - top;
        _canvas = new EffectCanvas(snapshot.Image);
        Content = _canvas;
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.Linear);
        _canvas.Pieces = MinimizeEffects.Frame(_effect, _window, _icon, _reverse ? 1 : 0);
    }

    public void Play()
    {
        Show();
        _clock.Start();
        CompositionTarget.Rendering += OnFrame;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var k = Math.Min(1, _clock.Elapsed.TotalMilliseconds / _duration.TotalMilliseconds);
        _canvas.Pieces = MinimizeEffects.Frame(_effect, _window, _icon, _reverse ? 1 - k : k);
        if (k < 1) return;
        CompositionTarget.Rendering -= OnFrame;
        _done?.Invoke();
        // One more frame with the restored window under the last picture, then go.
        Dispatcher.BeginInvoke(Close, System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= OnFrame;
        base.OnClosed(e);
    }

    /// Draws the effect's pieces; the snapshot is cut into its strips once, not every frame.
    private sealed class EffectCanvas(BitmapSource image) : FrameworkElement
    {
        private readonly Dictionary<RectD, BitmapSource> _crops = new();
        private IReadOnlyList<EffectPiece> _pieces = Array.Empty<EffectPiece>();

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
            foreach (var p in _pieces)
            {
                if (p.Opacity <= 0 || p.Destination.Width <= 0 || p.Destination.Height <= 0) continue;
                var dest = new Rect(p.Destination.X, p.Destination.Y, p.Destination.Width, p.Destination.Height);
                dc.PushOpacity(p.Opacity);
                dc.DrawImage(Crop(p.Source), dest);
                if (p.Shade > 0) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(Math.Min(1, p.Shade) * 255), 0, 0, 0)), null, dest);
                dc.Pop();
            }
        }

        private BitmapSource Crop(RectD source)
        {
            if (source.X == 0 && source.Y == 0 && source.Width >= 1 && source.Height >= 1) return image;
            if (_crops.TryGetValue(source, out var crop)) return crop;
            int w = image.PixelWidth, h = image.PixelHeight;
            var y = Math.Clamp((int)Math.Floor(source.Y * h), 0, h - 1);
            var bottom = Math.Clamp((int)Math.Ceiling(source.Bottom * h), y + 1, h);
            crop = new CroppedBitmap(image, new Int32Rect(0, y, w, bottom - y));
            crop.Freeze();
            return _crops[source] = crop;
        }
    }
}
