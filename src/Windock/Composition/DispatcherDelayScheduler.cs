using System.Diagnostics;
using System.Windows.Threading;
using Windock.Core.Abstractions;

namespace Windock.Composition;

internal sealed class DispatcherDelayScheduler(Dispatcher dispatcher) : IDelayScheduler
{
    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
        return new Cancel(timer);
    }

    private sealed class Cancel(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}

internal sealed class StopwatchClock : IClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();

    public TimeSpan Now => _watch.Elapsed;
}
