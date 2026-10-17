using System.Runtime;
using System.Windows.Threading;

namespace Windock.Composition;

/// Gives memory back to Windows after one-off work (startup, closing Pengaturan) so the idle dock stays small.
internal static class MemoryTrim
{
    public static void After(TimeSpan delay)
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        };
        timer.Start();
    }
}
