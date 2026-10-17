using System.Windows;

namespace Windock.Composition;

/// One Windock per user session; a second launch asks the running one to open Settings.
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Windock.SingleInstance";
    private const string EventName = @"Local\Windock.Activate";
    private const string ExitEventName = @"Local\Windock.Exit";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly RegisteredWaitHandle _wait;
    private readonly EventWaitHandle _exit;
    private readonly RegisteredWaitHandle _exitWait;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var dispatcher = Application.Current.Dispatcher;
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate,
            (_, _) => dispatcher.BeginInvoke(() => ActivationRequested?.Invoke()), null, Timeout.Infinite, executeOnlyOnce: false);
        _exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exit,
            (_, _) => dispatcher.BeginInvoke(() => ExitRequested?.Invoke()), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// Another launch asked this instance to quit (installer/updater, "Windock.exe --exit").
    public event Action? ExitRequested;

    public event Action? ActivationRequested;

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew) return new SingleInstance(mutex);
        mutex.Dispose();
        return null;
    }

    public static void SignalExisting(bool exit = false)
    {
        if (EventWaitHandle.TryOpenExisting(exit ? ExitEventName : EventName, out var evt))
        {
            using (evt) evt.Set();
        }
    }

    public void Dispose()
    {
        _wait.Unregister(null);
        _exitWait.Unregister(null);
        _activate.Dispose();
        _exit.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
