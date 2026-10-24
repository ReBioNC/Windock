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
        HideNever.Checked += (_, _) => SetAutoHide(DockAutoHide.Never);
        HideWhenCovered.Checked += (_, _) => SetAutoHide(DockAutoHide.WhenCovered);
        HideAlways.Checked += (_, _) => SetAutoHide(DockAutoHide.Always);
        PageSync.OnToggle(Bounce, on => _sync.Edit(s => s with { Behavior = s.Behavior with { BounceOnLaunch = on } }));
        PageSync.OnToggle(Startup, on => _sync.Edit(s => s with { Behavior = s.Behavior with { StartWithWindows = on } }));
        AttentionBounce.Checked += (_, _) => SetAttention(AttentionStyle.Bounce);
        AttentionDot.Checked += (_, _) => SetAttention(AttentionStyle.Dot);
        AttentionOff.Checked += (_, _) => SetAttention(AttentionStyle.Off);
        RepeatUntilOpened.Checked += (_, _) => SetRepeat(AttentionRepeat.UntilOpened);
        RepeatOnce.Checked += (_, _) => SetRepeat(AttentionRepeat.Once);
        PageSync.OnToggle(BadgeCounts, on => _sync.Edit(s => s with { Behavior = s.Behavior with { ShowBadgeCounts = on } }));
        PageSync.OnToggle(FromNotifications, on => _sync.Edit(s => s with { Behavior = s.Behavior with { AttentionFromNotifications = on } }));
    }

    private void SetAttention(AttentionStyle style) => _sync.Edit(s => s with { Behavior = s.Behavior with { Attention = style } });

    private void SetRepeat(AttentionRepeat repeat) => _sync.Edit(s => s with { Behavior = s.Behavior with { AttentionRepeat = repeat } });

    private void SetAutoHide(DockAutoHide mode) => _sync.Edit(s => s with { Behavior = s.Behavior with { AutoHide = mode } });

    private void SetMode(TaskbarMode mode) => _sync.Edit(s => s with { Behavior = s.Behavior with { TaskbarMode = mode } });

    private void Refresh(DockSettings s)
    {
        Hidden.IsChecked = s.Behavior.TaskbarMode == TaskbarMode.Hidden;
        AutoHide.IsChecked = s.Behavior.TaskbarMode == TaskbarMode.AutoHide;
        Visible.IsChecked = s.Behavior.TaskbarMode == TaskbarMode.Visible;
        HideNever.IsChecked = s.Behavior.AutoHide == DockAutoHide.Never;
        HideWhenCovered.IsChecked = s.Behavior.AutoHide == DockAutoHide.WhenCovered;
        HideAlways.IsChecked = s.Behavior.AutoHide == DockAutoHide.Always;
        Bounce.IsChecked = s.Behavior.BounceOnLaunch;
        Startup.IsChecked = s.Behavior.StartWithWindows;
        AttentionBounce.IsChecked = s.Behavior.Attention == AttentionStyle.Bounce;
        AttentionDot.IsChecked = s.Behavior.Attention == AttentionStyle.Dot;
        AttentionOff.IsChecked = s.Behavior.Attention == AttentionStyle.Off;
        RepeatUntilOpened.IsChecked = s.Behavior.AttentionRepeat == AttentionRepeat.UntilOpened;
        RepeatOnce.IsChecked = s.Behavior.AttentionRepeat == AttentionRepeat.Once;
        RepeatUntilOpened.IsEnabled = RepeatOnce.IsEnabled = s.Behavior.Attention != AttentionStyle.Off;
        FromNotifications.IsChecked = s.Behavior.AttentionFromNotifications;
        FromNotifications.IsEnabled = s.Behavior.Attention != AttentionStyle.Off;
        BadgeCounts.IsChecked = s.Behavior.ShowBadgeCounts;
        BadgeCounts.IsEnabled = s.Behavior.Attention != AttentionStyle.Off && s.Behavior.AttentionFromNotifications;
        HiddenHint.Text = s.Behavior.TaskbarMode == TaskbarMode.Hidden
            ? $"Emergency shortcut: {s.Safety.EmergencyHotkey}. Press it to quit Windock and get the taskbar back."
            : "";
    }

    private void OnReset(object sender, RoutedEventArgs e) => PageSync.ResetToDefaults(_context.Store);
}
