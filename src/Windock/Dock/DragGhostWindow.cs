using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Windock.Interop;

namespace Windock.Dock;

/// The icon that follows the cursor while an app is dragged out of or around the dock.
/// Click-through (WS_EX_TRANSPARENT) so it never gets in the way of the drop.
internal sealed class DragGhostWindow : Window
{
    private const long WS_EX_TRANSPARENT = 0x20;
    private readonly Image _image;
    private readonly Border _label;

    public DragGhostWindow(ImageSource? icon, double size)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        _image = new Image { Source = icon, Width = size, Height = size, Opacity = 0.85 };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x2A, 0x2A, 0x2A)), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = "Hapus dari Dock", Foreground = Brushes.White, FontSize = 12 },
            Visibility = Visibility.Hidden,
        };
        Content = new StackPanel { Children = { _image, _label }, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
    }

    /// Centers the icon on a screen point given in DIP.
    public void MoveTo(Point screenDip)
    {
        Left = screenDip.X - _image.Width / 2;
        Top = screenDip.Y - _image.Height / 2;
    }

    public void SetRemoving(bool removing)
    {
        _image.Opacity = removing ? 0.5 : 0.85;
        _label.Visibility = removing ? Visibility.Visible : Visibility.Hidden;
    }

    /// Removed from the dock: grow and fade out, then close.
    public void Poof()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(220));
        var fade = new DoubleAnimation(0, duration);
        fade.Completed += (_, _) => Close();
        var scale = (ScaleTransform)((StackPanel)Content).RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.4, duration));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.4, duration));
        BeginAnimation(OpacityProperty, fade);
    }
}
