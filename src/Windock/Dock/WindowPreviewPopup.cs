using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Windock.Core.Abstractions;
using Windock.Interop;

namespace Windock.Dock;

/// Live previews of an app's open windows above its dock icon, like the Windows taskbar:
/// title, live thumbnail and a close button per window; click a card to switch to that window.
internal sealed class WindowPreviewPopup : Window
{
    private const double CardWidth = 220, ThumbHeight = 124;

    private readonly ImageSource? _icon;
    private readonly List<(WindowInfo Window, Border Slot)> _cards = new();
    private readonly List<DwmThumbnail?> _thumbnails = new();
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private Point _anchor;          // screen DIP: centre of the icon (x) and top of the icon (y)
    private double _screenWidth;

    public WindowPreviewPopup(string appKey, ImageSource? icon)
    {
        AppKey = appKey;
        _icon = icon;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        Content = new Border { Padding = new Thickness(6), Child = _row };
        SizeChanged += (_, _) => { Reposition(); UpdateThumbnails(); };
    }

    public string AppKey { get; }

    /// Click on a card.
    public event Action<IntPtr>? WindowChosen;

    /// Click on a card's ✕.
    public event Action<IntPtr>? CloseRequested;

    public void ShowFor(IReadOnlyList<WindowInfo> windows, Point anchorScreenDip, double screenWidthDip)
    {
        _anchor = anchorScreenDip;
        _screenWidth = screenWidthDip;
        Fill(windows);
        if (!IsVisible) Show();
        UpdateThumbnails();
    }

    public bool ContainsPointer => IsVisible && IsMouseOver;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.AddExStyle(new WindowInteropHelper(this).Handle, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        WindowEffects.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        ClearThumbnails();
        base.OnClosed(e);
    }

    private void Fill(IReadOnlyList<WindowInfo> windows)
    {
        ClearThumbnails();
        _cards.Clear();
        _row.Children.Clear();
        foreach (var window in windows.Take(8))
        {
            var slot = new Border { Height = ThumbHeight, Margin = new Thickness(0, 6, 0, 0) };
            if (window.IsMinimized && _icon is not null)
                slot.Child = new Image { Source = _icon, Width = 48, Height = 48, Opacity = 0.9 };  // DWM shows nothing for minimized windows

            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(window.Title) ? window.DisplayName : window.Title,
                Foreground = Brushes.White, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            };
            var close = new Button
            {
                Content = "✕", Width = 24, Height = 22, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0),
                Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                ToolTip = "Tutup jendela",
            };
            var hwnd = window.Handle;
            close.Click += (_, e) => { e.Handled = true; CloseRequested?.Invoke(hwnd); };

            var header = new DockPanel();
            DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
            header.Children.Add(close);
            if (_icon is not null)
            {
                var small = new Image { Source = _icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) };
                DockPanel.SetDock(small, System.Windows.Controls.Dock.Left);
                header.Children.Add(small);
            }
            header.Children.Add(title);

            var card = new Border
            {
                Width = CardWidth, Padding = new Thickness(8), Margin = new Thickness(2), CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new StackPanel { Children = { header, slot } },
            };
            card.MouseEnter += (_, _) => card.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
            card.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && IsInside(d, close)) return;
                WindowChosen?.Invoke(hwnd);
            };
            _row.Children.Add(card);
            _cards.Add((window, slot));
        }
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current == container) return true;
        return false;
    }

    private void Reposition()
    {
        var left = _anchor.X - ActualWidth / 2;
        Left = Math.Clamp(left, 8, Math.Max(8, _screenWidth - ActualWidth - 8));
        Top = _anchor.Y - ActualHeight - 10;
    }

    /// DWM thumbnails are positioned in physical pixels relative to this window's client area.
    private void UpdateThumbnails()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _cards.Count == 0) return;
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (_thumbnails.Count == 0)
        {
            foreach (var (window, _) in _cards)
                _thumbnails.Add(window.IsMinimized ? null : DwmThumbnail.Create(hwnd, window.Handle));
        }
        for (var i = 0; i < _cards.Count; i++)
        {
            if (_thumbnails[i] is not { } thumb) continue;
            var slot = _cards[i].Slot;
            var origin = slot.TransformToAncestor(this).Transform(new Point(0, 0));
            thumb.Show((int)Math.Round(origin.X * dpi), (int)Math.Round(origin.Y * dpi),
                (int)Math.Round(slot.ActualWidth * dpi), (int)Math.Round(slot.ActualHeight * dpi));
        }
    }

    private void ClearThumbnails()
    {
        foreach (var thumb in _thumbnails) thumb?.Dispose();
        _thumbnails.Clear();
    }
}
