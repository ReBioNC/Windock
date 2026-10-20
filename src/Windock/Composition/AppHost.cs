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
    public RecycleBinWatcher? BinWatcher { get; private set; }
    public ShellHook? Shell { get; private set; }
    private NotificationWatcher? _notifications;
    public Status.StatusArea? StatusArea { get; private set; }
    private Launcher.LauncherWindow? _launcherWindow;

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
        Model.SetHiddenApps(Settings.Current.HiddenApps.Select(h => h.AppId));
        Model.SetShowRecycleBin(Settings.Current.Trash.ShowRecycleBin);
        Backdrop = new BackdropWindow();
        Dock = new DockController(Model, new StopwatchClock(), Settings, Icons, new DockWindow(), Backdrop, Log);
        Launcher = new AppLauncher(Log, (title, text) => Tray?.ShowBalloon(title, text));
        var menus = new DockContextMenu(Launcher, Settings, PinResolver, OpenSettings, Exit, EmptyRecycleBin);
        // Live foreground: the tracker skips our own windows, so its value is stale while Pengaturan is active.
        Dock.ItemClicked += (item, shift) =>
        {
            var foreground = Native.GetForegroundWindow();
            var action = DockClickPolicy.Decide(item, foreground, shift);
            Log.Info($"click {item.Key} windows=[{string.Join(",", item.Windows.Select(w => $"0x{w.Handle:X}"))}] fg=0x{foreground:X} -> {action}");
            if (action is ClickAction.ShowWindows) Dock.ShowPreview(item);
            else Launcher.Execute(action, item);
            if (action is ClickAction.Launch) Dock.BounceLaunch(item.Key);
        };
        Dock.ContextRequested += item =>
        {
            var menu = menus.Build(item);
            menu.PlacementTarget = Dock.MenuAnchor;
            Dock.SetMenuOpen(true);
            // The dock never takes focus, so the menu would not notice clicks elsewhere; watch for them.
            OutsideClickWatcher? outside = null;
            menu.Opened += (_, _) => outside = new OutsideClickWatcher(
                () => PresentationSource.FromVisual(menu) is System.Windows.Interop.HwndSource source ? source.Handle : IntPtr.Zero,
                () => menu.IsOpen = false);
            menu.Closed += (_, _) =>
            {
                outside?.Dispose();
                Dock.SetMenuOpen(false);
            };
            menu.IsOpen = true;
        };
        Dock.DropCompleted += HandleDrop;
        Dock.PreviewWindowChosen += hwnd => Launcher.Activate(hwnd);
        Dock.PreviewCloseRequested += hwnd => Launcher.CloseWindows(new[] { hwnd });
        Dock.SetHidden(InWindowsMode);
        Dock.Start();
        Shell = new ShellHook(Messages);
        Shell.Flashed += hwnd => Dock.RequestAttention(hwnd);
        SyncNotificationWatcher();
        StatusArea = new Status.StatusArea(Settings, Application.Current.Dispatcher, Log);
        Dock.AttachStatus(StatusArea);
        Dock.StatusClicked += OnStatusClicked;
        Shell.LanguageChanged += () => StatusArea.RefreshLanguage();
        Shell.Activated += _ => StatusArea.RefreshLanguage();
        BinWatcher = new RecycleBinWatcher();
        BinWatcher.Changed += RefreshRecycleBin;
        RefreshRecycleBin();

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
            if (id == HotkeyService.LauncherId)
            {
                ToggleLauncher();
                return;
            }
            Log.Info("emergency hotkey pressed");
            Exit();
        };
        RegisterHotkey(Settings.Current.Safety.EmergencyHotkey);
        RegisterLauncherHotkey(Settings.Current.Launcher);

        Commands = new CommandChannel(Application.Current.Dispatcher, Log);
        Commands.CommandReceived += (verb, argument) =>
        {
            if (verb == "pin") PinPath(argument);
        };
        SyncExplorerMenu(Settings.Current.Behavior.ExplorerContextMenu);
        SyncStartup(Settings.Current.Behavior.StartWithWindows);

        ApplyAccent(Settings.Current.Theme.AccentColor);
        UpdateWallpaperColor();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.Desktop) UpdateWallpaperColor();
        };

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
        var context = new SettingsContext(Settings, Icons, PinResolver, DetectDefaultPins, text => RegisterHotkey(text, notify: false),
            text => RegisterLauncherHotkey(Settings.Current.Launcher with { Hotkey = text }, notify: false), SuspendHotkey, DataDirectory, Log);
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
        if (e.New.Behavior.StartWithWindows != e.Old.Behavior.StartWithWindows) SyncStartup(e.New.Behavior.StartWithWindows);
        if (e.New.Theme.AccentColor != e.Old.Theme.AccentColor) ApplyAccent(e.New.Theme.AccentColor);
        if (e.Sections.HasFlag(SettingsSection.Status)) StatusArea?.ApplySettings();
        if (e.New.Behavior.AttentionFromNotifications != e.Old.Behavior.AttentionFromNotifications) SyncNotificationWatcher();
        if (e.Sections.HasFlag(SettingsSection.Launcher)) RegisterLauncherHotkey(e.New.Launcher);
        if (e.New.Theme.FollowWallpaper && !e.Old.Theme.FollowWallpaper) UpdateWallpaperColor();
    }

    /// Windows notifications (WhatsApp and other apps that send toasts or badges instead of flashing) also mark icons.
    private void SyncNotificationWatcher()
    {
        if (Settings.Current.Behavior.AttentionFromNotifications && _notifications is null && Dock is not null)
        {
            _notifications = new NotificationWatcher(Application.Current.Dispatcher, Log);
            _notifications.Raised += apps => Dock.RequestAppAttention(apps);
            _notifications.Cleared += apps => Dock.ClearAppAttention(apps);
            _notifications.BadgesChanged += counts => Dock.SetBadgeCounts(counts);
        }
        else if (!Settings.Current.Behavior.AttentionFromNotifications && _notifications is not null)
        {
            _notifications.Dispose();
            _notifications = null;
            Dock?.SetBadgeCounts(new Dictionary<string, int>());
        }
    }

    private void OnStatusClicked(string key, Point anchor)
    {
        Log.Info($"status click {key}");
        switch (key)
        {
            case Status.StatusArea.Tray:
                var icons = TrayIcons.Read();
                Log.Info($"tray popup: {icons.Count} icons");
                ShowStatusPopup(new Status.TrayPopup(icons), anchor);
                break;
            case Status.StatusArea.Network:
                OpenUri("ms-availablenetworks:");
                break;
            case Status.StatusArea.Volume when StatusArea is not null:
                ShowStatusPopup(new Status.VolumePopup(StatusArea.Audio), anchor);
                break;
            case Status.StatusArea.Battery:
                OpenUri("ms-settings:batterysaver");
                break;
            case Status.StatusArea.Language:
                SystemStatus.SwitchInputLanguage();
                var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                later.Tick += (_, _) => { later.Stop(); StatusArea?.RefreshLanguage(); };
                later.Start();
                break;
            case Status.StatusArea.Clock:
                ShowStatusPopup(new Status.CalendarPopup(Settings.Current.Status.Clock24Hour), anchor);
                break;
            case Status.StatusArea.Notifications:
                OpenUri("ms-actioncenter:");
                break;
        }
    }

    /// Keeps an auto-hiding dock on screen while the popup is open.
    /// Keeps an auto-hiding dock on screen while the popup is open.
    private void ShowStatusPopup(Status.DockPopup popup, Point anchor)
    {
        Dock?.SetMenuOpen(true);
        popup.Closed += (_, _) =>
        {
            // A tray icon's own menu opens as the tray popup closes: keep the dock up for it too.
            if (popup is Status.TrayPopup { MenuOwner: { } icon }) HoldDockForAppMenu(icon.Owner);
            else Dock?.SetMenuOpen(false);
        };
        popup.ShowAt(anchor);
    }

    /// Keeps the dock shown while a tray app's menu is up: until the next click (a menu item or elsewhere),
    /// or until neither a menu window nor that app is in front any more. Checks run only during this hold.
    private void HoldDockForAppMenu(IntPtr owner)
    {
        Native.GetWindowThreadProcessId(owner, out var ownerPid);
        var started = DateTime.UtcNow;
        var lastSeen = started;
        DateTime? clicked = null;
        var clicks = new OutsideClickWatcher(() => IntPtr.Zero, () => clicked ??= DateTime.UtcNow);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var foregroundPid);
            if (AnyMenuVisible() || foregroundPid == ownerPid) lastSeen = now;
            var done = (clicked is { } at && now - at > TimeSpan.FromMilliseconds(400))
                       || now - lastSeen > TimeSpan.FromMilliseconds(800)
                       || now - started > TimeSpan.FromMinutes(1);
            if (!done) return;
            timer.Stop();
            clicks.Dispose();
            Dock?.SetMenuOpen(false);
        };
        timer.Start();
    }

    private static bool AnyMenuVisible()
    {
        var h = IntPtr.Zero;
        while ((h = Native.FindWindowEx(IntPtr.Zero, h, "#32768", null)) != IntPtr.Zero)
            if (Native.IsWindowVisible(h)) return true;
        return false;
    }

    private void OpenUri(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"cannot open {uri}", ex);
        }
    }

    /// Accent color of menus, highlights and Pengaturan (dynamic resources, so open windows update too).
    private static void ApplyAccent(string hex)
    {
        if (!ThemeColor.TryParse(hex, out var c)) return;
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
        brush.Freeze();
        Application.Current.Resources["Windock.Accent"] = brush;
        Application.Current.Resources["Settings.Accent"] = brush;
    }

    /// Recomputes the wallpaper-based plate color (only while that option is on).
    private async void UpdateWallpaperColor()
    {
        if (!Settings.Current.Theme.FollowWallpaper) return;
        try
        {
            var hex = await WallpaperColor.ComputeAsync();
            Log.Info($"wallpaper color {hex}");
            Dock?.SetWallpaperColor(hex);
        }
        catch (Exception ex)
        {
            Log.Error("wallpaper color failed", ex);
        }
    }

    /// Registers the emergency hotkey; returns false (and tells the user) when it is invalid or taken.
    public bool RegisterHotkey(string text, bool notify = true)
    {
        if (Hotkey is not null && HotkeyGesture.TryParse(text, out var gesture) && Hotkey.TryRegister(HotkeyService.EmergencyId, gesture)) return true;
        Log.Error($"emergency hotkey '{text}' could not be registered");
        if (notify) Tray?.ShowBalloon("Shortcut darurat tidak aktif", "Shortcut dipakai aplikasi lain atau tidak valid. Ganti di Pengaturan.");
        return false;
    }

    /// Registers (or removes) the launcher hotkey; false when it is invalid or taken.
    public bool RegisterLauncherHotkey(LauncherSettings launcher, bool notify = true)
    {
        if (Hotkey is null) return false;
        if (!launcher.Enabled)
        {
            Hotkey.Unregister(HotkeyService.LauncherId);
            return true;
        }
        if (HotkeyGesture.TryParse(launcher.Hotkey, out var gesture, minModifiers: 1) && Hotkey.TryRegister(HotkeyService.LauncherId, gesture)) return true;
        Log.Error($"launcher hotkey '{launcher.Hotkey}' could not be registered");
        if (notify) Tray?.ShowBalloon("Shortcut launcher tidak aktif", $"{launcher.Hotkey} dipakai aplikasi lain atau tidak valid. Ganti di Pengaturan > Launcher.");
        return false;
    }

    private void ToggleLauncher()
    {
        if (_launcherWindow is not null)
        {
            _launcherWindow.CloseOnce();
            return;
        }
        if (Icons is null || Launcher is null) return;
        var l = Settings.Current.Launcher;
        Log.Info("launcher opened");
        _launcherWindow = new Launcher.LauncherWindow(Icons, l.MaxResults, l.IncludeFiles, entry =>
        {
            Log.Info($"launcher open {entry.Target}");
            Launcher.Launch(entry.Target, entry.Name);
        }, Log);
        _launcherWindow.Closed += (_, _) =>
        {
            _launcherWindow = null;
            MemoryTrim.After(TimeSpan.FromSeconds(2));  // the index and window are garbage now
        };
        _launcherWindow.Show();
        _launcherWindow.Activate();
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
            case DockDrop.TrashFiles trash:
                TrashFiles(trash.Paths);
                break;
            case DockDrop.Remove { Item.AppId: { } removeId }:
                Settings.Update(s => s with { Pins = PinListEditor.Remove(s.Pins, removeId, IdOf) });
                break;
        }
    }

    /// Files dropped on the Recycle Bin icon go to the Recycle Bin (undoable), after an optional question.
    private void TrashFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        if (Settings.Current.Trash.ConfirmBeforeTrash)
        {
            var what = paths.Count == 1 ? $"\"{Path.GetFileName(paths[0].TrimEnd('\\'))}\"" : $"{paths.Count} item";
            var answer = MessageBox.Show($"Pindahkan {what} ke Recycle Bin?\n\nBisa dipulihkan lagi dari Recycle Bin.", "Windock",
                MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.OK);
            if (answer != MessageBoxResult.OK) return;
        }
        var ok = RecycleBin.Send(paths, IntPtr.Zero);
        Log.Info($"trash {paths.Count} item(s): {(ok ? "done" : "cancelled or failed")}");
        RefreshRecycleBin();
    }

    private void EmptyRecycleBin()
    {
        RecycleBin.Empty(IntPtr.Zero);
        RefreshRecycleBin();
    }

    private void RefreshRecycleBin() => Dock?.SetRecycleBinFull(!RecycleBin.IsEmpty());

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

    private void SyncStartup(bool enabled)
    {
        try
        {
            if (Environment.ProcessPath is { } exe) StartupRegistration.Sync(enabled, exe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("startup registration failed", ex);
        }
    }

    private void SuspendHotkey(bool suspend)
    {
        if (suspend)
        {
            Hotkey?.UnregisterAll();
            return;
        }
        RegisterHotkey(Settings.Current.Safety.EmergencyHotkey, notify: false);
        RegisterLauncherHotkey(Settings.Current.Launcher, notify: false);
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
        if (msg == Native.WM_QUERYENDSESSION)
        {
            // Shutdown, restart or sign-out: Windows may end the process before any exit code runs,
            // so give the taskbar back now. If the shutdown is cancelled, WM_ENDSESSION(false) re-applies.
            Log.Info("session ending: restoring the taskbar");
            Try(() => Taskbar?.Restore());
            Try(() => AppBar?.Release());
            Settings.Flush();
            return;  // default handling allows the session to end
        }
        if (msg == Native.WM_ENDSESSION)
        {
            if (wParam != IntPtr.Zero)
            {
                Log.Info("session ended");
                RestoreAll();
            }
            else
            {
                Log.Info("session end cancelled: re-applying");
                ApplyMode();
            }
            return;
        }
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
