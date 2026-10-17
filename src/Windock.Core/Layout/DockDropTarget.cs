namespace Windock.Core.Layout;

/// Where a dragged app lands among the pinned icons, and when a drag means "remove from dock".
public static class DockDropTarget
{
    public const double RemoveDistance = 80;

    /// Gap index: the number of pinned icons whose centre is left of the cursor.
    public static int IndexFor(IReadOnlyList<double> pinnedCenters, double cursorX) => pinnedCenters.Count(c => c < cursorX);

    public static bool IsRemoveGesture(double plateTop, double cursorY) => cursorY < plateTop - RemoveDistance;
}
