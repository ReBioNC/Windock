using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windock.Core.Island;

namespace Windock.Island;

/// Wado, the island's little whale (our own design), drawn with vector shapes on a 120 x 120 canvas and scaled to
/// Size. Idle he only blinks now and then (a 150 ms animation, nothing moves in between); with music he sways at
/// 20 fps like the music bars. Happy, focus and sleep change his eyes.
internal sealed class WadoMascot : Viewbox
{
    private static readonly Brush BodyBrush = Frozen(0x85, 0xB7, 0xEB);
    private static readonly Brush BellyBrush = Frozen(0xE6, 0xF1, 0xFB);
    private static readonly Brush InkBrush = Frozen(0x04, 0x2C, 0x53);
    private static readonly Brush CheekBrush = Frozen(0xF4, 0xC0, 0xD1);
    private static readonly Brush DropBrush = Frozen(0xB5, 0xD4, 0xF4);
    private static readonly Random Jitter = new();

    private readonly Canvas _canvas = new() { Width = 120, Height = 120 };
    private readonly Canvas _body = new() { Width = 120, Height = 120 };
    private readonly ScaleTransform _blink = new(1, 1, 58, 64);
    private readonly TranslateTransform _bob = new();
    private readonly RotateTransform _sway = new(0, 58, 96);
    private readonly DispatcherTimer _blinkTimer = new(DispatcherPriority.Background);
    private readonly WadoMood _mood;

    public WadoMascot(WadoMood mood, double size)
    {
        _mood = mood;
        Width = Height = size;
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        Child = _canvas;
        _canvas.Children.Add(_body);
        _body.RenderTransform = new TransformGroup { Children = { _sway, _bob } };
        Draw();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Start();
            else Stop();
        };
    }

    private void Draw()
    {
        if (_mood is WadoMood.Happy or WadoMood.Music)
        {
            Add(new Ellipse { Width = 6, Height = 6, Fill = DropBrush }, 49, 27);
            Add(new Ellipse { Width = 7, Height = 7, Fill = DropBrush }, 56.5, 20.5);
            Add(new Ellipse { Width = 6, Height = 6, Fill = DropBrush }, 65, 27);
        }
        Add(Shape("M96 66 q14 -14 18 -4 q-6 4 -8 12 q-6 -2 -10 -8z", BodyBrush));
        Add(new Ellipse { Width = 80, Height = 60, Fill = BodyBrush }, 18, 40);
        Add(new Ellipse { Width = 56, Height = 30, Fill = BellyBrush }, 26, 67);
        Add(new Ellipse { Width = 10, Height = 6, Fill = CheekBrush }, 33, 71);
        Add(new Ellipse { Width = 10, Height = 6, Fill = CheekBrush }, 73, 71);

        switch (_mood)
        {
            case WadoMood.Happy:
                Add(Stroke("M41.5 66 q4.5 -7 9 0 M65.5 66 q4.5 -7 9 0"));
                Add(Stroke("M51 73 q7 8 14 0"));
                break;
            case WadoMood.Sleep:
                Add(Stroke("M41.5 64 q4.5 4 9 0 M65.5 64 q4.5 4 9 0"));
                Add(Stroke("M55 76 q3 2 6 0"));
                var z = new TextBlock { Text = "z", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = DropBrush };
                Add(z, 88, 22);
                break;
            default:
                var eyes = new Canvas { RenderTransform = _blink };
                AddEye(eyes, 46);
                AddEye(eyes, 70);
                _body.Children.Add(eyes);
                if (_mood == WadoMood.Focus) Add(Stroke("M40 55 l10 3 M76 55 l-10 3"));  // determined brows
                Add(Stroke(_mood == WadoMood.Focus ? "M54 75 h8" : "M53 74 q5 5 10 0"));
                break;
        }
        if (_mood == WadoMood.Music)
            Add(new TextBlock { Text = "♪", FontSize = 18, Foreground = DropBrush }, 94, 26);
    }

    private static void AddEye(Canvas eyes, double cx)
    {
        var eye = new Ellipse { Width = 9, Height = 11, Fill = InkBrush };
        Canvas.SetLeft(eye, cx - 4.5);
        Canvas.SetTop(eye, 58.5);
        eyes.Children.Add(eye);
        var shine = new Ellipse { Width = 3.2, Height = 3.2, Fill = Brushes.White };
        Canvas.SetLeft(shine, cx - 0.1);
        Canvas.SetTop(shine, 60.4);
        eyes.Children.Add(shine);
    }

    private void Start()
    {
        if (!IsVisible) return;
        if (_mood is WadoMood.Idle or WadoMood.Focus or WadoMood.Music)
        {
            _blinkTimer.Interval = NextBlink();
            _blinkTimer.Tick -= OnBlink;
            _blinkTimer.Tick += OnBlink;
            _blinkTimer.Start();
        }
        if (_mood == WadoMood.Music)
        {
            _bob.BeginAnimation(TranslateTransform.YProperty, Loop(0, -6, 0.32));
            _sway.BeginAnimation(RotateTransform.AngleProperty, Loop(-5, 5, 0.32));
        }
    }

    private void Stop()
    {
        _blinkTimer.Stop();
        _bob.BeginAnimation(TranslateTransform.YProperty, null);
        _sway.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private void OnBlink(object? sender, EventArgs e)
    {
        _blinkTimer.Interval = NextBlink();
        _blink.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(75)) { AutoReverse = true });
    }

    private static TimeSpan NextBlink() => TimeSpan.FromMilliseconds(3000 + Jitter.Next(3000));

    private static DoubleAnimation Loop(double from, double to, double seconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(animation, 20);
        return animation;
    }

    private void Add(UIElement element, double left = 0, double top = 0)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        _body.Children.Add(element);
    }

    private static Path Shape(string data, Brush fill) => new() { Data = Geometry.Parse(data), Fill = fill };

    private static Path Stroke(string data) => new()
    {
        Data = Geometry.Parse(data), Stroke = InkBrush, StrokeThickness = 2.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
