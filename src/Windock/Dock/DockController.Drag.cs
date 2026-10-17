using System.Windows;
using System.Windows.Input;
using Windock.Core.Apps;
using Windock.Core.Layout;
using Windock.Interop;

namespace Windock.Dock;

/// What a finished drag asks the app to do with the pin list. Gap indexes count pinned icons,
/// not including the one being dragged.
internal abstract record DockDrop
{
    public sealed record Reorder(DockItem Item, int GapIndex) : DockDrop;
    public sealed record PinRunning(DockItem Item, int GapIndex) : DockDrop;
    public sealed record PinFiles(IReadOnlyList<string> Paths, int GapIndex) : DockDrop;
    public sealed record Remove(DockItem Item) : DockDrop;
}

/// Drag and drop: drop programs/shortcuts from Explorer onto the dock, drag icons to reorder,
/// drag a pinned icon up out of the dock to remove it (DESIGN §5).
internal sealed partial class DockController
{
    private const double DragThreshold = 6;
    private const string GapPrefix = "__gap#";

    private DragState? _drag;
    private bool _shiftAtPress;
    private DragGhostWindow? _ghost;
    private string? _gapKey;
    private int _gapIndex = -1;
    private int _gapSerial;
    private string? _revive;              // dragged key to grow back in once the pin list is updated
    private readonly List<string> _pendingRemoved = new();

    public event Action<DockDrop>? DropCompleted;

    private sealed class DragState(DockItem item, Point start)
    {
        public DockItem Item { get; } = item;
        public Point Start { get; } = start;
        public bool Active { get; set; }
        public bool Removing { get; set; }
    }

    private void WireDragAndDrop()
    {
        var root = _window.Root;
        root.AllowDrop = true;
        root.DragEnter += OnExternalDragOver;
        root.DragOver += OnExternalDragOver;
        root.DragLeave += (_, _) => SetGap(null);
        root.Drop += OnExternalDrop;
        root.LostMouseCapture += (_, _) =>
        {
            if (_drag is { Active: true }) CancelDrag();
        };
    }

    /// Dock order as shown: the dragged pinned icon is lifted out and the gap is inserted among the pins.
    private List<string> ComposeOrder()
    {
        var keys = _model.Items.Select(i => i.Key).ToList();
        if (_drag is { Active: true, Item.IsPinned: true } d) keys.Remove(d.Item.Key);
        if (_gapKey is null) return keys;
        var pinned = keys.Where(k => _model.Find(k)?.IsPinned == true).ToList();
        var at = _gapIndex < pinned.Count ? keys.IndexOf(pinned[_gapIndex]) : pinned.Count == 0 ? 0 : keys.IndexOf(pinned[^1]) + 1;
        keys.Insert(at, _gapKey);
        return keys;
    }

    private void SyncOrder(IReadOnlyCollection<string> added, IReadOnlyCollection<string> removed)
    {
        _animator.ApplyChanges(ComposeOrder(), added, removed, _clock.Now);
        Invalidate();
    }

    private void SetGap(int? index)
    {
        if (index == (_gapKey is null ? null : _gapIndex)) return;
        var old = _gapKey;
        _gapKey = index is null ? null : GapPrefix + ++_gapSerial;
        _gapIndex = index ?? -1;
        SyncOrder(_gapKey is null ? Array.Empty<string>() : new[] { _gapKey }, old is null ? Array.Empty<string>() : new[] { old });
    }

    private int GapIndexAt(double x)
    {
        if (_layout is null) return 0;
        var centers = _layout.Items
            .Where(l => !l.Key.StartsWith(GapPrefix, StringComparison.Ordinal) && l.Key != _drag?.Item.Key && _model.Find(l.Key)?.IsPinned == true)
            .Select(l => l.Bounds.CenterX).ToList();
        return DockDropTarget.IndexFor(centers, x);
    }

    // ---- dragging an icon that is already in the dock ----

    private void OnLeftDown(Point p)
    {
        var hit = HitTest(p);
        _pressedKey = hit?.Key;
        _shiftAtPress = Native.IsShiftDown();
        _drag = hit is { Kind: DockItemKind.App } ? new DragState(hit, p) : null;
        Invalidate();
    }

    /// True when the move belongs to a drag (so normal hover handling is skipped).
    private bool OnDragMove(Point p, MouseEventArgs e)
    {
        if (_drag is null) return false;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (_drag.Active) CancelDrag();
            _drag = null;
            return false;
        }
        if (!_drag.Active)
        {
            if ((p - _drag.Start).Length < DragThreshold) return false;
            BeginDrag();
        }
        UpdateDrag(p);
        return true;
    }

    private void BeginDrag()
    {
        var drag = _drag!;
        drag.Active = true;
        _pressedKey = null;
        _hoverKey = null;
        HideTooltip();
        _cursorX = null;
        _animator.SetPointerInside(false, _clock.Now);
        _window.Root.CaptureMouse();

        _ghost = new DragGhostWindow(_iconImages.GetValueOrDefault(drag.Item.Key), _settings.Current.Appearance.IconSize * 1.15);
        _ghost.Show();
        SyncOrder(Array.Empty<string>(), drag.Item.IsPinned ? new[] { drag.Item.Key } : Array.Empty<string>());
    }

    private void UpdateDrag(Point p)
    {
        var drag = _drag!;
        var screen = _window.Root.PointToScreen(p);
        _ghost?.MoveTo(new Point(screen.X / _window.DpiScale, screen.Y / _window.DpiScale));

        var plateTop = _layout?.Plate.Y ?? 0;
        drag.Removing = drag.Item.IsPinned && DockDropTarget.IsRemoveGesture(plateTop, p.Y);
        _ghost?.SetRemoving(drag.Removing);
        // Above the dock but not far enough to remove: no gap, the icon would go back where it was.
        var overDock = p.Y >= plateTop - DockDropTarget.RemoveDistance;
        SetGap(drag.Removing || !overDock ? null : GapIndexAt(p.X));
    }

    /// Mouse released after a drag started. Returns true when the release was consumed.
    private bool FinishDrag()
    {
        if (_drag is not { Active: true } drag)
        {
            _drag = null;
            return false;
        }
        _drag = null;
        _window.Root.ReleaseMouseCapture();

        var gap = _gapIndex;
        var hadGap = _gapKey is not null;
        if (_gapKey is not null) _pendingRemoved.Add(_gapKey);
        _gapKey = null;
        _gapIndex = -1;

        DockDrop? drop = drag.Removing ? new DockDrop.Remove(drag.Item)
            : !hadGap ? null
            : drag.Item.IsPinned ? new DockDrop.Reorder(drag.Item, gap)
            : new DockDrop.PinRunning(drag.Item, gap);

        if (drag.Removing) _ghost?.Poof();
        else _ghost?.Close();
        _ghost = null;

        _revive = drag.Item.Key;
        if (drop is not null) DropCompleted?.Invoke(drop);
        FlushPendingOrder();  // nothing changed (e.g. dropped where it was): still restore the icon
        return true;
    }

    private void CancelDrag()
    {
        var drag = _drag;
        _drag = null;
        _ghost?.Close();
        _ghost = null;
        if (_gapKey is not null) _pendingRemoved.Add(_gapKey);
        _gapKey = null;
        _gapIndex = -1;
        _revive = drag?.Item.Key;
        FlushPendingOrder();
    }

    private void FlushPendingOrder()
    {
        if (_revive is null && _pendingRemoved.Count == 0) return;
        var added = _revive is not null && _model.Find(_revive) is not null ? new[] { _revive } : Array.Empty<string>();
        var removed = _pendingRemoved.ToArray();
        _revive = null;
        _pendingRemoved.Clear();
        SyncOrder(added, removed);
    }

    // ---- dropping files from Explorer / Desktop ----

    private static IReadOnlyList<string> PinnablePaths(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] files ? files.Where(PinListEditor.CanPin).ToList() : Array.Empty<string>();

    private void OnExternalDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (PinnablePaths(e.Data).Count == 0)
        {
            e.Effects = DragDropEffects.None;
            SetGap(null);
            return;
        }
        e.Effects = DragDropEffects.Link;
        SetGap(GapIndexAt(e.GetPosition(_window.Root).X));
    }

    private void OnExternalDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var paths = PinnablePaths(e.Data);
        var gap = Math.Max(0, _gapIndex);
        if (_gapKey is not null) _pendingRemoved.Add(_gapKey);
        _gapKey = null;
        _gapIndex = -1;
        if (paths.Count > 0) DropCompleted?.Invoke(new DockDrop.PinFiles(paths, gap));
        FlushPendingOrder();
    }
}
