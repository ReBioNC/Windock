using System.IO;
using System.Windows;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Tray;

namespace Windock.Composition;

/// Wires every component together and owns the app lifecycle (ARCHITECTURE §9).
internal sealed class AppHost
{
    public static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Windock");

    private bool _paused;
    private bool _exiting;

    public AppHost(ILog log) => Log = log;

    public ILog Log { get; }
    public SettingsStore Settings { get; private set; } = null!;
    public TrayIcon? Tray { get; private set; }

    public void Start(string[] args)
    {
        Log.Info($"Windock starting (args: {string.Join(' ', args)})");
        Settings = new SettingsStore(DataDirectory, new DispatcherDelayScheduler(Application.Current.Dispatcher), Log);
        Settings.Load();
        Tray = new TrayIcon(OpenSettings, TogglePause, Exit);
    }

    public void OpenSettings()
    {
        Log.Info("open settings requested");
    }

    public void TogglePause()
    {
        _paused = !_paused;
        Tray?.SetPaused(_paused);
        Log.Info(_paused ? "dock paused" : "dock resumed");
    }

    public void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info("exit requested");
        RestoreAll();
        Settings?.Flush();
        Tray?.Dispose();
        Application.Current.Shutdown();
    }

    /// Puts Windows back the way it was. Safe to call repeatedly and from crash handlers.
    public void RestoreAll()
    {
        Log.Info("restore all");
    }
}
