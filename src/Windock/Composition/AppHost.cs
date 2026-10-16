using System.IO;
using System.Windows;
using Windock.Core.Apps;
using Windock.Core.Layout;
using Windock.Core.Logging;
using Windock.Core.Settings;
using Windock.Dock;
using Windock.Interop;
using Windock.Tray;

namespace Windock.Composition;

/// Wires every component together and owns the app lifecycle (ARCHITECTURE §9).
internal sealed class AppHost
{
    public static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Windock");

    private bool _paused;
    private bool _fullscreen;
    private bool _exiting;

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

    /// --dump-windows: log what the tracker and resolver see, for manual verification.
    private void DumpDiagnostics(IEnumerable<string> extraPins)
    {
        foreach (var w in Windows.Snapshot())
            Log.Info($"window 0x{w.Handle:X} app={w.AppId} aumid={ShellInterop.WindowAppUserModelId(w.Handle)} name='{w.DisplayName}' title='{w.Title}' min={w.IsMinimized}");
        var pins = DefaultPins.Detect(File.Exists, Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        foreach (var pin in pins.Concat(extraPins.Select(p => new PinEntry(p))))
        {
            var r = PinResolver.Resolve(pin);
            Log.Info($"pin {pin.Path} -> app={r.AppId} name='{r.DisplayName}' exists={r.TargetExists}");
        }
    }

    public void Start(string[] args)
    {
        Log.Info($"Windock starting (args: {string.Join(' ', args)})");
        Settings = new SettingsStore(DataDirectory, new DispatcherDelayScheduler(Application.Current.Dispatcher), Log);
        Settings.Load();
        Windows = new WindowTracker(Log);
        PinResolver = new PinResolver(Log);
        if (args.Contains("--dump-windows"))
        {
            DumpDiagnostics(args.Where(a => !a.StartsWith("--")));
            Application.Current.Shutdown();
            return;
        }
        Tray = new TrayIcon(OpenSettings, TogglePause, Exit);

        Icons = new IconProvider(Log, Application.Current.Dispatcher);
        Model = new DockModel(Windows, PinResolver);
        Model.SetPins(Settings.Current.Pins);
        Backdrop = new BackdropWindow();
        Dock = new DockController(Model, new StopwatchClock(), Settings, Icons, new DockWindow(), Backdrop, Log);
        Settings.Changed += (_, e) => Dock.ApplySettings(e);
        Launcher = new AppLauncher(Log, (title, text) => Tray?.ShowBalloon(title, text));
        var menus = new DockContextMenu(Launcher, Settings, PinResolver, OpenSettings, Exit);
        Dock.ItemClicked += (item, shift) => Launcher.Execute(DockClickPolicy.Decide(item, Windows.Foreground, shift), item);
        Dock.ContextRequested += item =>
        {
            var menu = menus.Build(item);
            menu.PlacementTarget = Dock.MenuAnchor;
            menu.IsOpen = true;
        };
        Dock.Start();

        AppBar = new AppBarService(Log);
        AppBar.Reserved += bottomPx => Dock.SetBottomEdge(bottomPx / Dock.DpiScale);
        AppBar.FullscreenChanged += fullscreen =>
        {
            _fullscreen = fullscreen;
            Log.Info($"fullscreen app: {fullscreen}");
            UpdateDockVisibility();
        };
        Settings.Changed += (_, e) =>
        {
            if (e.Sections.HasFlag(SettingsSection.Appearance) && e.New.Appearance.IconSize != e.Old.Appearance.IconSize) ReserveSpace();
        };
        ReserveSpace();

        Messages = new MessageWindow("WindockMessages");
        Messages.Message += OnSystemMessage;
        Log.Info("dock started");
    }

    public void OpenSettings()
    {
        Log.Info("open settings requested");
    }

    public void TogglePause()
    {
        _paused = !_paused;
        Tray?.SetPaused(_paused);
        if (_paused) AppBar?.Release();
        else ReserveSpace();
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

    /// Puts Windows back the way it was. Safe to call repeatedly and from crash handlers.
    public void RestoreAll()
    {
        Log.Info("restore all");
        Try(() => AppBar?.Release());
    }

    private void ReserveSpace()
    {
        if (_paused || Dock is null) return;
        AppBar?.Reserve(DockMetrics.ReservedThickness(Settings.Current.Appearance.IconSize), Dock.DpiScale);
    }

    private void UpdateDockVisibility() => Dock?.SetHidden(_paused || _fullscreen);

    private void OnSystemMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is Native.WM_DISPLAYCHANGE or Native.WM_DPICHANGED)
        {
            Log.Info($"display/settings change 0x{msg:X}");
            Dock?.Relayout();
            ReserveSpace();
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
