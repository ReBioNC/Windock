using System.Windows;
using System.Windows.Controls;
using Windock.Core.Settings;

namespace Windock.Settings.Pages;

/// Keeps a page's controls in step with SettingsStore without echoing its own updates back.
internal sealed class PageSync
{
    private readonly SettingsStore _store;
    private readonly Action<DockSettings> _refresh;
    private bool _syncing;

    public PageSync(UserControl page, SettingsStore store, Action<DockSettings> refresh)
    {
        _store = store;
        _refresh = refresh;
        page.Loaded += (_, _) =>
        {
            _store.Changed += OnChanged;
            Refresh(_store.Current);
        };
        page.Unloaded += (_, _) => _store.Changed -= OnChanged;
    }

    /// Apply a user edit (ignored while the page is being refreshed from the store).
    public void Edit(Func<DockSettings, DockSettings> change)
    {
        if (!_syncing) _store.Update(change);
    }

    private void OnChanged(object? sender, SettingsChangedEventArgs e) => Refresh(e.New);

    private void Refresh(DockSettings s)
    {
        _syncing = true;
        try { _refresh(s); }
        finally { _syncing = false; }
    }

    /// "Kembalikan ke default" for appearance and behaviour; pins and first-run state are kept.
    public static void ResetToDefaults(SettingsStore store)
    {
        var answer = MessageBox.Show("Kembalikan semua tampilan dan perilaku ke pengaturan awal? Daftar pin tidak berubah.",
            "Windock", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        store.Update(s => SettingsDefaults.Create() with { Pins = s.Pins, FirstRunCompleted = s.FirstRunCompleted });
    }
}
