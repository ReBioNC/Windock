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
internal sealed class DockController
{
    private const string RecycleBinParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    private readonly DockModel _model;
    private readonly DockAnimator _animator = new();
    private readonly IClock _clock;
    private readonly SettingsStore _settings;
    private readonly IconProvider _icons;
    private readonly DockWindow _window;
    private readonly BackdropWindow _backdrop;
    private readonly DockRenderer _renderer;
    private readonly ILog _log;
    private readonly Dictionary<string, ImageSource> _iconImages = new();
    private readonly Dictionary<string, DockItem> _lastKnown = new();  // keeps leaving (ghost) items drawable
    private readonly DispatcherTimer _tooltipTimer;

    private LayoutResult? _layout;
    private double? _cursorX;
    private bool _renderHooked;
    private bool _dirty;
    private bool _hidden;
    private string? _hoverKey;
    private string? _pressedKey;
    private int _iconPixels;

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
    }

    /// Left click released on an item (item, shift held).
    public event Action<DockItem, bool>? ItemClicked;

    /// Right click on an item (null = empty plate area).
    public event Action<DockItem?>? ContextRequested;

    public IntPtr WindowHandle => _window.Handle;

    public UIElement MenuAnchor => _window.Root;

    public void Start()
    {
        var appearance = _settings.Current.Appearance;
        _window.Place(DockMetrics.WindowHeight(appearance.IconSize, appearance.Magnification.Size));
        _window.Show();
        _backdrop.SetBlur(appearance.Blur);
        _animator.AnimationsEnabled = SystemParameters.ClientAreaAnimation;
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) _animator.AnimationsEnabled = SystemParameters.ClientAreaAnimation;
        };

        _animator.Reset(_model.Items.Select(i => i.Key).ToList());
        RefreshIcons();
        _model.ItemsChanged += OnItemsChanged;

        var root = _window.Root;
        root.MouseMove += (_, e) => OnPointer(e.GetPosition(root));
        root.MouseLeave += (_, _) => OnPointerLeft();
        root.MouseLeftButtonDown += (_, e) => { _pressedKey = HitTest(e.GetPosition(root))?.Key; Invalidate(); };
        root.MouseLeftButtonUp += (_, e) => OnLeftUp(e.GetPosition(root));
        root.MouseRightButtonUp += (_, e) => { HideTooltip(); ContextRequested?.Invoke(HitTest(e.GetPosition(root))); e.Handled = true; };

        Invalidate();
    }

    public void ApplySettings(SettingsChangedEventArgs e)
    {
        if (e.Sections.HasFlag(SettingsSection.Pins)) _model.SetPins(e.New.Pins);
        if (!e.Sections.HasFlag(SettingsSection.Appearance)) return;

        var a = e.New.Appearance;
        _window.Place(DockMetrics.WindowHeight(a.IconSize, a.Magnification.Size));
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
            _window.Hide();
            _backdrop.Hide();
        }
        else
        {
            _window.Show();
            Invalidate();
        }
    }

    private void OnItemsChanged(object? sender, DockItemsChangedEventArgs e)
    {
        _animator.ApplyChanges(_model.Items.Select(i => i.Key).ToList(), e.Added, e.Removed, _clock.Now);
        foreach (var key in e.Added.Concat(e.Updated)) RequestIcon(key);
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
        }
        foreach (var item in _model.Items) RequestIcon(item.Key);
    }

    private void RequestIcon(string key)
    {
        if (_model.Find(key) is not { } item || item.Kind == DockItemKind.Separator) return;
        _lastKnown[key] = item;
        if (_iconImages.ContainsKey(key)) return;
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
            if (hover is not null) _tooltipTimer.Start();
        }
        Invalidate();
    }

    private void OnPointerLeft()
    {
        _cursorX = null;
        _hoverKey = null;
        _pressedKey = null;
        HideTooltip();
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
            ItemClicked?.Invoke(hit, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
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

    private void HideTooltip()
    {
        _tooltipTimer.Stop();
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
        try
        {
            var now = _clock.Now;
            RenderFrame(now);
            if (!_animator.IsAnimating(now) && !_dirty)
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
            key => _model.Find(key)?.IsRunning ?? false, _pressedKey);

        var origin = _window.PhysicalOrigin;
        _backdrop.Update(_layout.Plate, _window.DpiScale, origin.X, origin.Y, _window.Handle);
    }
}
