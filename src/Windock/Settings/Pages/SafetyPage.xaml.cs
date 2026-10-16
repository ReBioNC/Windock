using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Windock.Core.Input;
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
        Recorder.PreviewKeyDown += OnRecordKey;
        // While recording, the global hotkey would swallow the keys (and quit Windock); pause it.
        Recorder.GotKeyboardFocus += (_, _) => context.SuspendHotkey(true);
        Recorder.LostKeyboardFocus += (_, _) => context.SuspendHotkey(false);
        Unloaded += (_, _) => context.SuspendHotkey(false);
    }

    private void OnRecordKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;  // wait for the non-modifier key

        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString());
        var text = string.Join("+", parts);

        var old = _context.Store.Current.Safety.EmergencyHotkey;
        if (!HotkeyGesture.TryParse(text, out var gesture) || !_context.TryRegisterHotkey(gesture.ToString()))
        {
            _context.TryRegisterHotkey(old);  // keep the previous one working
            RecorderMessage.Foreground = (System.Windows.Media.Brush)FindResource("Settings.Error");
            RecorderMessage.Text = $"Shortcut tidak valid atau sudah dipakai: {text}";
            _context.SuspendHotkey(true);  // still recording
            return;
        }
        RecorderMessage.Foreground = (System.Windows.Media.Brush)FindResource("Settings.Muted");
        RecorderMessage.Text = "Shortcut disimpan.";
        _sync.Edit(s => s with { Safety = new SafetySettings(gesture.ToString()) });
        _context.SuspendHotkey(true);  // re-registered for real when the box loses focus
    }

    private void OnRestoreTaskbar(object sender, RoutedEventArgs e) =>
        _sync.Edit(s => s with { Behavior = s.Behavior with { TaskbarMode = TaskbarMode.Visible } });

    private void OnOpenData(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_context.DataDirectory}\"") { UseShellExecute = true })?.Dispose();
}
