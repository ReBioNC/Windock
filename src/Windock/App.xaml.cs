using System.IO;
using System.Windows;
using System.Windows.Threading;
using Windock.Composition;
using Windock.Core.Logging;

namespace Windock;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppHost? _host;
    private ILog _log = NullLog.Instance;
    private readonly Queue<DateTime> _recentErrors = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var exitRequest = e.Args.Contains("--exit");
        var pinIndex = Array.IndexOf(e.Args, "--pin");
        var pinPath = pinIndex >= 0 && pinIndex + 1 < e.Args.Length ? e.Args[pinIndex + 1] : null;
        _instance = SingleInstance.TryAcquire();
        if (_instance is null || exitRequest)
        {
            if (_instance is null && pinPath is not null) CommandChannel.Send("pin", pinPath);
            else if (_instance is null) SingleInstance.SignalExisting(exit: exitRequest);
            _instance?.Dispose();
            _instance = null;
            Shutdown();
            return;
        }

        _log = new FileLog(Path.Combine(AppHost.DataDirectory, "logs"), () => DateTime.Now);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => OnFatal("unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.Error("unobserved task exception", args.Exception);
            args.SetObserved();
        };
        DispatcherUnhandledException += OnDispatcherException;

        _host = new AppHost(_log);
        _instance.ActivationRequested += () =>
        {
            _log.Info("activation requested by second instance");
            _host.OpenSettings();
        };
        _instance.ExitRequested += () =>
        {
            _log.Info("exit requested by another launch");
            _host.Exit();
        };
        _host.Start(e.Args);
        if (e.Args.Contains("--settings")) _host.OpenSettings();
        if (pinPath is not null) _host.PinPath(pinPath);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.RestoreAll();
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// Recoverable UI errors keep the dock alive; a burst (>3 in 10 s) restores Windows and quits.
    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        _log.Error("dispatcher exception", args.Exception);
        var now = DateTime.UtcNow;
        _recentErrors.Enqueue(now);
        while (_recentErrors.Count > 0 && now - _recentErrors.Peek() > TimeSpan.FromSeconds(10)) _recentErrors.Dequeue();
        args.Handled = true;
        if (_recentErrors.Count > 3)
        {
            _log.Error("too many errors, shutting down");
            _host?.Exit();
        }
    }

    private void OnFatal(string what, Exception? ex)
    {
        _log.Error(what, ex);
        try
        {
            _host?.RestoreAll();
        }
        catch (Exception restoreError)
        {
            _log.Error("restore during crash failed", restoreError);
        }
    }
}
