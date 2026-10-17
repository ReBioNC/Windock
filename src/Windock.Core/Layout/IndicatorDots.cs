namespace Windock.Core.Layout;

/// Running-indicator dots: one per open window, at most three, centred under the icon.
public static class IndicatorDots
{
    public const int MaxDots = 3;
    public const double Pitch = DockMetrics.IndicatorDiameter + 3;

    /// Horizontal offsets of each dot centre from the icon centre.
    public static IReadOnlyList<double> Offsets(int windowCount)
    {
        var n = Math.Min(Math.Max(windowCount, 0), MaxDots);
        return Enumerable.Range(0, n).Select(i => (i - (n - 1) / 2.0) * Pitch).ToArray();
    }
}
