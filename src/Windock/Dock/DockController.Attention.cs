using System.Windows.Threading;
using Windock.Core.Animation;
using Windock.Core.Apps;
using Windock.Core.Settings;
using Windock.Interop;

namespace Windock.Dock;

/// Icons of windows that flash for attention: bounce bursts or a red badge until the app is opened.
internal sealed partial class DockController
{
    private readonly AttentionTracker _attention = new();
    private DispatcherTimer? _attentionTimer;

    /// The shell reported a flashing window.
    public void RequestAttention(IntPtr hwnd)
    {
        var style = _settings.Current.Behavior.Attention;
        if (style == AttentionStyle.Off || hwnd == IntPtr.Zero) return;
        var item = ItemWithWindow(hwnd) ?? ItemWithWindow(ShellHook.RootOwner(hwnd));
        if (item is null || item.IsActive || !_attention.Request(item.Key)) return;
        _log.Info($"attention: {item.Key}");
        UpdateAttention();
    }

    /// Windows notifications (toasts, badges) for apps identified by AUMID (lower case).
    public void RequestAppAttention(IEnumerable<string> aumids)
    {
        if (_settings.Current.Behavior.Attention == AttentionStyle.Off) return;
        var changed = false;
        foreach (var aumid in aumids)
        {
            if (_model.Find("aumid:" + aumid) is not { IsActive: false } item || !_attention.Request(item.Key)) continue;
            _log.Info($"attention (notification): {item.Key}");
            changed = true;
        }
        if (changed) UpdateAttention();
    }

    /// The app's notifications were all read (its badge went back to zero).
    public void ClearAppAttention(IEnumerable<string> aumids)
    {
        var changed = false;
        foreach (var aumid in aumids)
        {
            var key = "aumid:" + aumid;
            if (!_attention.Clear(key)) continue;
            _bounce.Stop(key, _clock.Now);
            changed = true;
        }
        if (changed) UpdateAttention();
    }

    private DockItem? ItemWithWindow(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? null : _model.Items.FirstOrDefault(i => i.Windows.Any(w => w.Handle == hwnd));

    /// Opened apps lose their mark; so do icons that left the dock.
    private void ClearAttention()
    {
        if (_attention.Keys.Count == 0) return;
        var before = _attention.Keys.Count;
        _attention.Retain(_model.Items.Select(i => i.Key));
        foreach (var item in _model.Items.Where(i => i.IsActive && _attention.Has(i.Key)).ToList())
        {
            _attention.Clear(item.Key);
            _bounce.Stop(item.Key, _clock.Now);
        }
        if (_attention.Keys.Count != before) UpdateAttention();
    }

    private void ApplyAttentionSettings(BehaviorSettings old, BehaviorSettings now)
    {
        if (old.Attention == now.Attention && old.AttentionRepeat == now.AttentionRepeat) return;
        if (now.Attention == AttentionStyle.Off)
        {
            foreach (var key in _attention.Keys.ToList())
            {
                _attention.Clear(key);
                _bounce.Stop(key, _clock.Now);
            }
        }
        UpdateAttention();
    }

    /// Starts due bounce bursts and keeps the burst timer running only while one can still come.
    private void UpdateAttention()
    {
        var behavior = _settings.Current.Behavior;
        if (behavior.Attention == AttentionStyle.Bounce)
        {
            foreach (var key in _attention.DueBursts(_clock.Now, behavior.AttentionRepeat))
                _bounce.Start(key, _clock.Now, BounceAnimator.Cycle * AttentionTracker.BounceBurstCycles);
        }
        var needTimer = behavior.Attention == AttentionStyle.Bounce && _attention.HasPendingBursts(behavior.AttentionRepeat);
        if (needTimer && _attentionTimer is null)
        {
            _attentionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _attentionTimer.Tick += (_, _) => UpdateAttention();
            _attentionTimer.Start();
        }
        else if (!needTimer && _attentionTimer is not null)
        {
            _attentionTimer.Stop();
            _attentionTimer = null;
        }
        Invalidate();
    }

    private bool ShowsAttentionBadge(string key) =>
        _settings.Current.Behavior.Attention == AttentionStyle.Dot && _attention.Has(key);
}
