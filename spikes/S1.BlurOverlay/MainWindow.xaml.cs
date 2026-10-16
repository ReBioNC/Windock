using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace S1.BlurOverlay;

public partial class MainWindow : Window
{
    private const double WindowHeight = 118, PlateHeight = 66, EdgeGap = 4, BackdropWidth = 640;
    private readonly Rectangle[] _icons = new Rectangle[5];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private BackdropWindow? _backdrop;
    private RawBackdrop? _raw;
    private IntPtr BlurHandle => _raw?.Handle ?? _backdrop?.Handle ?? IntPtr.Zero;
    private IntPtr _hwnd;
    private bool _animating;
    private double _lastFrameMs, _maxGapMs, _maxWorkMs;
    private int _frames, _slowFrames;
    private bool _loggedRgn;

    public MainWindow()
    {
        InitializeComponent();
        Width = SystemParameters.PrimaryScreenWidth;
        Height = WindowHeight;
        Left = 0;
        Top = SystemParameters.PrimaryScreenHeight - WindowHeight;
        for (var i = 0; i < _icons.Length; i++)
        {
            _icons[i] = new Rectangle { Fill = new SolidColorBrush(Color.FromRgb((byte)(60 + i * 40), 120, 220)), RadiusX = 10, RadiusY = 10 };
            Root.Children.Add(_icons[i]);
        }
        Plate.MouseLeftButtonUp += (_, _) => { App.Log("plate clicked"); StartAnimation(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        if (App.Raw) _raw = new RawBackdrop(App.Accent);
        else _backdrop = new BackdropWindow(App.Accent, App.Layered)
        {
            Width = BackdropWidth,
            Height = PlateHeight,
            Left = (SystemParameters.PrimaryScreenWidth - BackdropWidth) / 2,
            Top = SystemParameters.PrimaryScreenHeight - EdgeGap - PlateHeight,
        };
        _backdrop?.Show();
        App.Log($"overlay hwnd=0x{_hwnd:X} backdrop hwnd=0x{BlurHandle:X} dpi={VisualTreeHelper.GetDpi(this).DpiScaleX}");
        Layout(500);
        StartAnimation();
        new DispatcherTimer(TimeSpan.FromSeconds(App.ExitAfterSeconds), DispatcherPriority.Normal, (_, _) => Shutdown(), Dispatcher).Start();
    }

    private void Shutdown()
    {
        App.Log("exit");
        _backdrop?.Close();
        _raw?.Dispose();
        Application.Current.Shutdown();
    }

    private void StartAnimation()
    {
        if (_animating) return;
        _animating = true;
        _frames = 0; _slowFrames = 0; _maxGapMs = 0; _maxWorkMs = 0; _lastFrameMs = _clock.Elapsed.TotalMilliseconds;
        var stopAt = _clock.Elapsed + TimeSpan.FromSeconds(App.AnimateSeconds);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            var start = _clock.Elapsed.TotalMilliseconds;
            _maxGapMs = Math.Max(_maxGapMs, start - _lastFrameMs);
            _lastFrameMs = start;
            _frames++;
            var t = _clock.Elapsed.TotalSeconds;
            Layout(400 + 200 * Math.Sin(t * 2));
            var work = _clock.Elapsed.TotalMilliseconds - start;
            _maxWorkMs = Math.Max(_maxWorkMs, work);
            if (work > 8) _slowFrames++;
            if (_clock.Elapsed >= stopAt)
            {
                CompositionTarget.Rendering -= handler;
                _animating = false;
                App.Log($"paused: frames={_frames} fps={_frames / (double)App.AnimateSeconds:F1} maxGapMs={_maxGapMs:F1} maxWorkMs={_maxWorkMs:F2} slowFrames(>8ms)={_slowFrames}");
            }
        };
        CompositionTarget.Rendering += handler;
        App.Log("animating");
    }

    private void Layout(double plateWidth)
    {
        var screenW = Width;
        var plateX = (screenW - plateWidth) / 2;
        var plateY = WindowHeight - EdgeGap - PlateHeight;
        Plate.Width = plateWidth;
        Canvas.SetLeft(Plate, plateX);
        Canvas.SetTop(Plate, plateY);

        var slot = plateWidth / _icons.Length;
        for (var i = 0; i < _icons.Length; i++)
        {
            var size = i == 2 ? 80 : 48;  // middle icon "magnified", pokes above the plate
            _icons[i].Width = size;
            _icons[i].Height = size;
            Canvas.SetLeft(_icons[i], plateX + slot * i + (slot - size) / 2);
            Canvas.SetTop(_icons[i], plateY + PlateHeight - 12 - size);
        }

        if (BlurHandle == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (App.ResizeMode || App.Raw)
        {
            // Move/resize the blur window to exactly the plate rect, then round it with a region.
            var px = (int)Math.Round(plateX * dpi);
            var py = (int)Math.Round((Top + plateY) * dpi);
            var pw = (int)Math.Round(plateWidth * dpi);
            var ph = (int)Math.Round(PlateHeight * dpi);
            Native.SetWindowPos(BlurHandle, _hwnd, px, py, pw, ph, Native.SWP_NOACTIVATE);
            Native.SetWindowRgn(BlurHandle, Native.CreateRoundRectRgn(0, 0, pw + 1, ph + 1, (int)Math.Round(32 * dpi), (int)Math.Round(32 * dpi)), true);
            return;
        }
        var offset = (BackdropWidth - plateWidth) / 2;
        var rgn = Native.CreateRoundRectRgn(
            (int)Math.Round(offset * dpi), 0,
            (int)Math.Round((offset + plateWidth) * dpi) + 1, (int)Math.Round(PlateHeight * dpi) + 1,
            (int)Math.Round(32 * dpi), (int)Math.Round(32 * dpi));
        var ok = Native.SetWindowRgn(_backdrop.Handle, rgn, true);
        if (!_loggedRgn) { _loggedRgn = true; App.Log($"SetWindowRgn={ok} lastErr={System.Runtime.InteropServices.Marshal.GetLastWin32Error()} rgn=0x{rgn:X}"); }
        Native.SetWindowPos(_backdrop.Handle, _hwnd, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}
