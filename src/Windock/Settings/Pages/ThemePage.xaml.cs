using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class ThemePage : UserControl
{
    private static readonly (string Name, string Hex)[] PlatePresets =
    {
        ("Dark", "#1E1E1E"), ("Graphite", "#3A3A3C"), ("Midnight", "#1C2A44"), ("Forest", "#1E3326"), ("Wine", "#2D2240"), ("Light", "#E8E8EC"),
    };

    private static readonly (string Name, string Hex)[] DotPresets =
    {
        ("White", "#FFFFFF"), ("Gray", "#9A9A9A"), ("Blue", "#4C9AFF"), ("Black", "#202020"),
    };

    private static readonly (string Name, string Hex)[] AccentPresets =
    {
        ("Blue", "#2F6FE4"), ("Purple", "#8E5CF7"), ("Pink", "#E4407A"), ("Orange", "#F08A24"), ("Green", "#2FA84F"), ("Red", "#E5484D"),
    };

    private readonly SettingsContext _context;
    private readonly PageSync _sync;

    public ThemePage(SettingsContext context)
    {
        _context = context;
        InitializeComponent();
        AddSwatches(PlateSwatches, PlatePresets, hex => EditTheme(t => t with { PlateColor = hex, FollowWallpaper = false }));
        AddSwatches(DotSwatches, DotPresets, hex => EditTheme(t => t with { IndicatorColor = hex }));
        AddSwatches(AccentSwatches, AccentPresets, hex => EditTheme(t => t with { AccentColor = hex }));
        _sync = new PageSync(this, context.Store, Refresh);

        WireHex(PlateHex, hex => EditTheme(t => t with { PlateColor = hex, FollowWallpaper = false }));
        WireHex(DotHex, hex => EditTheme(t => t with { IndicatorColor = hex }));
        WireHex(AccentHex, hex => EditTheme(t => t with { AccentColor = hex }));
        PageSync.OnToggle(FollowWallpaper, on => EditTheme(t => t with { FollowWallpaper = on }));
        PageSync.OnToggle(Border, on => EditTheme(t => t with { ShowBorder = on }));
        Radius.ValueChanged += (_, e) => EditTheme(t => t with { CornerRadius = (int)Math.Round(e.NewValue) });
    }

    private void EditTheme(Func<ThemeSettings, ThemeSettings> change) => _sync.Edit(s => s with { Theme = change(s.Theme) });

    private void Refresh(DockSettings s)
    {
        var t = s.Theme;
        PlateHex.Text = t.PlateColor;
        PlateHex.IsEnabled = PlateSwatches.IsEnabled = !t.FollowWallpaper;
        FollowWallpaper.IsChecked = t.FollowWallpaper;
        Radius.Value = t.CornerRadius;
        Border.IsChecked = t.ShowBorder;
        DotHex.Text = t.IndicatorColor;
        AccentHex.Text = t.AccentColor;
        Mark(PlateSwatches, t.FollowWallpaper ? null : t.PlateColor);
        Mark(DotSwatches, t.IndicatorColor);
        Mark(AccentSwatches, t.AccentColor);
    }

    private static void AddSwatches(Panel panel, (string Name, string Hex)[] presets, Action<string> pick)
    {
        foreach (var (name, hex) in presets)
        {
            ThemeColor.TryParse(hex, out var c);
            var swatch = new Button
            {
                Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(0), ToolTip = name, Tag = hex,
                Cursor = Cursors.Hand, BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)),
                Template = SwatchTemplate(),
            };
            swatch.Click += (_, _) => pick(hex);
            panel.Children.Add(swatch);
        }
    }

    /// A round color dot; the border shows which one is selected.
    private static ControlTemplate SwatchTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var outer = new FrameworkElementFactory(typeof(Border));
        outer.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(13));
        outer.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        outer.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        outer.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(2));
        var inner = new FrameworkElementFactory(typeof(Border));
        inner.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(9));
        inner.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        inner.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
        inner.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
        outer.AppendChild(inner);
        template.VisualTree = outer;
        return template;
    }

    private static void Mark(Panel panel, string? selected)
    {
        foreach (var child in panel.Children.OfType<Button>())
        {
            var on = selected is not null && string.Equals((string)child.Tag, selected, StringComparison.OrdinalIgnoreCase);
            child.BorderBrush = on ? Brushes.White : Brushes.Transparent;
        }
    }

    /// Hex box: applied on Enter or when it loses focus; invalid text is reported and reverted.
    private void WireHex(TextBox box, Action<string> apply)
    {
        void Commit()
        {
            if (ThemeColor.Normalize(box.Text) is { } hex)
            {
                Message.Text = "";
                apply(hex);
            }
            else
            {
                Message.Text = $"\"{box.Text}\" is not a color. Use #RRGGBB, for example #1E1E1E.";
                Refresh(_context.Store.Current);
            }
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Commit();
        };
        box.LostKeyboardFocus += (_, _) => Commit();
    }

    private void OnReset(object sender, RoutedEventArgs e) => _sync.Edit(s => s with { Theme = ThemeSettings.Default });
}
