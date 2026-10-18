using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Windock.Core.Input;

namespace Windock.Settings.Pages;

/// A read-only box that records a key combination. While it has focus every global hotkey is paused,
/// so pressing the current combination does not trigger it.
internal sealed class HotkeyRecorder
{
    private readonly TextBox _box;
    private readonly TextBlock _message;
    private readonly int _minModifiers;
    private readonly Func<string> _current;
    private readonly Func<string, bool> _tryRegister;
    private readonly Action<string> _save;
    private readonly Action<bool> _suspend;

    public HotkeyRecorder(TextBox box, TextBlock message, int minModifiers, Func<string> current, Func<string, bool> tryRegister,
        Action<string> save, Action<bool> suspend)
    {
        _box = box;
        _message = message;
        _minModifiers = minModifiers;
        _current = current;
        _tryRegister = tryRegister;
        _save = save;
        _suspend = suspend;
        box.PreviewKeyDown += OnKey;
        box.GotKeyboardFocus += (_, _) => suspend(true);
        box.LostKeyboardFocus += (_, _) => suspend(false);
        box.Unloaded += (_, _) => suspend(false);
    }

    private void OnKey(object sender, KeyEventArgs e)
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

        var old = _current();
        if (!HotkeyGesture.TryParse(text, out var gesture, _minModifiers) || !_tryRegister(gesture.ToString()))
        {
            _tryRegister(old);  // keep the previous one working
            _message.Foreground = (Brush)_box.FindResource("Settings.Error");
            _message.Text = $"Shortcut tidak valid atau sudah dipakai: {text}";
            _suspend(true);  // still recording
            return;
        }
        _message.Foreground = (Brush)_box.FindResource("Settings.Muted");
        _message.Text = "Shortcut disimpan.";
        _save(gesture.ToString());
        _suspend(true);  // registered for real when the box loses focus
    }
}
