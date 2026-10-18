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

    /// Mode Windows: dock hidden, taskbar and screen space back to stock Windows; every setting is kept.
    private bool InWindowsMode => Settings.Current.Behavior.Mode == DockMode.Windows;
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
    public CommandChannel? Commands { get; private set; }
    public AutoHideController? AutoHide { get; private set; }

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

        Tray = new TrayIcon(OpenSettings, ToggleMode, Exit);
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
        Dock.ItemClicked += (item, shift) =>
        {
            var foreground = Native.GetForegroundWindow();
            var action = DockClickPolicy.Decide(item, foreground, shift);
            Log.Info($"click {item.Key} windows=[{string.Join(",", item.Windows.Select(w => $"0x{w.Handle:X}"))}] fg=0x{foreground:X} -> {action}");
            if (action is ClickAction.ShowWindows) Dock.ShowPreview(item);
            else Launcher.Execute(action, item);
        };
        Dock.ContextRequested += item =>
        {
            var menu = menus.Build(item);
            menu.PlacementTarget = Dock.MenuAnchor;
            Dock.SetMenuOpen(true);
            menu.Closed += (_, _) => Dock.SetMenuOpen(false);
            menu.IsOpen = true;
        };
        Dock.DropCompleted += HandleDrop;
        Dock.PreviewWindowChosen += hwnd => Launcher.Activate(hwnd);
        Dock.PreviewCloseRequested += hwnd => Launcher.CloseWindows(new[] { hwnd });
        Dock.SetHidden(InWindowsMode);
        Dock.Start();

        AutoHide = new AutoHideController(Dock, Windows, () => Settings.Current.Behavior.AutoHide, Log);
        AppBar = new AppBarService(Log);
        AppBar.Reserved += bottomPx => Dock.SetBottomEdge(bottomPx / Dock.DpiScale);
        // The shell also reports the desktop itself as "fullscreen"; treat its signal only as a cue to check.
        AppBar.FullscreenChanged += _ => CheckFullscreen();
        Windows.ForegroundChanged += (_, _) => CheckFullscreen();
        ApplyMode();

        Hotkey = new HotkeyService(Messages);
        Hotkey.Pressed += id =>
        {
            if (id != HotkeyService.EmergencyId) return;
            Log.Info("emergency hotkey pressed");
            Exit();
        };
        RegisterHotkey(Settings.Current.Safety.EmergencyHotkey);

        Commands = new CommandChannel(Application.Current.Dispatcher, Log);
        Commands.CommandReceived += (verb, argument) =>
        {
            if (verb == "pin") PinPath(argument);
        };
        SyncExplorerMenu(Settings.Current.Behavior.ExplorerContextMenu);

        Settings.Changed += OnSettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        MemoryTrim.After(TimeSpan.FromSeconds(20));
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
            MemoryTrim.After(TimeSpan.FromSeconds(2));  // the settings UI is garbage now
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public static IReadOnlyList<PinEntry> DetectDefaultPins() => DefaultPins.Detect(File.Exists,
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs));

    public void SetMode(DockMode mode) => Settings.Update(s => s with { Behavior = s.Behavior with { Mode = mode } });

    private void ToggleMode() => SetMode(InWindowsMode ? DockMode.Windock : DockMode.Windows);

    /// Puts the screen in the current mode. Mode Windows restores the taskbar exactly as the user had it.
    private void ApplyMode()
    {
        if (InWindowsMode)
        {
            Taskbar?.Restore();
            AppBar?.Release();
        }
        else
        {
            ReserveSpace();
            Taskbar?.Apply(Settings.Current.Behavior.TaskbarMode);
        }
        Tray?.SetMode(InWindowsMode);
        UpdateDockVisibility();
        Log.Info($"mode: {Settings.Current.Behavior.Mode}");
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
        Try(() => Hotkey?.UnregisterAll());
        Try(() => File.Delete(SessionMarker));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        Dock?.ApplySettings(e);
        if (e.Sections.HasFlag(SettingsSection.Appearance) && e.New.Appearance.IconSize != e.Old.Appearance.IconSize) ReserveSpace();
        if (e.New.Behavior.AutoHide != e.Old.Behavior.AutoHide)
        {
            ReserveSpace();
            AutoHide?.Apply();
        }
        if (e.New.Behavior.Mode != e.Old.Behavior.Mode) ApplyMode();
        else if (e.Sections.HasFlag(SettingsSection.Behavior) && !InWindowsMode) Taskbar?.Apply(e.New.Behavior.TaskbarMode);
        if (e.Sections.HasFlag(SettingsSection.Safety)) RegisterHotkey(e.New.Safety.EmergencyHotkey);
        if (e.New.Behavior.ExplorerContextMenu != e.Old.Behavior.ExplorerContextMenu) SyncExplorerMenu(e.New.Behavior.ExplorerContextMenu);
    }

    /// Registers the emergency hotkey; returns false (and tells the user) when it is invalid or taken.
    public bool RegisterHotkey(string text, bool notify = true)
    {
        if (Hotkey is not null && HotkeyGesture.TryParse(text, out var gesture) && Hotkey.TryRegister(HotkeyService.EmergencyId, gesture)) return true;
        Log.Error($"emergency hotkey '{text}' could not be registered");
        if (notify) Tray?.ShowBalloon("Shortcut darurat tidak aktif", "Shortcut dipakai aplikasi lain atau tidak valid. Ganti di Pengaturan.");
        return false;
    }

    /// Pins a program or shortcut (Explorer "Pin ke Windock", drag and drop). index = gap among pins; null = last.
    public void PinPath(string path, int? index = null)
    {
        if (!PinListEditor.CanPin(path))
        {
            Tray?.ShowBalloon("Tidak bisa di-pin", "Hanya program (.exe) dan shortcut (.lnk) yang bisa ditaruh di dock.");
            return;
        }
        Log.Info($"pin {path} at {index?.ToString() ?? "end"}");
        Settings.Update(s => s with { Pins = PinListEditor.Insert(s.Pins, new PinEntry(path), index ?? s.Pins.Count, IdOf) });
    }

    private AppId IdOf(PinEntry pin) => PinResolver.Resolve(pin).AppId;

    /// Applies a finished drag in the dock to the pin list (gap indexes exclude the dragged icon).
    private void HandleDrop(DockDrop drop)
    {
        Log.Info($"dock drop: {drop}");
        switch (drop)
        {
            case DockDrop.Reorder { Item.AppId: { } id } reorder:
                Settings.Update(s =>
                {
                    var from = s.Pins.ToList().FindIndex(p => IdOf(p) == id);
                    if (from < 0) return s;
                    var to = reorder.GapIndex >= from ? reorder.GapIndex + 1 : reorder.GapIndex;
                    return s with { Pins = PinListEditor.Move(s.Pins, from, to) };
                });
                break;
            case DockDrop.PinRunning running when DockContextMenu.PinPathFor(running.Item) is { } path:
                PinPath(path, running.GapIndex);
                break;
            case DockDrop.PinFiles files:
                for (var i = 0; i < files.Paths.Count; i++) PinPath(files.Paths[i], files.GapIndex + i);
                break;
            case DockDrop.Remove { Item.AppId: { } removeId }:
                Settings.Update(s => s with { Pins = PinListEditor.Remove(s.Pins, removeId, IdOf) });
                break;
        }
    }

    private void SyncExplorerMenu(bool enabled)
    {
        try
        {
            if (!enabled) ExplorerContextMenu.Unregister();
            else if (Environment.ProcessPath is { } exe && !ExplorerContextMenu.IsRegisteredFor(exe)) ExplorerContextMenu.Register(exe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("explorer context menu registration failed", ex);
        }
    }

    private void SuspendHotkey(bool suspend)
    {
        if (suspend) Hotkey?.Unregister(HotkeyService.EmergencyId);
        else RegisterHotkey(Settings.Current.Safety.EmergencyHotkey, notify: false);
    }

    private void ReserveSpace()
    {
        if (InWindowsMode || Dock is null) return;
        if (!AutoHidePolicy.ReservesScreenSpace(Settings.Current.Behavior.AutoHide))
        {
            AppBar?.Release();  // a hiding dock leaves the whole screen to windows
            return;
        }
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

    private void UpdateDockVisibility()
    {
        Dock?.SetHidden(InWindowsMode || _fullscreen);
        AutoHide?.SetSuspended(InWindowsMode || _fullscreen);
    }

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
            if (!InWindowsMode) Taskbar?.Reapply();
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Log.Info("resumed from sleep: re-applying");
            ReserveSpace();
            if (!InWindowsMode) Taskbar?.Reapply();
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
