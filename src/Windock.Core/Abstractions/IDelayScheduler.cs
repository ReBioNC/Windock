namespace Windock.Core.Abstractions;

/// Runs an action once after a delay; disposing the handle cancels it.
public interface IDelayScheduler
{
    IDisposable Schedule(TimeSpan delay, Action action);
}
