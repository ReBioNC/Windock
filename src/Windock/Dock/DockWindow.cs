using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Windock.Interop;

namespace Windock.Dock;

/// Transparent, click-through (alpha 0), topmost, never-activated strip along the bottom of the primary screen.
internal sealed class DockWindow : Window
{
    public DockWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Windock";
        Content = Root = new Canvas { ClipToBounds = false };
        RenderOptions.SetBitmapScalingMode(Root, BitmapScalingMode.HighQuality);
    }

    public Canvas Root { get; }

    public IntPtr Handle { get; private set; }

    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// Fit to the primary screen bottom edge with the given height (DIP).
    public void Place(double height)
    {
        Width = SystemParameters.PrimaryScreenWidth;
        Height = height;
        Left = 0;
        Top = SystemParameters.PrimaryScreenHeight - height;
    }

    /// Top-left corner on screen in physical pixels.
    public (int X, int Y) PhysicalOrigin => ((int)Math.Round(Left * DpiScale), (int)Math.Round(Top * DpiScale));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        WindowEffects.MakeToolNoActivate(Handle);
    }
}
