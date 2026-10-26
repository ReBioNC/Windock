using System.Windows;
using System.Windows.Controls;
using Windock.Core.Animation;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

internal partial class AnimationsPage : UserControl
{
    private readonly PageSync _sync;

    public AnimationsPage(SettingsContext context)
    {
        InitializeComponent();
        _sync = new PageSync(this, context.Store, Refresh);

        foreach (var (button, effect) in Effects())
            button.Checked += (_, _) => Edit(a => a with { Effect = effect });
        PageSync.OnToggle(AnimateRestore, on => Edit(a => a with { AnimateRestore = on }));
        PageSync.OnToggle(DisableWindows, on => Edit(a => a with { DisableWindowsAnimation = on }));
        Duration.ValueChanged += (_, e) => Edit(a => a with { DurationMs = (int)Math.Round(e.NewValue) });
    }

    private (RadioButton, MinimizeEffect)[] Effects() => new[]
    {
        (EffectGenie, MinimizeEffect.Genie), (EffectScale, MinimizeEffect.Scale), (EffectFold, MinimizeEffect.Fold),
        (EffectSqueeze, MinimizeEffect.Squeeze), (EffectDrop, MinimizeEffect.Drop), (EffectOff, MinimizeEffect.Off),
    };

    private void Edit(Func<AnimationSettings, AnimationSettings> change) => _sync.Edit(s => s with { Animations = change(s.Animations) });

    private void Refresh(DockSettings s)
    {
        var a = s.Animations;
        foreach (var (button, effect) in Effects()) button.IsChecked = a.Effect == effect;
        Options.IsEnabled = a.Effect != MinimizeEffect.Off;
        Duration.Value = a.DurationMs;
        DurationText.Text = $"{a.DurationMs} ms";
        AnimateRestore.IsChecked = a.AnimateRestore;
        DisableWindows.IsChecked = a.DisableWindowsAnimation;
    }

    private void OnReset(object sender, RoutedEventArgs e) => _sync.Edit(s => s with { Animations = AnimationSettings.Default });
}
