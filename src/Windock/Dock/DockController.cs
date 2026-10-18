using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windock.Core.Abstractions;
using Windock.Core.Animation;
using Windock.Core.Apps;
using Windock.Core.Layout;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Interop;

namespace Windock.Dock;

/// Connects model → animator → layout → renderer, and turns pointer input into dock events.
/// The render loop only runs while something moves; an idle dock costs no CPU (N-02).
internal sealed partial class DockController
{
    private const string RecycleBinParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    private readonly DockModel _model;
    private readonly DockAnimator _animator = new();
    private readonly BounceAnimator _bounce = new();
    private readonly Dictionary<string, int> _launchWindows = new();  // bouncing launches: window count at click
    private readonly IClock _clock;
    private readonly SettingsStore _settings;
    private readonly IconProvider _icons;
    private readonly DockWindow _window;
    private readonly BackdropWindow _backdrop;
    private readonly DockRenderer _renderer;
    private readonly ILog _log;
    private readonly Dictionary<string, ImageSource> _iconImages = new();
    private readonly HashSet<string> _iconRequested = new();
    private readonly Dictionary<string, DockItem> _lastKnown = new();  // keeps leaving (ghost) items drawable
    private readonly DispatcherTimer _tooltipTimer;
    private readonly DispatcherTimer _previewTimer;

    /// Hover time before window previews appear (the name tooltip shows sooner).
    public static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(700);
    private readonly DispatcherTimer _previewHideTimer;
    private WindowPreviewPopup? _preview;

    private LayoutResult? _layout;
    private double? _cursorX;
    private bool _renderHooked;
    private bool _dirty;
    private bool _hidden;
    private string? _hoverKey;
    private string? _pressedKey;
    private int _iconPixels;
    private double? _bottomEdge;

    public DockController(DockModel model, IClock clock, SettingsStore settings, IconProvider icons, DockWindow window, BackdropWindow backdrop, ILog log)
    {
        _model = model;
        _clock = clock;
        _settings = settings;
        _icons = icons;
        _window = window;
        _backdrop = backdrop;
        _log = log;
        _renderer = new DockRenderer(window.Root);
        _tooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _tooltipTimer.Tick += (_, _) => { _tooltipTimer.Stop(); ShowTooltip(); };
        _previewTimer = new DispatcherTimer { Interval = PreviewDelay };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); ShowHoverInfo(); };
        _previewHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _previewHideTimer.Tick += (_, _) =>
        {
            _previewHideTimer.Stop();
            if (_preview?.ContainsPointer != true) HidePreview();
        };
    }

    /// Left click released on an item (item, shift held).
    public event Action<DockItem, bool>? ItemClicked;

    /// Right click on an item (null = empty plate area).
    public event Action<DockItem?>? ContextRequested;

    public IntPtr WindowHandle => _window.Handle;

    public UIElement MenuAnchor => _window.Root;

    public double DpiScale => _window.DpiScale;

    public void SetBottomEdge(double bottomDip)
    {
        if (_bottomEdge == bottomDip) return;
        _bottomEdge = bottomDip;
        Relayout();
    }

    /// After resolution or DPI changes: refit the window to the screen edge.
    public void Relayout()
    {
        var a = _settings.Current.Appearance;
        _window.Place(DockMetrics.WindowHeight(a.IconSize, a.Magnification.Size), _bottomEdge);
        RefreshIcons();
        Invalidate();
    }

    public void Start()
    {
        var appearance = _settings.Current.Appearance;
        _window.Place(DockMetrics.WindowHeight(appearance.IconSize, appearance.Magnification.Size), _bottomEdge);
        if (!_hidden) _window.Show();
        _backdrop.SetBlur(appearance.Blur);
        _animator.AnimationsEnabled = _bounce.AnimationsEnabled = SystemParameters.ClientAreaAnimation;
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                _animator.AnimationsEnabled = _bounce.AnimationsEnabled = SystemParameters.ClientAreaAnimation;
        };

        _animator.Reset(_model.Items.Select(i => i.Key).ToList());
        RefreshIcons();
        _model.ItemsChanged += OnItemsChanged;

        var root = _window.Root;
        root.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(root);
            if (!OnDragMove(p, e)) OnPointer(p);
        };
        root.MouseLeave += (_, _) =>
        {
            if (_drag is not { Active: true }) OnPointerLeft();
        };
        root.MouseLeftButtonDown += (_, e) => OnLeftDown(e.GetPosition(root));
        root.MouseLeftButtonUp += (_, e) =>
        {
            if (!FinishDrag()) OnLeftUp(e.GetPosition(root));
        };
        WireDragAndDrop();
        root.MouseRightButtonUp += (_, e) => { HideTooltip(); ContextRequested?.Invoke(HitTest(e.GetPosition(root))); e.Handled = true; };

        Invalidate();
    }

    public void ApplySettings(SettingsChangedEventArgs e)
    {
        if (e.Sections.HasFlag(SettingsSection.Pins)) _model.SetPins(e.New.Pins);
        if (e.Sections.HasFlag(SettingsSection.HiddenApps)) _model.SetHiddenApps(e.New.HiddenApps.Select(h => h.AppId));
        if (e.Sections.HasFlag(SettingsSection.Trash)) _model.SetShowRecycleBin(e.New.Trash.ShowRecycleBin);
        if (!e.Sections.HasFlag(SettingsSection.Appearance)) return;

        var a = e.New.Appearance;
        _window.Place(DockMetrics.WindowHeight(a.IconSize, a.Magnification.Size), _bottomEdge);
        if (a.Blur != e.Old.Appearance.Blur) _backdrop.SetBlur(a.Blur);
        RefreshIcons();
        Invalidate();
    }

    /// Fullscreen apps and Pause: hide instantly, no animation.
    public void SetHidden(bool hidden)
    {
        if (_hidden == hidden) return;
        _hidden = hidden;
        if (hidden)
        {
            HideTooltip();
            HidePreview();
            _window.Hide();
            _backdrop.Hide();
        }
        else
        {
            _window.Show();
            Invalidate();
        }
    }

    /// Bounces an icon while its app starts: until a new window shows up or LaunchTimeout passes.
    public void BounceLaunch(string key)
    {
        if (!_settings.Current.Behavior.BounceOnLaunch || _model.Find(key) is not { } item) return;
        _launchWindows[key] = item.Windows.Count;
        _bounce.Start(key, _clock.Now, BounceAnimator.LaunchTimeout);
        Invalidate();
    }

    /// Bounces an icon for `limit` (attention requests); Stop lands it.
    public void Bounce(string key, TimeSpan limit)
    {
        _bounce.Start(key, _clock.Now, limit);
        Invalidate();
    }

    public void StopBounce(string key)
    {
        _bounce.Stop(key, _clock.Now);
        Invalidate();
    }

    private void StopLaunchBounces()
    {
        foreach (var (key, count) in _launchWindows.ToList())
        {
            if (_model.Find(key) is { } item && item.Windows.Count <= count) continue;
            _launchWindows.Remove(key);
            _bounce.Stop(key, _clock.Now);
        }
    }

    private void OnItemsChanged(object? sender, DockItemsChangedEventArgs e)
    {
        StopLaunchBounces();
        var added = e.Added.ToList();
        if (_revive is not null && _model.Find(_revive) is not null && !added.Contains(_revive)) added.Add(_revive);
        var removed = e.Removed.Concat(_pendingRemoved).ToList();
        _revive = null;
        _pendingRemoved.Clear();
        _animator.ApplyChanges(ComposeOrder(), added, removed, _clock.Now);
        foreach (var key in e.Added.Concat(e.Updated)) RequestIcon(key);
        if (_preview is not null && (e.Updated.Contains(_preview.AppKey) || e.Removed.Contains(_preview.AppKey))) RefreshPreview();
        Invalidate();
    }

    private void RefreshIcons()
    {
        var a = _settings.Current.Appearance;
        var px = IconProvider.PixelSizeFor(a.Magnification.Enabled ? a.Magnification.Size : a.IconSize, _window.DpiScale);
        if (px != _iconPixels)
        {
            _iconPixels = px;
            _iconImages.Clear();
            _iconRequested.Clear();
        }
        foreach (var item in _model.Items) RequestIcon(item.Key);
    }

    private void RequestIcon(string key)
    {
        if (_model.Find(key) is not { } item || item.Kind == DockItemKind.Separator) return;
        _lastKnown[key] = item;
        if (!_iconRequested.Add(key)) return;
        var path = item.Kind == DockItemKind.RecycleBin ? RecycleBinParsingName : item.IconPath;
        var hwnd = item.Windows.Count > 0 ? item.Windows[0].Handle : IntPtr.Zero;
        _icons.Request(key, path, hwnd, _iconPixels, (k, image) =>
        {
            _iconImages[k] = image;
            Invalidate();
        });
    }

    private void OnPointer(Point p)
    {
        if (_layout is null) return;
        _cursorX = p.X;
        _animator.SetPointerInside(true, _clock.Now);
        var hover = HitTest(p)?.Key;
        if (hover != _hoverKey)
        {
            _hoverKey = hover;
            HideTooltip();
            if (_preview is not null)
            {
                // Like the taskbar: once previews are open, moving to another running app swaps them at once.
                if (hover is not null && _model.Find(hover) is { IsRunning: true } next) ShowPreview(next);
                else _previewHideTimer.Start();
            }
            else if (hover is not null)
            {
                _tooltipTimer.Start();
                if (_model.Find(hover) is { IsRunning: true }) _previewTimer.Start();
            }
        }
        Invalidate();
    }

    private void OnPointerLeft()
    {
        _cursorX = null;
        _hoverKey = null;
        _pressedKey = null;
        HideTooltip();
        if (_preview is not null) _previewHideTimer.Start();
        InteractionEnded?.Invoke();
        _animator.SetPointerInside(false, _clock.Now);
        Invalidate();
    }

    private void OnLeftUp(Point p)
    {
        var hit = HitTest(p);
        var pressed = _pressedKey;
        _pressedKey = null;
        HideTooltip();
        Invalidate();
        if (hit is not null && hit.Key == pressed)
            ItemClicked?.Invoke(hit, _shiftAtPress || Native.IsShiftDown());
    }

    private DockItem? HitTest(Point p)
    {
        if (_layout is null) return null;
        foreach (var l in _layout.Items)
        {
            if (l.Kind != LayoutItemKind.Icon) continue;
            // Whole slot column counts, so the gaps between icons are not dead zones.
            var half = (l.Bounds.Width + DockMetrics.IconSpacing) / 2;
            if (p.X >= l.Bounds.CenterX - half && p.X < l.Bounds.CenterX + half && p.Y >= l.Bounds.Y)
                return _model.Find(l.Key);
        }
        return null;
    }

    private void ShowTooltip()
    {
        if (_hoverKey is null || _layout is null || _model.Find(_hoverKey) is not { } item) return;
        var bounds = _layout.Items.FirstOrDefault(l => l.Key == _hoverKey).Bounds;
        _renderer.ShowTooltip(item.DisplayName, bounds);
    }

    /// Window previews for a running app (title, live thumbnail, ✕) after PreviewDelay of hovering.
    private void ShowHoverInfo()
    {
        if (_hoverKey is not null && _model.Find(_hoverKey) is { IsRunning: true } item) ShowPreview(item);
    }

    public event Action<IntPtr>? PreviewWindowChosen;
    public event Action<IntPtr>? PreviewCloseRequested;

    public void ShowPreview(DockItem item)
    {
        if (_layout is null || !item.IsRunning) return;
        HideTooltip();
        _previewHideTimer.Stop();
        var bounds = _layout.Items.FirstOrDefault(l => l.Key == item.Key).Bounds;
        var anchor = new Point(_window.Left + bounds.CenterX, _window.Top + Math.Min(bounds.Y, _layout.Plate.Y));
        if (_preview is not null && _preview.AppKey != item.Key) HidePreview();
        if (_preview is null)
        {
            _preview = new WindowPreviewPopup(item.Key, _iconImages.GetValueOrDefault(item.Key));
            _preview.MouseEnter += (_, _) => _previewHideTimer.Stop();
            _preview.MouseLeave += (_, _) => _previewHideTimer.Start();
            _preview.WindowChosen += hwnd =>
            {
                HidePreview();
                PreviewWindowChosen?.Invoke(hwnd);
            };
            _preview.CloseRequested += hwnd => PreviewCloseRequested?.Invoke(hwnd);
        }
        _preview.ShowFor(item.Windows, anchor, _window.Width);
    }

    public void HidePreview()
    {
        _previewHideTimer.Stop();
        if (_preview is null) return;
        _preview.Close();
        _preview = null;
        InteractionEnded?.Invoke();
    }

    private void RefreshPreview()
    {
        if (_preview is null) return;
        if (_model.Find(_preview.AppKey) is { IsRunning: true } item) ShowPreview(item);
        else HidePreview();
    }

    private void HideTooltip()
    {
        _tooltipTimer.Stop();
        _previewTimer.Stop();
        _renderer.HideTooltip();
    }

    private void Invalidate()
    {
        _dirty = true;
        if (_renderHooked || _hidden) return;
        CompositionTarget.Rendering += OnRendering;
        _renderHooked = true;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_hidden)
        {
            // Hidden (fullscreen app / Mode Windows): a frame now would re-show the blur window.
            CompositionTarget.Rendering -= OnRendering;
            _renderHooked = false;
            return;
        }
        try
        {
            var now = _clock.Now;
            RenderFrame(now);
            if (!_animator.IsAnimating(now) && !_bounce.IsAnimating(now) && !IsSliding(now) && !_dirty)
            {
                CompositionTarget.Rendering -= OnRendering;
                _renderHooked = false;
            }
        }
        catch (Exception ex)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderHooked = false;
            _log.Error("dock render failed", ex);
        }
    }

    private void RenderFrame(TimeSpan now)
    {
        _dirty = false;
        var settings = _settings.Current.Appearance;
        var items = _animator.Slots(now).Select(s =>
        {
            var item = _model.Find(s.Key) ?? _lastKnown.GetValueOrDefault(s.Key);
            var kind = item?.Kind == DockItemKind.Separator || s.Key.StartsWith("sep:", StringComparison.Ordinal) ? LayoutItemKind.Separator : LayoutItemKind.Icon;
            return new LayoutItem(s.Key, kind, s.SlotProgress);
        }).ToList();

        _layout = DockLayoutEngine.Compute(new LayoutInput(items, settings.IconSize, settings.Magnification.Size,
            settings.Magnification.Enabled, _cursorX, _animator.MagnifyProgress(now), _window.Width, _window.Height));

        _renderer.Render(_layout, settings, key => _iconImages.GetValueOrDefault(key),
            key => _model.Find(key)?.Windows.Count ?? 0, _pressedKey,
            key => _bounce.Lift(key, now) * BounceAnimator.HeightFactor * settings.IconSize);

        ApplySlide(now);
    }
}
