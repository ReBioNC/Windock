using System.Windows;
using System.Windows.Controls;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class SwitcherPage : UserControl
{
    private readonly PageSync _sync;

    public SwitcherPage(SettingsContext context)
    {
        InitializeComponent();
        _sync = new PageSync(this, context.Store, Refresh);

        PageSync.OnToggle(Enabled, on => Edit(s => s with { Enabled = on }));
        PageSync.OnToggle(Previews, on => Edit(s => s with { LivePreviews = on }));
        PageSync.OnToggle(CurrentScreen, on => Edit(s => s with { CurrentScreenOnly = on }));
        PageSync.OnToggle(FullscreenFallback, on => Edit(s => s with { WindowsSwitcherInFullscreen = on }));
        Grouped.Checked += (_, _) => Edit(s => s with { GroupByApp = true });
        Flat.Checked += (_, _) => Edit(s => s with { GroupByApp = false });
        Small.Checked += (_, _) => Edit(s => s with { CardSize = SwitcherCardSize.Small });
        Medium.Checked += (_, _) => Edit(s => s with { CardSize = SwitcherCardSize.Medium });
        Large.Checked += (_, _) => Edit(s => s with { CardSize = SwitcherCardSize.Large });
    }

    private void Edit(Func<SwitcherSettings, SwitcherSettings> change) => _sync.Edit(s => s with { Switcher = change(s.Switcher) });

    private void Refresh(DockSettings settings)
    {
        var s = settings.Switcher;
        Enabled.IsChecked = s.Enabled;
        Options.IsEnabled = s.Enabled;
        Grouped.IsChecked = s.GroupByApp;
        Flat.IsChecked = !s.GroupByApp;
        Small.IsChecked = s.CardSize == SwitcherCardSize.Small;
        Medium.IsChecked = s.CardSize == SwitcherCardSize.Medium;
        Large.IsChecked = s.CardSize == SwitcherCardSize.Large;
        Previews.IsChecked = s.LivePreviews;
        CurrentScreen.IsChecked = s.CurrentScreenOnly;
        FullscreenFallback.IsChecked = s.WindowsSwitcherInFullscreen;
    }

    private void OnReset(object sender, RoutedEventArgs e) => _sync.Edit(s => s with { Switcher = SwitcherSettings.Default });
}
