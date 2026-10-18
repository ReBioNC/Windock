using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Windock.Core.Layout;
using Windock.Core.Settings;
using Windock.Status;

namespace Windock.Dock;

/// Applies a LayoutResult to WPF elements on the dock canvas. Holds no layout logic of its own.
internal sealed class DockRenderer
{
    private readonly Canvas _canvas;
    private readonly Rectangle _activeZone = new() { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    private readonly Border _plate = new() { CornerRadius = new CornerRadius(DockMetrics.PlateRadius), BorderThickness = new Thickness(1) };
    private SolidColorBrush _dotBrush = new(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));
    private string? _dotColor;
    private readonly Dictionary<string, Visuals> _visuals = new();
    private readonly Border _tooltip;
    private readonly TextBlock _tooltipText;

    public DockRenderer(Canvas canvas)
    {
        _canvas = canvas;
        _plate.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        _tooltipText = new TextBlock { Foreground = Brushes.White, FontSize = SystemFonts.MessageFontSize };
        _tooltip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x2A, 0x2A, 0x2A)), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 4), Child = _tooltipText, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        canvas.Children.Add(_activeZone);
        canvas.Children.Add(_plate);
        canvas.Children.Add(_tooltip);
        Panel.SetZIndex(_tooltip, 10);
    }

    /// theme.PlateColor is the color actually shown (already resolved for "ikuti warna wallpaper").
    public void Render(LayoutResult layout, AppearanceSettings appearance, ThemeSettings theme, Func<string, ImageSource?> iconFor,
        Func<string, int> windowCount, string? pressedKey, Func<string, double> liftFor, Func<string, bool> badgeFor,
        Func<string, StatusButton?> statusFor, string? hoverKey)
    {
        Place(_activeZone, layout.ActiveZone);
        Place(_plate, layout.Plate);
        var plate = ThemeColor.TryParse(theme.PlateColor, out var pc) ? pc : new ThemeColor(0x1E, 0x1E, 0x1E);
        _plate.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(appearance.BackgroundOpacity * 255), plate.R, plate.G, plate.B));
        _plate.CornerRadius = new CornerRadius(theme.CornerRadius);
        _plate.BorderThickness = new Thickness(theme.ShowBorder ? 1 : 0);
        if (_dotColor != theme.IndicatorColor)
        {
            _dotColor = theme.IndicatorColor;
            var dc = ThemeColor.TryParse(theme.IndicatorColor, out var parsed) ? parsed : new ThemeColor(0xFF, 0xFF, 0xFF);
            _dotBrush = new SolidColorBrush(Color.FromArgb(0xD9, dc.R, dc.G, dc.B));
            _dotBrush.Freeze();
        }

        var seen = new HashSet<string>();
        foreach (var item in layout.Items)
        {
            seen.Add(item.Key);
            if (!_visuals.TryGetValue(item.Key, out var v)) _visuals[item.Key] = v = Create(item.Kind);
            Place(v.Element, item.Bounds);
            if (liftFor(item.Key) is var lift and > 0) Canvas.SetTop(v.Element, item.Bounds.Y - lift);
            if (v.Badge is { } badge)
            {
                var show = item.SlotProgress >= 1 && badgeFor(item.Key);
                badge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show)
                {
                    var size = Math.Round(item.Bounds.Width * 0.28);
                    badge.Width = badge.Height = size;
                    Canvas.SetLeft(badge, item.Bounds.X + item.Bounds.Width - size * 0.8);
                    Canvas.SetTop(badge, item.Bounds.Y - size * 0.2);
                }
            }

            // Entering/leaving items fade and scale between 60% and 100% (DESIGN §4).
            var p = Math.Clamp(item.SlotProgress, 0, 1);
            var scale = 0.6 + 0.4 * p;
            v.Element.Opacity = p * (item.Key == pressedKey ? 0.7 : 1);
            v.Element.RenderTransform = p >= 1 ? Transform.Identity : new ScaleTransform(scale, scale, item.Bounds.Width / 2, item.Bounds.Height / 2);

            if (v.Element is Image image) image.Source = iconFor(item.Key);
            else if (v.Element is Border box && statusFor(item.Key) is { } button)
            {
                ShowStatus(box, button, item.Bounds.Height);
                box.Background = item.Key == pressedKey ? StatusPressed : item.Key == hoverKey ? StatusHover : Brushes.Transparent;
            }
            var offsets = appearance.ShowRunningIndicators ? IndicatorDots.Offsets(windowCount(item.Key)) : Array.Empty<double>();
            for (var d = 0; d < v.Dots.Count; d++)
            {
                var dot = v.Dots[d];
                dot.Visibility = d < offsets.Count ? Visibility.Visible : Visibility.Collapsed;
                if (d >= offsets.Count) continue;
                dot.Opacity = p;
                dot.Fill = _dotBrush;
                Canvas.SetLeft(dot, item.IndicatorCenterX + offsets[d] - DockMetrics.IndicatorDiameter / 2);
                Canvas.SetTop(dot, item.IndicatorCenterY - DockMetrics.IndicatorDiameter / 2);
            }
        }

        foreach (var gone in _visuals.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _canvas.Children.Remove(_visuals[gone].Element);
            foreach (var dot in _visuals[gone].Dots) _canvas.Children.Remove(dot);
            if (_visuals[gone].Badge is { } badge) _canvas.Children.Remove(badge);
            _visuals.Remove(gone);
        }
    }

    public void ShowTooltip(string text, RectD iconBounds)
    {
        _tooltipText.Text = text;
        _tooltip.Visibility = Visibility.Visible;
        _tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _tooltip.DesiredSize;
        Canvas.SetLeft(_tooltip, iconBounds.CenterX - size.Width / 2);
        Canvas.SetTop(_tooltip, Math.Max(0, iconBounds.Y - 12 - size.Height));
    }

    public void HideTooltip() => _tooltip.Visibility = Visibility.Collapsed;

    private static readonly FontFamily GlyphFont = new("Segoe MDL2 Assets");
    private static readonly Brush StatusHover = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush StatusPressed = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    private static Brush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static (double Glyph, double Text, double Sub) StatusFontSizes(double icon) =>
        (Math.Round(icon * 0.40), Math.Round(icon * 0.31), Math.Round(icon * 0.25));

    /// Width (DIP) a status button needs at this icon size.
    public double MeasureStatus(StatusButton b, double icon)
    {
        var (glyphSize, textSize, subSize) = StatusFontSizes(icon);
        double Measure(string? text, FontFamily font, double size) => string.IsNullOrEmpty(text) ? 0
            : new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brushes.White, 1.0).WidthIncludingTrailingWhitespace;
        var glyph = Measure(b.Glyph, GlyphFont, glyphSize);
        var text = Math.Max(Measure(b.Text, SystemFonts.MessageFontFamily, textSize), Measure(b.SubText, SystemFonts.MessageFontFamily, subSize));
        var content = glyph + text + (glyph > 0 && text > 0 ? 4 : 0);
        return Math.Ceiling(Math.Max(icon * 0.62, content + 12));
    }

    private static void ShowStatus(Border box, StatusButton b, double icon)
    {
        if (box.Tag is (StatusButton shown, double size) && shown == b && size == icon) return;
        box.Tag = (b, icon);
        var (glyphSize, textSize, subSize) = StatusFontSizes(icon);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (b.Glyph is not null)
            row.Children.Add(new TextBlock { Text = b.Glyph, FontFamily = GlyphFont, FontSize = glyphSize, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        if (b.Text is not null || b.SubText is not null)
        {
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(b.Glyph is null ? 0 : 4, 0, 0, 0) };
            if (b.Text is not null)
                lines.Children.Add(new TextBlock { Text = b.Text, FontSize = textSize, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            if (b.SubText is not null)
                lines.Children.Add(new TextBlock { Text = b.SubText, FontSize = subSize, Foreground = Brushes.White, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center });
            row.Children.Add(lines);
        }
        box.Child = row;
    }

    private Visuals Create(LayoutItemKind kind)
    {
        FrameworkElement element = kind switch
        {
            LayoutItemKind.Icon => new Image { Stretch = Stretch.Uniform },
            LayoutItemKind.Fixed => new Border { CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, IsHitTestVisible = false },
            _ => new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) },
        };
        _canvas.Children.Add(element);
        var dots = new List<Ellipse>();
        for (var i = 0; kind == LayoutItemKind.Icon && i < IndicatorDots.MaxDots; i++)
        {
            var dot = new Ellipse
            {
                Width = DockMetrics.IndicatorDiameter, Height = DockMetrics.IndicatorDiameter,
                Fill = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            };
            _canvas.Children.Add(dot);
            dots.Add(dot);
        }
        Ellipse? badge = null;
        if (kind == LayoutItemKind.Icon)
        {
            badge = new Ellipse
            {
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30)), Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            };
            _canvas.Children.Add(badge);
            Panel.SetZIndex(badge, 5);
        }
        return new Visuals(element, dots, badge);
    }

    private static void Place(FrameworkElement e, RectD r)
    {
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = Math.Max(0, r.Width);
        e.Height = Math.Max(0, r.Height);
    }

    private sealed record Visuals(FrameworkElement Element, IReadOnlyList<Ellipse> Dots, Ellipse? Badge);
}
