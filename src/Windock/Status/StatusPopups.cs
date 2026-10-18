using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using Windock.Core.Status;
using Windock.Interop;

namespace Windock.Status;

/// A small dark window above a status button; closes when it loses focus. Built when opened, dropped when closed.
internal abstract class DockPopup : Window
{
    private Point _anchor;  // screen DIP: centre x and top y of the button
    private bool _closing;

    /// Closing deactivates the window, which would ask to close it again.
    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    protected DockPopup()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        Foreground = Brushes.White;
        FontSize = 13;
        Deactivated += (_, _) => CloseOnce();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) CloseOnce();
        };
        Closing += (_, _) => _closing = true;
        SizeChanged += (_, _) => Reposition();
    }

    public void ShowAt(Point anchorScreenDip)
    {
        _anchor = anchorScreenDip;
        Show();
        Activate();
        Reposition();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_TOOLWINDOW);
    }

    private void Reposition()
    {
        var area = SystemParameters.PrimaryScreenWidth;
        Left = Math.Clamp(_anchor.X - ActualWidth / 2, 8, Math.Max(8, area - ActualWidth - 8));
        Top = _anchor.Y - ActualHeight - 10;
    }

    protected static Brush Accent => Application.Current.TryFindResource("Windock.Accent") as Brush ?? Brushes.SteelBlue;

    protected static TextBlock Glyph(string glyph, double size) =>
        new() { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, VerticalAlignment = VerticalAlignment.Center };

    protected static Button FlatButton(UIElement content, string tooltip)
    {
        var button = new Button
        {
            Content = content, ToolTip = tooltip, Foreground = Brushes.White, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(6), Cursor = Cursors.Hand, Focusable = false,
        };
        button.Template = HoverTemplate();
        return button;
    }

    private static ControlTemplate HoverTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), "Bd"));
        template.Triggers.Add(hover);
        return template;
    }
}

/// Speaker (mute toggle), slider and percentage for the default playback device.
internal sealed class VolumePopup : DockPopup
{
    private readonly AudioEndpoint _audio;
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 100, Width = 220, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = new() { Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _speaker = Glyph(StatusGlyphs.Mute, 18);
    private bool _updating;

    public VolumePopup(AudioEndpoint audio)
    {
        _audio = audio;
        var mute = FlatButton(_speaker, "Bisukan / suarakan");
        mute.Click += (_, _) => _audio.ToggleMute();
        _slider.ValueChanged += (_, e) =>
        {
            if (!_updating) _audio.SetLevel(e.NewValue / 100);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 12, 8) };
        row.Children.Add(mute);
        row.Children.Add(new Border { Width = 8 });
        row.Children.Add(_slider);
        row.Children.Add(_percent);
        Content = row;
        _audio.Changed += Sync;
        Closed += (_, _) => _audio.Changed -= Sync;
        MouseWheel += (_, e) => _audio.Step(e.Delta > 0 ? 0.02 : -0.02);
        Sync();
    }

    private void Sync()
    {
        _updating = true;
        _slider.Value = Math.Round(_audio.Level * 100);
        _updating = false;
        _percent.Text = $"{Math.Round(_audio.Level * 100)}";
        _speaker.Text = StatusGlyphs.Volume(_audio.Level, _audio.Muted);
    }
}

/// Big time, full date and a month grid (Monday first), with month navigation.
internal sealed class CalendarPopup : DockPopup
{
    private static readonly CultureInfo Id = StatusFormat.Indonesian;
    private readonly bool _use24Hour;
    private readonly TextBlock _time = new() { FontSize = 30, FontWeight = FontWeights.Light };
    private readonly TextBlock _date = new() { Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _month = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly UniformGrid _days = new() { Columns = 7 };
    private readonly System.Windows.Threading.DispatcherTimer _tick;
    private DateTime _shown;

    public CalendarPopup(bool use24Hour)
    {
        _use24Hour = use24Hour;
        var today = DateTime.Today;
        _shown = new DateTime(today.Year, today.Month, 1);

        var prev = FlatButton(Glyph("", 12), "Bulan sebelumnya");
        prev.Click += (_, _) => ShowMonth(_shown.AddMonths(-1));
        var next = FlatButton(Glyph("", 12), "Bulan berikutnya");
        next.Click += (_, _) => ShowMonth(_shown.AddMonths(1));
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        DockPanel.SetDock(next, System.Windows.Controls.Dock.Right);
        DockPanel.SetDock(prev, System.Windows.Controls.Dock.Right);
        header.Children.Add(next);
        header.Children.Add(prev);
        header.Children.Add(_month);

        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 14), Width = 7 * 38 };
        panel.Children.Add(_time);
        panel.Children.Add(_date);
        panel.Children.Add(header);
        panel.Children.Add(_days);
        Content = panel;

        _tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => UpdateTime();
        _tick.Start();
        Closed += (_, _) => _tick.Stop();
        UpdateTime();
        ShowMonth(_shown);
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;
        _time.Text = StatusFormat.Clock(now, _use24Hour, showSeconds: true);
        _date.Text = StatusFormat.LongDate(now);
    }

    private void ShowMonth(DateTime first)
    {
        _shown = first;
        _month.Text = Id.TextInfo.ToTitleCase(first.ToString("MMMM yyyy", Id));
        _days.Children.Clear();
        foreach (var name in new[] { "Sen", "Sel", "Rab", "Kam", "Jum", "Sab", "Min" })
            _days.Children.Add(new TextBlock { Text = name, Opacity = 0.6, FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 4) });

        var offset = ((int)first.DayOfWeek + 6) % 7;  // Monday = 0
        var start = first.AddDays(-offset);
        var today = DateTime.Today;
        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var isToday = day == today;
            var cell = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(4, 1, 4, 1),
                Background = isToday ? Accent : Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = day.Day.ToString(CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Opacity = day.Month == first.Month ? 1 : 0.35, FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal,
                },
            };
            _days.Children.Add(cell);
        }
    }
}

/// Windock's own tray: every notification-area icon in a grid above the ^ button. Clicks go to the real icons,
/// so apps show their windows and menus as usual; the Windows taskbar stays hidden.
internal sealed class TrayPopup : DockPopup
{
    private const int Columns = 6;

    public TrayPopup(IReadOnlyList<NotifyAreaIcon> icons)
    {
        var grid = new WrapPanel { Width = Math.Min(Math.Max(icons.Count, 1), Columns) * 40, Margin = new Thickness(6) };
        foreach (var icon in icons)
        {
            var image = new Image { Width = 20, Height = 20, Source = ToImage(icon.Icon) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var button = FlatButton(image, string.IsNullOrWhiteSpace(icon.Tooltip) ? "(tanpa nama)" : icon.Tooltip);
            button.Width = button.Height = 40;
            button.PreviewMouseLeftButtonUp += (_, _) => TrayIcons.Click(icon, TrayClick.Left);
            button.PreviewMouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                TrayIcons.Click(icon, TrayClick.Right);
            };
            button.MouseDoubleClick += (_, _) => TrayIcons.Click(icon, TrayClick.Double);
            grid.Children.Add(button);
        }
        if (icons.Count == 0)
            grid.Children.Add(new TextBlock { Text = "Tidak ada ikon tray.", Opacity = 0.7, Margin = new Thickness(8) });
        Content = grid;
    }

    private static ImageSource? ToImage(IntPtr icon)
    {
        if (icon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null;  // the app replaced its icon meanwhile
        }
    }
}
