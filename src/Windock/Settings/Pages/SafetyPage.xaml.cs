using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class SafetyPage : UserControl
{
    private readonly SettingsContext _context;
    private readonly PageSync _sync;

    public SafetyPage(SettingsContext context)
    {
        _context = context;
        InitializeComponent();
        _sync = new PageSync(this, context.Store, s => Recorder.Text = s.Safety.EmergencyHotkey);
        _ = new HotkeyRecorder(Recorder, RecorderMessage, minModifiers: 2, () => context.Store.Current.Safety.EmergencyHotkey,
            context.TryRegisterHotkey, hotkey => _sync.Edit(s => s with { Safety = new SafetySettings(hotkey) }), context.SuspendHotkey);
    }

    private void OnRestoreTaskbar(object sender, RoutedEventArgs e) =>
        _sync.Edit(s => s with { Behavior = s.Behavior with { TaskbarMode = TaskbarMode.Visible } });

    private void OnOpenData(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_context.DataDirectory}\"") { UseShellExecute = true })?.Dispose();
}
