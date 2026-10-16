using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Core.Tests.Fakes;

namespace Windock.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "windock-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ManualDelayScheduler _scheduler = new();

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private SettingsStore NewStore()
    {
        var store = new SettingsStore(_dir, _scheduler, NullLog.Instance);
        store.Load();
        return store;
    }

    private static DockSettings WithOpacity(DockSettings s, double o) => s with { Appearance = s.Appearance with { BackgroundOpacity = o } };

    [Fact]
    public void Load_without_file_uses_defaults_and_creates_no_file()
    {
        var store = NewStore();

        Assert.Equal(SettingsDefaults.Create(), store.Current);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Update_raises_Changed_with_only_affected_sections()
    {
        var store = NewStore();
        SettingsChangedEventArgs? args = null;
        store.Changed += (_, e) => args = e;

        store.Update(s => WithOpacity(s, 0.8));

        Assert.NotNull(args);
        Assert.Equal(SettingsSection.Appearance, args!.Sections);
        Assert.Equal(0.8, args.New.Appearance.BackgroundOpacity);
        Assert.Equal(0.55, args.Old.Appearance.BackgroundOpacity);
    }

    [Fact]
    public void Update_reports_pins_section_for_pin_changes()
    {
        var store = NewStore();
        SettingsSection sections = SettingsSection.None;
        store.Changed += (_, e) => sections = e.Sections;

        store.Update(s => s with { Pins = new[] { new PinEntry("a.exe") } });

        Assert.Equal(SettingsSection.Pins, sections);
    }

    [Fact]
    public void Update_with_no_effective_change_raises_nothing()
    {
        var store = NewStore();
        var raised = false;
        store.Changed += (_, _) => raised = true;

        store.Update(s => s with { Pins = new List<PinEntry>() });
        store.Update(s => WithOpacity(s, 0.55));

        Assert.False(raised);
        Assert.Empty(_scheduler.Active);
    }

    [Fact]
    public void Update_normalizes_values()
    {
        var store = NewStore();

        store.Update(s => s with { Appearance = s.Appearance with { IconSize = 500 } });

        Assert.Equal(96, store.Current.Appearance.IconSize);
    }

    [Fact]
    public void Saves_are_debounced_until_scheduler_fires()
    {
        var store = NewStore();

        store.Update(s => WithOpacity(s, 0.6));
        store.Update(s => WithOpacity(s, 0.7));

        Assert.Single(_scheduler.Active);
        Assert.Equal(SettingsStore.SaveDelay, _scheduler.Active[0].Delay);
        Assert.False(File.Exists(SettingsPath));

        _scheduler.RunAll();

        Assert.Equal(store.Current, SettingsJson.Parse(File.ReadAllText(SettingsPath)));
    }

    [Fact]
    public void Flush_writes_immediately_and_leaves_no_tmp_file()
    {
        var store = NewStore();
        store.Update(s => WithOpacity(s, 0.6));

        store.Flush();

        Assert.Equal(0.6, SettingsJson.Parse(File.ReadAllText(SettingsPath)).Appearance.BackgroundOpacity);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.Empty(_scheduler.Active);
    }

    [Fact]
    public void Previous_valid_file_is_kept_as_bak_on_save()
    {
        var store = NewStore();
        store.Update(s => WithOpacity(s, 0.6));
        store.Flush();

        store.Update(s => WithOpacity(s, 0.7));
        store.Flush();

        Assert.Equal(0.6, SettingsJson.Parse(File.ReadAllText(SettingsPath + ".bak")).Appearance.BackgroundOpacity);
        Assert.Equal(0.7, SettingsJson.Parse(File.ReadAllText(SettingsPath)).Appearance.BackgroundOpacity);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_bak_and_is_preserved()
    {
        File.WriteAllText(SettingsPath, "{{{");
        File.WriteAllText(SettingsPath + ".bak", SettingsJson.Serialize(WithOpacity(SettingsDefaults.Create(), 0.42)));

        var store = NewStore();

        Assert.Equal(0.42, store.Current.Appearance.BackgroundOpacity);
        var corrupt = Assert.Single(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
        Assert.Equal("{{{", File.ReadAllText(corrupt));
    }

    [Fact]
    public void Corrupt_file_and_corrupt_bak_fall_back_to_defaults()
    {
        File.WriteAllText(SettingsPath, "{{{");
        File.WriteAllText(SettingsPath + ".bak", "nope");

        var store = NewStore();

        Assert.Equal(SettingsDefaults.Create(), store.Current);
    }

    [Fact]
    public void Corrupt_file_is_not_copied_over_bak_on_next_save()
    {
        File.WriteAllText(SettingsPath, "{{{");
        File.WriteAllText(SettingsPath + ".bak", SettingsJson.Serialize(WithOpacity(SettingsDefaults.Create(), 0.42)));
        var store = NewStore();

        store.Update(s => WithOpacity(s, 0.9));
        store.Flush();

        Assert.Equal(0.42, SettingsJson.Parse(File.ReadAllText(SettingsPath + ".bak")).Appearance.BackgroundOpacity);
    }
}
