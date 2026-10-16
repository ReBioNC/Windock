using System.Windows;
using System.Windows.Controls;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class BehaviorPage : UserControl
{
    private readonly SettingsContext _context;
    private readonly PageSync _sync;

    public BehaviorPage(SettingsContext context)
    {
        _context = context;
        InitializeComponent();
        _sync = new PageSync(this, context.Store, Refresh);

        Hidden.Checked += (_, _) => SetMode(TaskbarMode.Hidden);
        AutoHide.Checked += (_, _) => SetMode(TaskbarMode.AutoHide);
        Visible.Checked += (_, _) => SetMode(TaskbarMode.Visible);
    }

    private void SetMode(TaskbarMode mode) => _sync.Edit(s => s with { Behavior = s.Behavior with { TaskbarMode = mode } });

    private void Refresh(DockSettings s)
    {
        Hidden.IsChecked = s.Behavior.TaskbarMode == TaskbarMode.Hidden;
        AutoHide.IsChecked = s.Behavior.TaskbarMode == TaskbarMode.AutoHide;
        Visible.IsChecked = s.Behavior.TaskbarMode == TaskbarMode.Visible;
        HiddenHint.Text = s.Behavior.TaskbarMode == TaskbarMode.Hidden
            ? $"Shortcut darurat: {s.Safety.EmergencyHotkey}. Tekan untuk mematikan Windock dan mengembalikan taskbar."
            : "";
    }

    private void OnReset(object sender, RoutedEventArgs e) => PageSync.ResetToDefaults(_context.Store);
}
