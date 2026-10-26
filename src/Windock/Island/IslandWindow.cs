using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Windock.Core.Island;
using Windock.Interop;
using MediaState = Windock.Core.Island.MediaState;

namespace Windock.Island;

/// Everything the island draws at one moment; built by IslandController.
internal sealed record IslandView(
    IslandMode Mode, bool Expanded,
    string? Clock, string? Date, string? BatteryGlyph, string? BatteryPercent,
    string BigClock, string LongDate, string? BatteryLine,
    MediaState? Media, ImageSource? Cover,
    IslandEvent? Event,
    IReadOnlyList<IslandPanel> Panels, int Page, IReadOnlyList<DeviceEntry>? Devices, WeatherView Weather);

/// Pages of the open card, in order. Home is always the clock card; Music has the player controls.
internal enum IslandPanel { Home, Music, Devices, Weather }

/// The Weather panel: a report, or a message instead (no city yet, city not found, loading, offline).
internal sealed record WeatherView(WeatherReport? Report, string? Place, string? Message, bool Stale);

/// The Dynamic Island: a transparent, click-through, topmost tool window at the top centre of the primary
/// screen holding one pill. The pill's size springs between states (DESIGN: Dynamic Island). A 2 px strip at the
/// top edge above it brings it back when slid out of view, and keeps it there while the pointer rests on the edge.
internal sealed class IslandWindow : Window
{
    /// The open card is wide and short (user's choice): 480 x about 130.
    public const double CompactHeight = 34, ExpandedWidth = 480, TopMargin = 6;
    /// The compact pill is this much wider than its content (the user asked for 20 % wider than v0.10.0).
    private const double CompactWidthFactor = 1.2;
    private const double CanvasWidth = 600, CanvasHeight = 230, EdgeStripWidth = 384;
    /// Invisible click areas at the open card's left and right edges that turn the page.
    private const double PageZoneWidth = 40, PagePadding = 24;
    private static readonly Duration SpringDuration = TimeSpan.FromMilliseconds(420);
    private static readonly Duration SlideDuration = TimeSpan.FromMilliseconds(250);
    private static readonly Brush Secondary = Frozen(Color.FromRgb(0xA8, 0xA8, 0xA8));
    private static readonly Brush Track = Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Bars = Frozen(Color.FromRgb(0x5D, 0xCA, 0xA5));
    private static readonly FontFamily Icons = new("Segoe MDL2 Assets");
    private static readonly FontFamily Symbols = new("Segoe UI Symbol");
    private static readonly Brush Dot = Frozen(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));

    private readonly Grid _root = new();
    private readonly Border _pill = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
    private readonly ContentControl _content = new() { Foreground = Brushes.White, Focusable = false };
    private readonly Border _edgeStrip = new()
    {
        Height = 2, Width = EdgeStripWidth, VerticalAlignment = VerticalAlignment.Top,
        Background = Frozen(Color.FromArgb(1, 0, 0, 0)),  // alpha 1: invisible but receives the mouse
    };
    private readonly TranslateTransform _slide = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private Storyboard? _bars;
    private Border? _progressFill;
    private TextBlock? _progressText, _durationText;
    private IslandView? _view;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private readonly SwipeGesture _swipe = new();
    private readonly System.Diagnostics.Stopwatch _swipeClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly System.Windows.Threading.DispatcherTimer _swipeEnd = new() { Interval = SwipeGesture.GestureGap };
    private TranslateTransform? _pager;
    private StackPanel? _dots;
    private bool _shown = true;

    public IslandWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Windock Dynamic Island";
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        _pill.Child = _content;
        _pill.RenderTransform = _slide;
        _pill.Margin = new Thickness(0, TopMargin, 0, 0);
        _pill.Height = CompactHeight;
        _pill.Width = 160;
        _pill.SizeChanged += (_, _) => _pill.CornerRadius = new CornerRadius(Math.Min(_pill.ActualHeight / 2, 28));
        _pill.MouseEnter += (_, _) => PointerEntered?.Invoke();
        _pill.MouseLeave += (_, _) => PointerLeft?.Invoke();
        _pill.MouseWheel += (_, e) =>
        {
            if (_view is not { Expanded: true } || _view.Panels.Count < 2) return;
            e.Handled = true;
            Swipe(-e.Delta);  // wheel up: previous page
        };
        _swipeEnd.Tick += (_, _) => OnSwipeEnded();
        _edgeStrip.MouseEnter += (_, _) => EdgeEntered?.Invoke();
        _edgeStrip.MouseLeave += (_, _) => EdgeLeft?.Invoke();
        IsVisibleChanged += (_, _) => UpdateBars();  // hidden for a fullscreen app: the bars stop too

        _root.LayoutTransform = _scale;
        _root.Children.Add(_pill);
        _root.Children.Add(_edgeStrip);
        Content = _root;
        Place();
    }

    public event Action? PointerEntered, PointerLeft, EdgeEntered, EdgeLeft;
    public event Action? CoverClicked, PlayPauseClicked, NextClicked, PreviousClicked;
    /// -1 previous page, +1 next page.
    public event Action<int>? PageTurnRequested;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public bool IsShown => _shown;

    /// The compact pill's area in screen pixels (for "does a window cover the island?").
    public (int Left, int Top, int Right, int Bottom) CompactAreaPx
    {
        get
        {
            var dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var half = 180 * _scale.ScaleX * dpi;
            var centre = (Left + Width / 2) * dpi;
            return ((int)(centre - half), (int)(Top * dpi), (int)(centre + half), (int)((Top + (TopMargin + CompactHeight + 6) * _scale.ScaleY) * dpi));
        }
    }

    public void SetAppearance(double scale, Color color)
    {
        _scale.ScaleX = _scale.ScaleY = scale;
        _pill.Background = Frozen(color);
        Place();
    }

    public void Place()
    {
        Width = CanvasWidth * _scale.ScaleX;
        Height = CanvasHeight * _scale.ScaleY;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    public void RaiseToTop() => WindowEffects.RaiseTopmost(Handle);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.MakeToolNoActivate(Handle);
        HwndSource.FromHwnd(Handle)?.AddHook(OnMessage);
    }

    /// A two-finger touchpad swipe arrives as horizontal wheel steps, which WPF has no event for. Fingers moving
    /// left scroll right (positive steps): the next page, as on a phone.
    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOUSEHWHEEL || _view is not { Expanded: true } || _view.Panels.Count < 2) return IntPtr.Zero;
        Swipe((short)((wParam.ToInt64() >> 16) & 0xFFFF));
        handled = true;
        return IntPtr.Zero;
    }

    /// The card follows an unfinished swipe by up to this share of a page, then turns or springs back.
    private const double DragFollow = 0.3;

    private void Swipe(int delta)
    {
        var step = _swipe.Feed(delta, _swipeClock.Elapsed);
        _swipeEnd.Stop();
        _swipeEnd.Start();
        if (step.Turn != 0)
        {
            PageTurnRequested?.Invoke(step.Turn);
            return;
        }
        if (_pager is null || _view is null || step.Drag == 0) return;
        _pager.BeginAnimation(TranslateTransform.XProperty, null);
        _pager.X = -_view.Page * ExpandedWidth - step.Drag * DragFollow * ExpandedWidth;
    }

    /// The fingers stopped before the swipe turned the page: back to where it was.
    private void OnSwipeEnded()
    {
        _swipeEnd.Stop();
        _swipe.Reset();
        if (_view is { Expanded: true } && _pager is not null && Math.Abs(_pager.X + _view.Page * ExpandedWidth) > 0.5)
            ShowPage(_view.Page, animate: true);
    }

    /// Slides the pill into view or up out of it.
    public void SetShown(bool shown)
    {
        if (shown == _shown) return;
        _shown = shown;
        var to = shown ? 0 : -(TopMargin + _pill.ActualHeight + 4);
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(to, SlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = shown ? EasingMode.EaseOut : EasingMode.EaseIn },
        });
        UpdateBars();
    }

    public void Render(IslandView view)
    {
        var old = _view;
        _view = view;
        if (old is not null && SameLayout(old, view))
        {
            if (old.Page != view.Page) ShowPage(view.Page, animate: true);
            return;  // e.g. only the media position moved: UpdateProgress handles it
        }
        var content = view.Expanded ? BuildExpanded(view) : BuildCompact(view);
        var maxWidth = view.Expanded ? ExpandedWidth : 480;
        _content.Content = content;  // measured in place, so it inherits the window's font
        content.Measure(new Size(maxWidth, double.PositiveInfinity));
        var width = view.Expanded ? ExpandedWidth
            : Math.Clamp(content.DesiredSize.Width * CompactWidthFactor, MinCompactWidth(view) * CompactWidthFactor, maxWidth);
        var height = view.Expanded ? Math.Max(96, content.DesiredSize.Height) : CompactHeight;
        if (view.Expanded) ShowPage(view.Page, animate: old is { Expanded: true } && old.Page != view.Page);

        var changedShape = old is null || old.Mode != view.Mode || old.Expanded != view.Expanded || old.Event != view.Event;
        if (changedShape)
        {
            content.Opacity = 0;
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { BeginTime = TimeSpan.FromMilliseconds(90) });
        }
        Spring(WidthProperty, width);
        Spring(HeightProperty, height);
        UpdateBars();
    }

    /// The media card's progress bar and times (called each second while it is open and playing).
    public void UpdateProgress(TimeSpan position, TimeSpan duration)
    {
        if (_progressFill is null || _progressText is null || _durationText is null) return;
        var fraction = duration > TimeSpan.Zero ? Math.Clamp(position / duration, 0, 1) : 0;
        _progressFill.Width = fraction * ProgressWidth;
        _progressText.Text = Time(position);
        _durationText.Text = Time(duration);
    }

    private static bool SameLayout(IslandView a, IslandView b) =>
        a.Mode == b.Mode && a.Expanded == b.Expanded && a.Media == b.Media && ReferenceEquals(a.Cover, b.Cover) && a.Event == b.Event
        && a.Clock == b.Clock && a.Date == b.Date && a.BatteryGlyph == b.BatteryGlyph && a.BatteryPercent == b.BatteryPercent
        && a.BigClock == b.BigClock && a.LongDate == b.LongDate && a.BatteryLine == b.BatteryLine
        && a.Panels.SequenceEqual(b.Panels) && ReferenceEquals(a.Devices, b.Devices) && a.Weather == b.Weather;

    private static double MinCompactWidth(IslandView v) => v.Mode switch
    {
        IslandMode.Media => 200,
        IslandMode.Event => 240,
        _ => 120,
    };

    private void Spring(DependencyProperty property, double to) =>
        _pill.BeginAnimation(property, new DoubleAnimation(to, SpringDuration)
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut },
        });

    // ---- compact ----

    private FrameworkElement BuildCompact(IslandView v)
    {
        _progressFill = null;
        _progressText = _durationText = null;
        return v.Mode switch
        {
            IslandMode.Event when v.Event is { } e => EventRow(e),
            IslandMode.Media when v.Media is { } m => MediaRow(m, v.Cover),
            _ => IdleRow(v),
        };
    }

    private static FrameworkElement IdleRow(IslandView v)
    {
        var row = Row();
        if (v.Clock is { } clock) row.Children.Add(Text(clock, 13, FontWeights.SemiBold));
        if (v.Date is { } date) row.Children.Add(Spaced(Text(date, 13, brush: Secondary), row));
        if (v.BatteryGlyph is { } glyph)
        {
            var battery = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (v.BatteryPercent is { } percent) battery.Children.Add(new Border { Child = Text(percent, 12, brush: Secondary), Margin = new Thickness(0, 0, 4, 0) });
            battery.Children.Add(Glyph(glyph, 15));
            row.Children.Add(Spaced(battery, row));
        }
        return row;
    }

    private FrameworkElement MediaRow(MediaState m, ImageSource? cover)
    {
        var row = new DockPanel { Margin = new Thickness(7, 0, 14, 0), Height = CompactHeight, LastChildFill = false };
        var art = Cover(cover, 22, 6);
        art.MouseLeftButtonUp += (_, e) => { e.Handled = true; CoverClicked?.Invoke(); };
        DockPanel.SetDock(art, System.Windows.Controls.Dock.Left);
        row.Children.Add(art);
        FrameworkElement right = m.Playing ? BarsVisual() : Glyph(IslandGlyphs.Pause, 12, Secondary);
        DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
        row.Children.Add(right);
        return row;
    }

    private static FrameworkElement EventRow(IslandEvent e)
    {
        var row = Row();
        row.Children.Add(Glyph(e.Glyph, 16));
        if (e.Level is { } level)
        {
            var track = new Grid { Width = 120, Height = 4, VerticalAlignment = VerticalAlignment.Center };
            track.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(2) });
            track.Children.Add(new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Clamp(level, 0, 1) * 120,
            });
            row.Children.Add(Spaced(track, row, 12));
            row.Children.Add(Spaced(new TextBlock
            {
                Text = e.Detail ?? $"{Math.Round(level * 100)}", Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            }, row, 6));
            return row;
        }
        var text = Text(e.Text, 13, FontWeights.SemiBold);
        text.MaxWidth = 200;
        row.Children.Add(Spaced(text, row, 10));
        if (e.Detail is { } detail)
        {
            var d = Text(detail, 13, brush: Secondary);
            d.MaxWidth = 150;
            row.Children.Add(Spaced(d, row, 10));
        }
        return row;
    }

    // ---- expanded ----

    private FrameworkElement BuildExpanded(IslandView v)
    {
        _pager = null;
        _dots = null;
        _progressFill = null;
        _progressText = _durationText = null;
        if (v.Panels.Count < 2) return IdleCard(v);

        // Pages side by side in a strip that slides; all as tall as the tallest so the card does not jump.
        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var panel in v.Panels)
        {
            var page = panel switch
            {
                IslandPanel.Music => MusicPage(v),
                IslandPanel.Devices => DevicesPage(v.Devices),
                IslandPanel.Weather => WeatherPage(v.Weather),
                _ => IdleCard(v),
            };
            strip.Children.Add(new Border { Width = ExpandedWidth, Padding = new Thickness(PagePadding, 0, PagePadding, 0), Child = page });
        }
        _pager = new TranslateTransform();
        strip.RenderTransform = _pager;
        // A negative right margin arranges the strip at its full width (an element arranged narrower than it wants
        // is clipped to that width, which would cut off every page but the first); the grid clips to one page.
        strip.Margin = new Thickness(0, 0, -(v.Panels.Count - 1) * ExpandedWidth, 18);  // 18: room for the dots

        var root = new Grid { ClipToBounds = true, Width = ExpandedWidth };
        root.Children.Add(strip);
        root.Children.Add(PageZone(HorizontalAlignment.Left, -1));
        root.Children.Add(PageZone(HorizontalAlignment.Right, 1));
        _dots = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 9),
        };
        for (var i = 0; i < v.Panels.Count; i++)
            _dots.Children.Add(new Border { Height = 6, Width = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0, 3, 0), Background = Dot });
        root.Children.Add(_dots);
        return root;
    }

    private FrameworkElement MusicPage(IslandView v)
    {
        if (v.Media is { } m) return MediaCard(m, v.Cover);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 18, 16, 8) };
        panel.Children.Add(Cover(null, 56, 12));
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text("Nothing playing", 14, FontWeights.SemiBold));
        text.Children.Add(Text("Play something in Spotify, a browser or a media player", 12, brush: Secondary));
        panel.Children.Add(text);
        return panel;
    }

    /// Transparent, so it takes clicks without being seen (the user wanted no arrows).
    private Border PageZone(HorizontalAlignment side, int delta)
    {
        var zone = new Border { Width = PageZoneWidth, HorizontalAlignment = side, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 20) };
        zone.MouseLeftButtonUp += (_, e) => { e.Handled = true; PageTurnRequested?.Invoke(delta); };
        return zone;
    }

    private void ShowPage(int page, bool animate)
    {
        if (_pager is null || _dots is null) return;
        page = Math.Clamp(page, 0, _dots.Children.Count - 1);
        var to = -page * ExpandedWidth;
        if (animate)
        {
            _pager.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        else
        {
            _pager.BeginAnimation(TranslateTransform.XProperty, null);
            _pager.X = to;
        }
        for (var i = 0; i < _dots.Children.Count; i++)
        {
            var dot = (Border)_dots.Children[i];
            dot.Background = i == page ? Brushes.White : Dot;
            dot.BeginAnimation(WidthProperty, new DoubleAnimation(i == page ? 14 : 6, TimeSpan.FromMilliseconds(200)));
        }
    }

    // ---- devices and weather pages ----

    private const int MaxDeviceRows = 6;

    private static FrameworkElement DevicesPage(IReadOnlyList<DeviceEntry>? devices)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 8) };
        var header = new DockPanel();
        if (devices is { Count: > MaxDeviceRows })
        {
            var more = Text($"+{devices.Count - MaxDeviceRows} more", 12, brush: Secondary);
            DockPanel.SetDock(more, System.Windows.Controls.Dock.Right);
            header.Children.Add(more);
        }
        header.Children.Add(Text("Devices", 12, brush: Secondary));
        panel.Children.Add(header);
        if (devices is null || devices.Count == 0)
        {
            panel.Children.Add(new Border { Margin = new Thickness(0, 10, 0, 0), Child = Text(devices is null ? "Looking for devices\u2026" : "No devices connected", 13, brush: Secondary) });
            return panel;
        }
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var d in devices.Take(MaxDeviceRows))
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 12, 0) };
            var icon = Glyph(DeviceBurst.Glyph(d.Kind), 15);
            icon.Width = 24;
            DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
            row.Children.Add(icon);
            var extra = string.Join(" \u00b7 ", new[] { d.Battery is { } b ? $"{b}%" : null, d.Note }.Where(x => x is not null));
            if (extra.Length > 0)
            {
                var right = Text(extra, 12, brush: Secondary);
                right.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
                row.Children.Add(right);
            }
            row.Children.Add(Text(d.Name ?? d.Kind.ToString(), 13));
            grid.Children.Add(row);
        }
        panel.Children.Add(grid);
        return panel;
    }

    private static FrameworkElement WeatherPage(WeatherView w)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 8) };
        if (w.Report is not { } r)
        {
            panel.Children.Add(Text("Weather", 12, brush: Secondary));
            var message = Text(w.Message ?? "Loading weather…", 13, brush: Secondary);
            message.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(new Border { Margin = new Thickness(0, 10, 0, 0), Child = message });
            return panel;
        }
        var (condition, glyph) = WeatherCodes.Describe(r.Code, r.IsDay);
        var top = new DockPanel();
        var icon = new TextBlock { Text = glyph, FontFamily = Symbols, FontSize = 30, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
        top.Children.Add(icon);
        var range = new TextBlock
        {
            Text = $"H {WeatherCodes.Temperature(r.High)}\nL {WeatherCodes.Temperature(r.Low)}", FontSize = 12, Foreground = Secondary,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(range, System.Windows.Controls.Dock.Right);
        top.Children.Add(range);
        var now = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        now.Children.Add(Text(WeatherCodes.Temperature(r.Temperature), 28, FontWeights.Light));
        now.Children.Add(Text(string.Join(" · ", new[] { w.Place, condition }.Where(x => !string.IsNullOrWhiteSpace(x))), 12, brush: Secondary));
        top.Children.Add(now);
        panel.Children.Add(top);

        var hours = new UniformGrid { Rows = 1, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var h in r.Hours)
        {
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            cell.Children.Add(new TextBlock { Text = h.Time.ToString("HH"), FontSize = 11, Foreground = Secondary, HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(new TextBlock
            {
                Text = WeatherCodes.Describe(h.Code, h.IsDay).Glyph, FontFamily = Symbols, FontSize = 14, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2),
            });
            cell.Children.Add(new TextBlock { Text = WeatherCodes.Temperature(h.Temperature), FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            hours.Children.Add(cell);
        }
        panel.Children.Add(hours);
        if (w.Stale) panel.Children.Add(new Border { Margin = new Thickness(0, 4, 0, 0), Child = Text($"Offline · updated {r.FetchedAt:HH:mm}", 11, brush: Secondary) });
        return panel;
    }

    private static FrameworkElement IdleCard(IslandView v)
    {
        var panel = new StackPanel { Margin = new Thickness(22, 12, 22, 8) };
        panel.Children.Add(Text(v.BigClock, 38, FontWeights.Light));
        panel.Children.Add(Text(v.LongDate, 13, brush: Secondary));
        if (v.BatteryLine is { } line)
        {
            var battery = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            if (v.BatteryGlyph is { } glyph) battery.Children.Add(new Border { Child = Glyph(glyph, 15, Secondary), Margin = new Thickness(0, 0, 8, 0) });
            battery.Children.Add(Text(line, 13, brush: Secondary));
            panel.Children.Add(battery);
        }
        return panel;
    }

    private const double ProgressWidth = 320;

    private FrameworkElement MediaCard(MediaState m, ImageSource? cover)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 14, 16, 8) };

        var top = new DockPanel();
        var art = Cover(cover, 56, 12);
        art.MouseLeftButtonUp += (_, e) => { e.Handled = true; CoverClicked?.Invoke(); };
        DockPanel.SetDock(art, System.Windows.Controls.Dock.Left);
        top.Children.Add(art);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(ControlButton(IslandGlyphs.Previous, 16, () => PreviousClicked?.Invoke()));
        controls.Children.Add(ControlButton(m.Playing ? IslandGlyphs.Pause : IslandGlyphs.Play, 20, () => PlayPauseClicked?.Invoke()));
        controls.Children.Add(ControlButton(IslandGlyphs.Next, 16, () => NextClicked?.Invoke()));
        DockPanel.SetDock(controls, System.Windows.Controls.Dock.Right);
        top.Children.Add(controls);
        var titles = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        titles.MouseLeftButtonUp += (_, e) => { e.Handled = true; CoverClicked?.Invoke(); };
        titles.Children.Add(Text(m.Title, 14, FontWeights.SemiBold));
        var sub = string.Join(" \u00b7 ", new[] { m.Artist, m.App }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (sub.Length > 0) titles.Children.Add(Text(sub, 12, brush: Secondary));
        top.Children.Add(titles);
        panel.Children.Add(top);

        var progress = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        _progressText = new TextBlock { FontSize = 11, Foreground = Secondary, Width = 36, VerticalAlignment = VerticalAlignment.Center };
        _durationText = new TextBlock { FontSize = 11, Foreground = Secondary, Width = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_progressText, System.Windows.Controls.Dock.Left);
        DockPanel.SetDock(_durationText, System.Windows.Controls.Dock.Right);
        progress.Children.Add(_progressText);
        progress.Children.Add(_durationText);
        var track = new Grid { Height = 4, Width = ProgressWidth, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(2) });
        _progressFill = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Children.Add(_progressFill);
        progress.Children.Add(track);
        panel.Children.Add(progress);
        return panel;
    }

    private static Border ControlButton(string glyph, double size, Action click)
    {
        var hover = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        var button = new Border
        {
            Child = Glyph(glyph, size), Width = 40, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent, Cursor = Cursors.Hand,
        };
        ((TextBlock)button.Child).HorizontalAlignment = HorizontalAlignment.Center;
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    // ---- music bars ----

    private FrameworkElement BarsVisual()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 14 };
        _bars?.Stop(this);
        _bars = new Storyboard();
        Timeline.SetDesiredFrameRate(_bars, 20);  // a small layered window: 20 fps looks the same and costs a third
        var periods = new[] { 0.45, 0.32, 0.52, 0.38 };
        for (var i = 0; i < periods.Length; i++)
        {
            var scale = new ScaleTransform(1, 0.4);
            var bar = new Rectangle
            {
                Width = 3, Height = 14, RadiusX = 1.5, RadiusY = 1.5, Fill = Bars, Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
            };
            panel.Children.Add(bar);
            var anim = new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(periods[i])) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Storyboard.SetTarget(anim, bar);
            Storyboard.SetTargetProperty(anim, new PropertyPath("RenderTransform.ScaleY"));
            _bars.Children.Add(anim);
        }
        return panel;
    }

    /// Bars move only while the compact media pill is on screen and playing.
    private void UpdateBars()
    {
        var run = _shown && IsVisible && _view is { Mode: IslandMode.Media, Expanded: false, Media.Playing: true };
        if (_bars is null) return;
        if (run) _bars.Begin(this, true);
        else _bars.Stop(this);
        if (!run && _view is not { Mode: IslandMode.Media, Expanded: false }) _bars = null;
    }

    // ---- helpers ----

    private static StackPanel Row() => new()
    {
        Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 0), Height = CompactHeight, HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static FrameworkElement Spaced(FrameworkElement element, Panel row, double gap = 14)
    {
        if (row.Children.Count > 0) element.Margin = new Thickness(gap, element.Margin.Top, element.Margin.Right, element.Margin.Bottom);
        return element;
    }

    private static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? brush = null) => new()
    {
        Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, Foreground = brush ?? Brushes.White,
    };

    private static TextBlock Glyph(string glyph, double size, Brush? brush = null) => new()
    {
        Text = glyph, FontFamily = Icons, FontSize = size, VerticalAlignment = VerticalAlignment.Center, Foreground = brush ?? Brushes.White,
    };

    private static Border Cover(ImageSource? image, double size, double radius) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(radius), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
        Background = image is null ? Frozen(Color.FromRgb(0x1D, 0x9E, 0x75)) : new ImageBrush(image) { Stretch = Stretch.UniformToFill },
        Child = image is null ? new TextBlock
        {
            Text = IslandGlyphs.Music, FontFamily = Icons, FontSize = size * 0.45, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        } : null,
    };

    private static string Time(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}
