namespace Windock.Core.Layout;

/// Fixed dock geometry from DESIGN.md §2–3, in DIP.
public static class DockMetrics
{
    public const double PlatePaddingV = 6;
    public const double IndicatorSpace = 6;
    public const double EdgeGap = 4;
    public const double IconSpacing = 8;
    public const double PlatePaddingH = 8;
    public const double SeparatorSlotWidth = 17;
    public const double PlateRadius = 16;
    public const double IndicatorDiameter = 4;
    public const double IndicatorOffset = 3;
    public const double TooltipSpace = 16;
    public const double ScreenMargin = 8;
    public const double ReachFactor = 2.5;
    public const double SeparatorHeightFactor = 0.6;
    public const double FixedSpacing = 4;

    public static double PlateHeight(double icon) => icon + 2 * PlatePaddingV + IndicatorSpace;

    /// Room above the plate fits the magnified icon or a bouncing one, whichever is taller.
    public static double WindowHeight(double icon, double magnified) =>
        PlateHeight(icon) + EdgeGap + Math.Max(magnified - icon, icon * Animation.BounceAnimator.HeightFactor) + TooltipSpace;

    public static double ReservedThickness(double icon) => PlateHeight(icon) + EdgeGap;
}
