using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Windock.Core.Island;
using Windock.Interop;
using MediaState = Windock.Core.Island.MediaState;

namespace Windock.Island;

/// Everything the island draws at one moment; built by IslandController. Mood null: Wado is turned off.
internal sealed record IslandView(
    IslandMode Mode, bool Expanded,
    string? Clock, string? Date, string? BatteryGlyph, string? BatteryPercent,
    string BigClock, string LongDate, string? BatteryLine,
    MediaState? Media, ImageSource? Cover,
    IslandEvent? Event,
    IReadOnlyList<IslandPanel> Panels, int Page, IReadOnlyList<DeviceEntry>? Devices, WeatherView Weather,
    bool Charging, WadoMood? Mood, LyricsView Lyrics, FocusView? Focus);

/// The focus timer's state apart from the seconds ticking by (those go through UpdateFocus).
internal sealed record FocusView(FocusPhase Phase, int Session, int Sessions, bool Running, int NextBreakMinutes);

/// Lyrics on the Music page. Show false: hidden by the user (or turned off); Lines null: Message says why.
internal sealed record LyricsView(bool Show, IReadOnlyList<LyricLine>? Lines, string? Message)
{
    public static readonly LyricsView Hidden = new(false, null, null);
}

/// Pages of the open card, in order. Home is always the clock card; Music has the player controls.
internal enum IslandPanel { Home, Music, Weather, Devices, Focus }

/// The Weather panel: a report, or a message instead (no city yet, city not found, loading, offline).
internal sealed record WeatherView(WeatherReport? Report, string? Place, string? Message, bool Stale);

/// The Dynamic Island: a transparent, click-through, topmost tool window at the top centre of the primary
/// screen holding one pill. The pill's size springs between states (DESIGN: Dynamic Island). The open card has a
/// header (time, activity, Wado), the selected panel, and a tab bar (spec 2026-10-26-island-v2). A 2 px strip at
/// the top edge brings the island back when slid out of view, and keeps it there while the pointer rests on it.
internal sealed partial class IslandWindow : Window
{
    public const double CompactHeight = 34, ExpandedWidth = 560, TopMargin = 6;
    /// The compact pill is this much wider than its content (the user asked for 20 % wider than v0.10.0).
    private const double CompactWidthFactor = 1.2;
    private const double CanvasWidth = 700, CanvasHeight = 260, EdgeStripWidth = 384;
    private const int WM_MOUSEHWHEEL = 0x020E;
    /// The card follows an unfinished swipe by up to this share of a page, then turns or springs back.
    private const double DragFollow = 0.3;
    private static readonly Duration SpringDuration = TimeSpan.FromMilliseconds(420);
    private static readonly Duration SlideDuration = TimeSpan.FromMilliseconds(250);

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
    private readonly SwipeGesture _swipe = new();
    private readonly System.Diagnostics.Stopwatch _swipeClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly System.Windows.Threading.DispatcherTimer _swipeEnd = new() { Interval = SwipeGesture.GestureGap };
    private IslandView? _view;
    private TranslateTransform? _pager;
    private readonly List<Border> _tabs = new();
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
    public event Action? CoverClicked, PlayPauseClicked, NextClicked, PreviousClicked, LyricsToggled;
    public event Action? FocusStartPause, FocusReset, FocusBreak;
    /// -1 previous page, +1 next page.
    public event Action<int>? PageTurnRequested;
    /// A tab in the tab bar was clicked: its page index.
    public event Action<int>? TabClicked;

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

    private static bool SameLayout(IslandView a, IslandView b) =>
        a.Mode == b.Mode && a.Expanded == b.Expanded && a.Media == b.Media && ReferenceEquals(a.Cover, b.Cover) && a.Event == b.Event
        && a.Clock == b.Clock && a.Date == b.Date && a.BatteryGlyph == b.BatteryGlyph && a.BatteryPercent == b.BatteryPercent
        && a.BigClock == b.BigClock && a.LongDate == b.LongDate && a.BatteryLine == b.BatteryLine
        && a.Panels.SequenceEqual(b.Panels) && ReferenceEquals(a.Devices, b.Devices) && a.Weather == b.Weather
        && a.Charging == b.Charging && a.Mood == b.Mood && a.Lyrics == b.Lyrics && a.Focus == b.Focus;

    private static double MinCompactWidth(IslandView v) => v.Mode switch
    {
        IslandMode.Media => 200,
        _ when v.Focus is { Running: true } => 150,
        IslandMode.Event => 240,
        _ => 120,
    };

    private void Spring(DependencyProperty property, double to) =>
        _pill.BeginAnimation(property, new DoubleAnimation(to, SpringDuration)
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut },
        });

    // ---- the open card: header, pages, tab bar ----

    private FrameworkElement BuildExpanded(IslandView v)
    {
        _pager = null;
        _tabs.Clear();
        ResetProgress();
        var card = new StackPanel { Width = ExpandedWidth };
        card.Children.Add(Header(v));
        if (v.Panels.Count < 2)
        {
            card.Children.Add(IdleCard(v));
            return card;
        }

        // Pages side by side in a strip that slides; all as tall as the tallest so the card does not jump.
        var strip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var panel in v.Panels)
        {
            var page = panel switch
            {
                IslandPanel.Music => MusicPage(v),
                IslandPanel.Devices => DevicesPage(v.Devices),
                IslandPanel.Weather => WeatherPage(v.Weather),
                IslandPanel.Focus when v.Focus is { } focus => FocusPage(focus),
                _ => IdleCard(v),
            };
            strip.Children.Add(new Border { Width = ExpandedWidth, Padding = new Thickness(8, 0, 8, 0), Child = page });
        }
        _pager = new TranslateTransform();
        strip.RenderTransform = _pager;
        // A negative right margin arranges the strip at its full width (an element arranged narrower than it wants
        // is clipped to that width, which would cut off every page but the first); the grid clips to one page.
        strip.Margin = new Thickness(0, 0, -(v.Panels.Count - 1) * ExpandedWidth, 0);
        var pages = new Grid { ClipToBounds = true, Width = ExpandedWidth };
        pages.Children.Add(strip);
        card.Children.Add(pages);
        card.Children.Add(TabBar(v.Panels));
        return card;
    }

    /// Time and charging at the left; music and Wado at the right.
    private static FrameworkElement Header(IslandView v)
    {
        var header = new DockPanel { Margin = new Thickness(20, 10, 18, 0), Height = 24, LastChildFill = false };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (v.Charging) left.Children.Add(new Border { Child = Glyph(Bolt, 13, Accent), Margin = new Thickness(0, 0, 6, 0) });
        left.Children.Add(Text(v.Clock ?? v.BigClock, 12, FontWeights.SemiBold));
        DockPanel.SetDock(left, System.Windows.Controls.Dock.Left);
        header.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (v.Media is { Playing: true }) right.Children.Add(new Border { Child = Glyph(IslandGlyphs.Music, 13, Sky), Margin = new Thickness(0, 0, 8, 0) });
        if (v.Mood is { } mood) right.Children.Add(new WadoMascot(mood, 30));
        DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
        header.Children.Add(right);
        return header;
    }

    private FrameworkElement TabBar(IReadOnlyList<IslandPanel> panels)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 10) };
        for (var i = 0; i < panels.Count; i++)
        {
            var (glyph, font) = TabGlyph(panels[i]);
            var tab = new Border
            {
                Width = 38, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(2, 0, 2, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = glyph, FontFamily = font, FontSize = 14, Foreground = TabIdle,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var index = i;
            tab.MouseLeftButtonUp += (_, e) => { e.Handled = true; TabClicked?.Invoke(index); };
            _tabs.Add(tab);
            bar.Children.Add(tab);
        }
        return bar;
    }

    private static (string Glyph, FontFamily Font) TabGlyph(IslandPanel panel) => panel switch
    {
        IslandPanel.Music => ("", Icons),
        IslandPanel.Weather => (WeatherCodes.Cloud, Symbols),
        IslandPanel.Devices => ("", Icons),
        IslandPanel.Focus => ("", Icons),
        _ => ("", Icons),
    };

    private void ShowPage(int page, bool animate)
    {
        if (_pager is null || _tabs.Count == 0) return;
        page = Math.Clamp(page, 0, _tabs.Count - 1);
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
        for (var i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Background = i == page ? TabSelected : Brushes.Transparent;
            ((TextBlock)_tabs[i].Child).Foreground = i == page ? Brushes.White : TabIdle;
        }
    }
}
