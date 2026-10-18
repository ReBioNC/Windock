using System.Windows;
using System.Windows.Input;
using Windock.Core.Layout;
using Windock.Status;

namespace Windock.Dock;

/// The status area at the right end of the plate: fixed-size buttons that never magnify.
internal sealed partial class DockController
{
    private const string StatusSeparatorKey = "sep:status";
    private StatusArea? _status;

    /// Left click on a status button: key and the button's anchor (screen DIP: centre x, top y).
    public event Action<string, Point>? StatusClicked;

    public void AttachStatus(StatusArea status)
    {
        _status = status;
        status.Changed += Invalidate;
        var root = _window.Root;
        root.MouseWheel += (_, e) =>
        {
            if (StatusAt(e.GetPosition(root)) is not { Key: StatusArea.Volume }) return;
            status.Audio.Step(e.Delta > 0 ? 0.02 : -0.02);
            e.Handled = true;
        };
        root.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle || StatusAt(e.GetPosition(root)) is not { Key: StatusArea.Volume }) return;
            status.Audio.ToggleMute();
            e.Handled = true;
        };
        Invalidate();
    }

    private IEnumerable<LayoutItem> StatusLayoutItems(double iconSize)
    {
        if (_status is null || _status.Buttons.Count == 0) yield break;
        yield return new LayoutItem(StatusSeparatorKey, LayoutItemKind.Separator, 1);
        foreach (var b in _status.Buttons) yield return new LayoutItem(b.Key, LayoutItemKind.Fixed, 1, _renderer.MeasureStatus(b, iconSize));
    }

    private StatusButton? StatusButtonFor(string? key) =>
        key is null || _status is null ? null : _status.Buttons.FirstOrDefault(b => b.Key == key);

    private StatusButton? StatusAt(Point p)
    {
        if (_layout is null) return null;
        foreach (var l in _layout.Items)
        {
            if (l.Kind != LayoutItemKind.Fixed) continue;
            var half = (l.Bounds.Width + DockMetrics.FixedSpacing) / 2;
            if (p.X >= l.Bounds.CenterX - half && p.X < l.Bounds.CenterX + half && p.Y >= l.Bounds.Y - 4) return StatusButtonFor(l.Key);
        }
        return null;
    }

    private Point StatusAnchor(string key)
    {
        var bounds = _layout?.Items.FirstOrDefault(l => l.Key == key).Bounds ?? default;
        var plateTop = _layout?.Plate.Y ?? bounds.Y;
        return new Point(_window.Left + bounds.CenterX, _window.Top + plateTop);
    }

    private void RaiseStatusClicked(string key) => StatusClicked?.Invoke(key, StatusAnchor(key));
}
