using System.Windows;
using System.Windows.Controls;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class LauncherPage : UserControl
{
    private readonly PageSync _sync;

    public LauncherPage(SettingsContext context)
    {
        InitializeComponent();
        _sync = new PageSync(this, context.Store, Refresh);
        _ = new HotkeyRecorder(Recorder, RecorderMessage, minModifiers: 1, () => context.Store.Current.Launcher.Hotkey,
            context.TryRegisterLauncherHotkey, hotkey => Edit(l => l with { Hotkey = hotkey }), context.SuspendHotkey);

        PageSync.OnToggle(Enabled, on => Edit(l => l with { Enabled = on }));
        PageSync.OnToggle(IncludeFiles, on => Edit(l => l with { IncludeFiles = on }));
        MaxResults.ValueChanged += (_, e) => Edit(l => l with { MaxResults = (int)Math.Round(e.NewValue) });
    }

    private void Edit(Func<LauncherSettings, LauncherSettings> change) => _sync.Edit(s => s with { Launcher = change(s.Launcher) });

    private void Refresh(DockSettings s)
    {
        var l = s.Launcher;
        Enabled.IsChecked = l.Enabled;
        Options.IsEnabled = l.Enabled;
        Recorder.Text = l.Hotkey;
        MaxResults.Value = l.MaxResults;
        IncludeFiles.IsChecked = l.IncludeFiles;
    }

    private void OnReset(object sender, RoutedEventArgs e) => _sync.Edit(s => s with { Launcher = LauncherSettings.Default });
}
