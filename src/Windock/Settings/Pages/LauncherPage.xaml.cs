using System.Windows;
using System.Windows.Controls;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class LauncherPage : UserControl
{
    private readonly PageSync _sync;
    private readonly Action? _clearHistory;

    public LauncherPage(SettingsContext context)
    {
        InitializeComponent();
        _clearHistory = context.ClearUsageHistory;
        _sync = new PageSync(this, context.Store, Refresh);
        _ = new HotkeyRecorder(Recorder, RecorderMessage, minModifiers: 1, () => context.Store.Current.Launcher.Hotkey,
            context.TryRegisterLauncherHotkey, hotkey => Edit(l => l with { Hotkey = hotkey }), context.SuspendHotkey);

        PageSync.OnToggle(Enabled, on => Edit(l => l with { Enabled = on }));
        PageSync.OnToggle(IncludeFiles, on => Edit(l => l with { IncludeFiles = on }));
        PageSync.OnToggle(ShowCategories, on => Edit(l => l with { ShowCategories = on }));
        PageSync.OnToggle(UseWindowsHistory, on => Edit(l => l with { UseWindowsHistory = on }));
        PageSync.OnToggle(TrackLaunches, on => Edit(l => l with { TrackLaunches = on }));
        LayoutGrid.Checked += (_, _) => Edit(l => l with { Layout = LauncherLayout.Grid });
        LayoutList.Checked += (_, _) => Edit(l => l with { Layout = LauncherLayout.List });
        MaxResults.ValueChanged += (_, e) => Edit(l => l with { MaxResults = (int)Math.Round(e.NewValue) });
        Columns.ValueChanged += (_, e) => Edit(l => l with { Columns = (int)Math.Round(e.NewValue) });
        FrequentCount.ValueChanged += (_, e) => Edit(l => l with { FrequentCount = (int)Math.Round(e.NewValue) });
    }

    private void Edit(Func<LauncherSettings, LauncherSettings> change) => _sync.Edit(s => s with { Launcher = change(s.Launcher) });

    private void Refresh(DockSettings s)
    {
        var l = s.Launcher;
        Enabled.IsChecked = l.Enabled;
        Options.IsEnabled = l.Enabled;
        Recorder.Text = l.Hotkey;
        LayoutGrid.IsChecked = l.Layout == LauncherLayout.Grid;
        LayoutList.IsChecked = l.Layout == LauncherLayout.List;
        GridOptions.Visibility = l.Layout == LauncherLayout.Grid ? Visibility.Visible : Visibility.Collapsed;
        ListOptions.Visibility = l.Layout == LauncherLayout.List ? Visibility.Visible : Visibility.Collapsed;
        Columns.Value = l.Columns;
        ShowCategories.IsChecked = l.ShowCategories;
        FrequentCount.Value = l.FrequentCount;
        UseWindowsHistory.IsChecked = l.UseWindowsHistory;
        TrackLaunches.IsChecked = l.TrackLaunches;
        MaxResults.Value = l.MaxResults;
        IncludeFiles.IsChecked = l.IncludeFiles;
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        _clearHistory?.Invoke();
        ClearMessage.Text = "Cleared. Windows' own history is not touched; turn it off above to stop using it.";
    }

    /// Keeps the categories you chose; everything else goes back to its default.
    private void OnReset(object sender, RoutedEventArgs e) =>
        _sync.Edit(s => s with { Launcher = LauncherSettings.Default with { CategoryOverrides = s.Launcher.CategoryOverrides } });
}
