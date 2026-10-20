using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Windock.Interop;

namespace Windock.Dock;

/// Invisible 2 px strip along the bottom screen edge: touching it (or dragging a file onto it) reveals a hidden dock.
internal sealed class EdgeTriggerWindow : Window
{
    public EdgeTriggerWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));  // alpha 1: invisible but receives the mouse
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        Place();
    }

    public void RaiseToTop() => WindowEffects.RaiseTopmost(new WindowInteropHelper(this).Handle);

    public void Place()
    {
        Width = SystemParameters.PrimaryScreenWidth;
        Height = 2;
        Left = 0;
        Top = SystemParameters.PrimaryScreenHeight - Height;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.MakeToolNoActivate(new WindowInteropHelper(this).Handle);
    }
}
