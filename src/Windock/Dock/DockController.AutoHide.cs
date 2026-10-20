using System.Windows.Media;
using Windock.Core.Animation;
using Windock.Core.Layout;

namespace Windock.Dock;

/// Sliding the dock below the screen edge for auto-hide (DESIGN §4: 250 ms, ease-in out / ease-out in).
internal sealed partial class DockController
{
    public static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(250);

    private readonly TranslateTransform _slideTransform = new();
    private Tween? _slide;
    private bool _menuOpen;

    public bool IsSlidOut { get; private set; }

    /// The pointer is on the dock or busy with it (preview, menu, drag) — auto-hide must wait.
    public bool IsPointerUsingDock => _cursorX is not null || _preview is not null || _drag is not null || _menuOpen;

    /// Raised when the pointer stops using the dock (left it, closed a preview or menu).
    public event Action? InteractionEnded;

    /// The dock's plate in screen pixels at its shown position (for "does a window cover the dock?").
    public (int Left, int Top, int Right, int Bottom) DockAreaPx
    {
        get
        {
            if (_layout is null) return default;
            var dpi = _window.DpiScale;
            var (ox, oy) = _window.PhysicalOrigin;
            var p = _layout.Plate;
            return (ox + (int)(p.X * dpi), oy + (int)(p.Y * dpi), ox + (int)(p.Right * dpi), oy + (int)((p.Bottom + DockMetrics.EdgeGap) * dpi));
        }
    }

    /// The dock window back on top of everything (see WindowEffects.RaiseTopmost).
    public void RaiseToTop() => Interop.WindowEffects.RaiseTopmost(_window.Handle);

    public void SetSlidOut(bool slidOut)
    {
        if (slidOut == IsSlidOut) return;
        var now = _clock.Now;
        var from = SlideProgress(now);
        IsSlidOut = slidOut;
        if (slidOut)
        {
            HideTooltip();
            HidePreview();
        }
        var duration = _animator.AnimationsEnabled ? SlideDuration : TimeSpan.Zero;
        _slide = new Tween(from, slidOut ? 1 : 0, now, duration, slidOut ? Easing.InCubic : Easing.OutCubic);
        Invalidate();
    }

    public void SetMenuOpen(bool open)
    {
        _menuOpen = open;
        if (!open) InteractionEnded?.Invoke();
    }

    private double SlideProgress(TimeSpan now) => _slide?.ValueAt(now) ?? (IsSlidOut ? 1 : 0);

    private bool IsSliding(TimeSpan now) => _slide is not null && !_slide.IsComplete(now);

    /// Moves canvas and blur down by the plate height times the slide progress.
    private void ApplySlide(TimeSpan now)
    {
        if (_layout is null) return;
        var progress = SlideProgress(now);
        var offset = progress * (_layout.Plate.Height + DockMetrics.EdgeGap + 2);
        if (_window.Root.RenderTransform != _slideTransform) _window.Root.RenderTransform = _slideTransform;
        _slideTransform.Y = offset;

        if (progress >= 1)
        {
            _backdrop.Hide();
            return;
        }
        var origin = _window.PhysicalOrigin;
        _backdrop.Update(_layout.Plate with { Y = _layout.Plate.Y + offset }, _window.DpiScale, origin.X, origin.Y, _window.Handle);
    }
}
