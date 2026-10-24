using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Windock.Core.Apps;
using Windock.Core.Layout;
using Windock.Core.Settings;
using Windock.Interop;

namespace Windock.Switcher;

/// The Alt+Tab panel: a search line and one card per window (live preview, icon, title), grouped by app if wanted.
/// Never takes focus — the keyboard is read by AltTabHook — and is thrown away when the switch is done.
internal sealed class SwitcherWindow : Window
{
    private readonly IconProvider _icons;
    private readonly SwitcherSettings _options;
    private readonly IntPtr _monitor;
    private readonly TextBlock _search = new() { FontSize = 15, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _body = new();
    private readonly List<(SwitcherEntry Entry, Border Card, Border Slot, bool Minimized)> _cards = new();
    private readonly List<DwmThumbnail?> _thumbnails = new();
    private static readonly Brush Panel = Frozen(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly Brush CardBack = Frozen(Color.FromRgb(0x2C, 0x2C, 0x2C));
    private static readonly Brush SlotBack = Frozen(Color.FromRgb(0x18, 0x18, 0x18));
    private static readonly Brush Muted = Frozen(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));

    public SwitcherWindow(IconProvider icons, SwitcherSettings options, IntPtr monitor)
    {
        _icons = icons;
        _options = options;
        _monitor = monitor;
        Title = "Windock Alt+Tab";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Panel;
        BorderBrush = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        MaxWidth = SystemParameters.PrimaryScreenWidth * 0.86;
        MaxHeight = SystemParameters.PrimaryScreenHeight * 0.8;

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 10) };
        searchRow.Children.Add(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = Muted,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        });
        searchRow.Children.Add(_search);
        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
        root.Children.Add(searchRow);
        root.Children.Add(_body);
        Content = root;
        SizeChanged += (_, _) => Arrange();
    }

    /// A card was clicked (index into the shown list).
    public event Action<int>? CardClicked;

    private (double W, double H) SlotSize => _options.CardSize switch
    {
        SwitcherCardSize.Small => (150, 90),
        SwitcherCardSize.Large => (280, 170),
        _ => (210, 126),
    };

    public void Show(IReadOnlyList<SwitcherEntry> items, IReadOnlyDictionary<nint, WindowFacts> facts, int selected, string query)
    {
        _search.Text = query.Length == 0 ? "Type to search" : query;
        _search.Foreground = query.Length == 0 ? Muted : Brushes.White;
        ClearThumbnails();
        _cards.Clear();
        _body.Children.Clear();
        if (items.Count == 0)
        {
            _body.Children.Add(new TextBlock { Text = "No matching windows.", Foreground = Muted, Margin = new Thickness(4, 6, 4, 6) });
        }
        else
        {
            // Groups (or, ungrouped, single cards) flow left to right and wrap, so the panel uses the screen's width.
            var flow = new WrapPanel();
            _body.Children.Add(flow);
            Panel? cards = null;
            string? group = null;
            for (var i = 0; i < items.Count; i++)
            {
                var entry = items[i];
                var info = facts.GetValueOrDefault(entry.Handle);
                if (cards is null || (_options.GroupByApp && entry.AppKey != group))
                {
                    group = entry.AppKey;
                    var block = new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
                    if (_options.GroupByApp) block.Children.Add(GroupHeader(entry, info, items.Count(e => e.AppKey == group)));
                    cards = new WrapPanel { MaxWidth = MaxWidth - 40 };  // an app with many windows wraps too
                    block.Children.Add(cards);
                    flow.Children.Add(block);
                    if (!_options.GroupByApp) cards = null;  // ungrouped: each card is its own block
                }
                var card = Card(i, entry, info, i == selected);
                if (cards is not null) cards.Children.Add(card);
                else ((Panel)((StackPanel)flow.Children[^1]).Children[^1]).Children.Add(card);
            }
        }
        if (!IsVisible) base.Show();
        UpdateLayout();
        Arrange();
    }

    /// Re-highlights without rebuilding (keeps the previews).
    public void Select(int selected)
    {
        var accent = Application.Current.TryFindResource("Windock.Accent") as Brush ?? Brushes.SteelBlue;
        for (var i = 0; i < _cards.Count; i++)
        {
            var on = i == selected;
            _cards[i].Card.BorderBrush = on ? accent : Brushes.Transparent;
            _cards[i].Card.Background = on ? Frozen(Color.FromRgb(0x38, 0x38, 0x38)) : CardBack;
            if (on) _cards[i].Card.BringIntoView();
        }
    }

    private UIElement GroupHeader(SwitcherEntry entry, WindowFacts? info, int count)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 8, 4, 6) };
        header.Children.Add(AppIcon(entry, info, 16));
        header.Children.Add(new TextBlock
        {
            Text = count > 1 ? $"{entry.AppName} · {count} windows" : entry.AppName, Foreground = Muted, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
        });
        return header;
    }

    private Border Card(int index, SwitcherEntry entry, WindowFacts? info, bool selected)
    {
        var (w, h) = SlotSize;
        var minimized = info?.Minimized ?? false;
        var slot = new Border { Width = w, Height = h, Background = SlotBack, CornerRadius = new CornerRadius(6) };
        if (minimized || !_options.LivePreviews)
        {
            var big = AppIcon(entry, info, Math.Min(48, h * 0.45));
            big.HorizontalAlignment = HorizontalAlignment.Center;
            big.VerticalAlignment = VerticalAlignment.Center;
            slot.Child = big;
        }
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 2, 6), Width = w };
        if (!_options.GroupByApp) title.Children.Add(AppIcon(entry, info, 14));
        title.Children.Add(new TextBlock
        {
            Text = entry.Title.Length == 0 ? entry.AppName : entry.Title, Foreground = Brushes.White, FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(_options.GroupByApp ? 0 : 6, 0, 0, 0), MaxWidth = w - 24,
        });
        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(slot);
        var card = new Border
        {
            Child = stack, Padding = new Thickness(8), Margin = new Thickness(4), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = CardBack, Cursor = Cursors.Hand,
        };
        card.MouseLeftButtonUp += (_, _) => CardClicked?.Invoke(index);
        _cards.Add((entry, card, slot, minimized));
        if (selected) Dispatcher.BeginInvoke(() => Select(index));
        return card;
    }

    private Image AppIcon(SwitcherEntry entry, WindowFacts? info, double size)
    {
        var image = new Image { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        _icons.Request(entry.AppKey, info?.ExePath, entry.Handle, IconProvider.PixelSizeFor(Math.Max(size, 32), 1.25), (_, img) => image.Source = img);
        return image;
    }

    /// Centre on the monitor where the switch started, then (re)draw the live previews.
    private void Arrange()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        if (Native.GetWindowRect(hwnd, out var r) && Native.GetMonitorInfo(_monitor, ref info))
        {
            var w = r.Right - r.Left;
            var h = r.Bottom - r.Top;
            var area = info.rcWork;
            var x = area.Left + (area.Right - area.Left - w) / 2;
            var y = area.Top + (area.Bottom - area.Top - h) / 2;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            WindowEffects.SetRoundRectRegion(hwnd, new RectD(0, 0, w, h), 14 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        }
        UpdateThumbnails(hwnd);
    }

    private void UpdateThumbnails(IntPtr hwnd)
    {
        if (!_options.LivePreviews || _cards.Count == 0) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (_thumbnails.Count == 0)
            foreach (var card in _cards) _thumbnails.Add(card.Minimized ? null : DwmThumbnail.Create(hwnd, card.Entry.Handle));
        for (var i = 0; i < _cards.Count; i++)
        {
            if (_thumbnails[i] is not { } thumb) continue;
            var slot = _cards[i].Slot;
            if (!slot.IsVisible) continue;
            var origin = slot.TransformToAncestor(this).Transform(new Point(0, 0));
            thumb.Show((int)Math.Round(origin.X * dpi), (int)Math.Round(origin.Y * dpi),
                (int)Math.Round(slot.ActualWidth * dpi), (int)Math.Round(slot.ActualHeight * dpi));
        }
    }

    private void ClearThumbnails()
    {
        foreach (var t in _thumbnails) t?.Dispose();
        _thumbnails.Clear();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.MakeToolNoActivate(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        ClearThumbnails();
        base.OnClosed(e);
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// What the switcher needs to know about a window besides its switcher entry.
internal sealed record WindowFacts(string? ExePath, bool Minimized);
