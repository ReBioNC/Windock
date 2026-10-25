using Windock.Core.Abstractions;
using Windock.Core.Logging;

namespace Windock.Core.Settings;

[Flags]
public enum SettingsSection { None = 0, Appearance = 1, Behavior = 2, Pins = 4, Safety = 8, Theme = 16, Status = 32, Launcher = 64, Trash = 128, HiddenApps = 256, Switcher = 512, Island = 1024 }

public sealed class SettingsChangedEventArgs(DockSettings old, DockSettings @new, SettingsSection sections) : EventArgs
{
    public DockSettings Old { get; } = old;
    public DockSettings New { get; } = @new;
    public SettingsSection Sections { get; } = sections;
}

/// Owns settings.json: tolerant load with .bak fallback, debounced atomic saves (ARCHITECTURE §8).
public sealed class SettingsStore(string directory, IDelayScheduler scheduler, ILog log)
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly string _path = Path.Combine(directory, "settings.json");
    private IDisposable? _pendingSave;

    public DockSettings Current { get; private set; } = SettingsDefaults.Create();

    public event EventHandler<SettingsChangedEventArgs>? Changed;

    public void Load()
    {
        Current = TryRead(_path, quarantineOnFailure: true)
                  ?? TryRead(_path + ".bak", quarantineOnFailure: false)
                  ?? SettingsDefaults.Create();
    }

    public void Update(Func<DockSettings, DockSettings> change)
    {
        var old = Current;
        var updated = SettingsNormalizer.Normalize(change(old));
        if (updated == old) return;

        Current = updated;
        _pendingSave?.Dispose();
        _pendingSave = scheduler.Schedule(SaveDelay, Save);
        Changed?.Invoke(this, new SettingsChangedEventArgs(old, updated, Diff(old, updated)));
    }

    public void Flush()
    {
        if (_pendingSave is null) return;
        Save();
    }

    private void Save()
    {
        _pendingSave?.Dispose();
        _pendingSave = null;
        try
        {
            Directory.CreateDirectory(directory);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, SettingsJson.Serialize(Current));
            if (File.Exists(_path)) File.Copy(_path, _path + ".bak", overwrite: true);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error("Failed to save settings", ex);
        }
    }

    private DockSettings? TryRead(string path, bool quarantineOnFailure)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return SettingsJson.Parse(File.ReadAllText(path));
        }
        catch (SettingsParseException ex)
        {
            log.Error($"Unreadable settings file {path}", ex);
            if (quarantineOnFailure)
            {
                // Move it aside so the next save never copies it over the good .bak.
                var corrupt = Path.Combine(directory, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
                File.Move(path, corrupt);
            }
            return null;
        }
        catch (IOException ex)
        {
            log.Error($"Cannot read settings file {path}", ex);
            return null;
        }
    }

    private static SettingsSection Diff(DockSettings a, DockSettings b)
    {
        var s = SettingsSection.None;
        if (a.Appearance != b.Appearance) s |= SettingsSection.Appearance;
        if (a.Behavior != b.Behavior) s |= SettingsSection.Behavior;
        if (!a.Pins.SequenceEqual(b.Pins)) s |= SettingsSection.Pins;
        if (a.Safety != b.Safety) s |= SettingsSection.Safety;
        if (a.Theme != b.Theme) s |= SettingsSection.Theme;
        if (a.Status != b.Status) s |= SettingsSection.Status;
        if (a.Launcher != b.Launcher) s |= SettingsSection.Launcher;
        if (a.Trash != b.Trash) s |= SettingsSection.Trash;
        if (a.Switcher != b.Switcher) s |= SettingsSection.Switcher;
        if (a.Island != b.Island) s |= SettingsSection.Island;
        if (!a.HiddenApps.SequenceEqual(b.HiddenApps)) s |= SettingsSection.HiddenApps;
        return s;
    }
}
