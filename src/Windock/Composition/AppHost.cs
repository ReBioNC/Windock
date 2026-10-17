using System.IO;
using System.Windows;
using Microsoft.Win32;
using Windock.Core.Apps;
using Windock.Core.Input;
using Windock.Core.Layout;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Core.Taskbar;
using Windock.Dock;
using Windock.Interop;
using Windock.Settings;
using Windock.Tray;

namespace Windock.Composition;

/// Wires every component together and owns the app lifecycle (ARCHITECTURE §9).
internal sealed class AppHost
{
    public static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Windock");

    /// Present while Windock runs; left behind only by an unclean exit.
    private static readonly string SessionMarker = Path.Combine(DataDirectory, "session.lock");

    private bool _paused;
    private bool _fullscreen;
    private bool _exiting;
    private uint _taskbarCreatedMessage;
    private SettingsWindow? _settingsWindow;

    public AppHost(ILog log) => Log = log;

    public ILog Log { get; }
    public SettingsStore Settings { get; private set; } = null!;
    public TrayIcon? Tray { get; private set; }
    public WindowTracker Windows { get; private set; } = null!;
    public PinResolver PinResolver { get; private set; } = null!;
    public IconProvider? Icons { get; private set; }
    public DockModel? Model { get; private set; }
    public BackdropWindow? Backdrop { get; private set; }
    public DockController? Dock { get; private set; }
    public AppLauncher? Launcher { get; private set; }
    public AppBarService? AppBar { get; private set; }
    public MessageWindow? Messages { get; private set; }
    public TaskbarModeCoordinator? Taskbar { get; private set; }
    public HotkeyService? Hotkey { get; private set; }

    public void Start(string[] args)
    {
        Log.Info($"Windock starting (args: {string.Join(' ', args)})");
        Settings = new SettingsStore(DataDirectory, new DispatcherDelayScheduler(Application.Current.Dispatcher), Log);
        Settings.Load();
        if (!Settings.Current.FirstRunCompleted)
        {
            var pins = DetectDefaultPins();
            Log.Info($"first run: pinning {pins.Count} default apps");
            Settings.Update(s => s with { Pins = pins, FirstRunCompleted = true });
        }
        Windows = new WindowTracker(Log);
        PinResolver = new PinResolver(Log);
        if (args.Contains("--dump-windows"))
        {
            DumpDiagnostics(args.Where(a => !a.StartsWith("--")));
            Application.Current.Shutdown();
            return;
        }

        // Undo anything a killed previous session left on the taskbar before showing anything (A-04).
        var taskbarController = new Win32TaskbarController(Log, Application.Current.Dispatcher, Windows);
        Taskbar = new TaskbarModeCoordinator(taskbarController, new RecoveryStateStore(DataDirectory), Environment.ProcessId);
        var recovered = Taskbar.RecoverFromPreviousSession();
        if (File.Exists(SessionMarker) && !recovered)
        {
            Log.Info("previous session ended uncleanly: clearing its reserved screen space");
            taskbarController.RefreshWorkArea();
        }
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(SessionMarker, Environment.ProcessId.ToString());

        Tray = new TrayIcon(OpenSettings, TogglePause, Exit);
        Messages = new MessageWindow("WindockMessages");
        Messages.Message += OnSystemMessage;
        _taskbarCreatedMessage = Native.RegisterWindowMessage("TaskbarCreated");

        Icons = new IconProvider(Log, Application.Current.Dispatcher);
        Model = new DockModel(Windows, PinResolver);
        Model.SetPins(Settings.Current.Pins);
        Backdrop = new BackdropWindow();
        Dock = new DockController(Model, new StopwatchClock(), Settings, Icons, new DockWindow(), Backdrop, Log);
        Launcher = new AppLauncher(Log, (title, text) => Tray?.ShowBalloon(title, text));
        var menus = new DockContextMenu(Launcher, Settings, PinResolver, OpenSettings, Exit);
        // Live foreground: the tracker skips our own windows, so its value is stale while Pengaturan is active.
        Dock.ItemClicked += (item, shift) => Launcher.Execute(DockClickPolicy.Decide(item, Native.GetForegroundWindow(), shift), item);
        Dock.ContextRequested += item =>
        {
            var menu = menus.Build(item);
            menu.PlacementTarget = Dock.MenuAnchor;
            menu.IsOpen = true;
        };
        Dock.Start();

        AppBar = new AppBarService(Log);
        AppBar.Reserved += bottomPx => Dock.SetBottomEdge(bottomPx / Dock.DpiScale);
        // The shell also reports the desktop itself as "fullscreen"; treat its signal only as a cue to check.
        AppBar.FullscreenChanged += _ => CheckFullscreen();
        Windows.ForegroundChanged += (_, _) => CheckFullscreen();
        ReserveSpace();
        Taskbar.Apply(Settings.Current.Behavior.TaskbarMode);

        Hotkey = new HotkeyService(Messages);
        Hotkey.Pressed += () =>
        {
            Log.Info("emergency hotkey pressed");
            Exit();
        };
        RegisterHotkey(Settings.Current.Safety.EmergencyHotkey);

        Settings.Changed += OnSettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Log.Info("dock started");
    }

    public void OpenSettings()
    {
        if (Icons is null) return;
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        var context = new SettingsContext(Settings, Icons, PinResolver, DetectDefaultPins, text => RegisterHotkey(text, notify: false), SuspendHotkey, DataDirectory, Log);
        _settingsWindow = new SettingsWindow(context);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            Settings.Flush();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public static IReadOnlyList<PinEntry> DetectDefaultPins() => DefaultPins.Detect(File.Exists,
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs));

    public void TogglePause()
    {
        _paused = !_paused;
        Tray?.SetPaused(_paused);
        if (_paused)
        {
            Taskbar?.Restore();
            AppBar?.Release();
        }
        else
        {
            ReserveSpace();
            Taskbar?.Apply(Settings.Current.Behavior.TaskbarMode);
        }
        UpdateDockVisibility();
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

    /// Puts Windows back the way it was. Safe to call repeatedly and from crash handlers on any thread.
    public void RestoreAll()
    {
        Log.Info("restore all");
        Try(() => Taskbar?.Restore());
        Try(() => AppBar?.Release());
        Try(() => Hotkey?.Unregister());
        Try(() => File.Delete(SessionMarker));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        Dock?.ApplySettings(e);
        if (e.Sections.HasFlag(SettingsSection.Appearance) && e.New.Appearance.IconSize != e.Old.Appearance.IconSize) ReserveSpace();
        if (e.Sections.HasFlag(SettingsSection.Behavior) && !_paused) Taskbar?.Apply(e.New.Behavior.TaskbarMode);
        if (e.Sections.HasFlag(SettingsSection.Safety)) RegisterHotkey(e.New.Safety.EmergencyHotkey);
    }

    /// Registers the emergency hotkey; returns false (and tells the user) when it is invalid or taken.
    public bool RegisterHotkey(string text, bool notify = true)
    {
        if (Hotkey is not null && HotkeyGesture.TryParse(text, out var gesture) && Hotkey.TryRegister(gesture)) return true;
        Log.Error($"emergency hotkey '{text}' could not be registered");
        if (notify) Tray?.ShowBalloon("Shortcut darurat tidak aktif", "Shortcut dipakai aplikasi lain atau tidak valid. Ganti di Pengaturan.");
        return false;
    }

    private void SuspendHotkey(bool suspend)
    {
        if (suspend) Hotkey?.Unregister();
        else RegisterHotkey(Settings.Current.Safety.EmergencyHotkey, notify: false);
    }

    private void ReserveSpace()
    {
        if (_paused || Dock is null) return;
        AppBar?.Reserve(DockMetrics.ReservedThickness(Settings.Current.Appearance.IconSize), Dock.DpiScale);
    }

    private void CheckFullscreen()
    {
        var fullscreen = FullscreenDetector.IsFullscreenApp(Native.GetForegroundWindow());
        if (fullscreen == _fullscreen) return;
        _fullscreen = fullscreen;
        Log.Info($"fullscreen app: {fullscreen}");
        UpdateDockVisibility();
    }

    private void UpdateDockVisibility() => Dock?.SetHidden(_paused || _fullscreen);

    private void OnSystemMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is Native.WM_DISPLAYCHANGE or Native.WM_DPICHANGED)
        {
            Log.Info($"display change 0x{msg:X}");
            Dock?.Relayout();
            ReserveSpace();
        }
        else if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            // Explorer restarted: it forgot our app bar and shows its taskbar again.
            Log.Info("TaskbarCreated: re-applying");
            AppBar?.Release();
            ReserveSpace();
            if (!_paused) Taskbar?.Reapply();
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Log.Info("resumed from sleep: re-applying");
            ReserveSpace();
            if (!_paused) Taskbar?.Reapply();
        });
    }

    /// --dump-windows: log what the tracker and resolver see, for manual verification.
    private void DumpDiagnostics(IEnumerable<string> extraPins)
    {
        foreach (var w in Windows.Snapshot())
            Log.Info($"window 0x{w.Handle:X} app={w.AppId} aumid={ShellInterop.WindowAppUserModelId(w.Handle)} name='{w.DisplayName}' title='{w.Title}' min={w.IsMinimized}");
        foreach (var pin in DetectDefaultPins().Concat(extraPins.Select(p => new PinEntry(p))))
        {
            var r = PinResolver.Resolve(pin);
            Log.Info($"pin {pin.Path} -> app={r.AppId} name='{r.DisplayName}' exists={r.TargetExists}");
        }
    }

    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("restore step failed", ex);
        }
    }
}
