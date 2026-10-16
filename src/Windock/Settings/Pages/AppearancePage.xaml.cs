using System.Windows;
using System.Windows.Controls;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class AppearancePage : UserControl
{
    private readonly SettingsContext _context;
    private readonly PageSync _sync;

    public AppearancePage(SettingsContext context)
    {
        _context = context;
        InitializeComponent();
        _sync = new PageSync(this, context.Store, Refresh);

        IconSize.ValueChanged += (_, e) => Edit(a => a with { IconSize = (int)e.NewValue });
        Magnification.Checked += (_, _) => Edit(a => a with { Magnification = a.Magnification with { Enabled = true } });
        Magnification.Unchecked += (_, _) => Edit(a => a with { Magnification = a.Magnification with { Enabled = false } });
        MagnifiedSize.ValueChanged += (_, e) => Edit(a => a with { Magnification = a.Magnification with { Size = (int)e.NewValue } });
        OpacitySlider.ValueChanged += (_, e) => Edit(a => a with { BackgroundOpacity = Math.Round(e.NewValue) / 100 });
        BlurOff.Checked += (_, _) => Edit(a => a with { Blur = BlurMode.Off });
        BlurStandard.Checked += (_, _) => Edit(a => a with { Blur = BlurMode.Standard });
        Indicators.Checked += (_, _) => Edit(a => a with { ShowRunningIndicators = true });
        Indicators.Unchecked += (_, _) => Edit(a => a with { ShowRunningIndicators = false });
    }

    private void Edit(Func<AppearanceSettings, AppearanceSettings> change) =>
        _sync.Edit(s => s with { Appearance = change(s.Appearance) });

    private void Refresh(DockSettings s)
    {
        var a = s.Appearance;
        IconSize.Value = a.IconSize;
        Magnification.IsChecked = a.Magnification.Enabled;
        MagnifiedSize.Minimum = a.IconSize;
        MagnifiedSize.Value = a.Magnification.Size;
        MagnifiedSize.IsEnabled = a.Magnification.Enabled;
        OpacitySlider.Value = Math.Round(a.BackgroundOpacity * 100);
        BlurOff.IsChecked = a.Blur == BlurMode.Off;
        BlurStandard.IsChecked = a.Blur != BlurMode.Off;  // stored Acrylic is shown as standard blur (spike S1)
        Indicators.IsChecked = a.ShowRunningIndicators;
    }

    private void OnReset(object sender, RoutedEventArgs e) => PageSync.ResetToDefaults(_context.Store);
}
