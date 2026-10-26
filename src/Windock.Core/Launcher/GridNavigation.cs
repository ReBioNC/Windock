namespace Windock.Core.Launcher;

public enum GridMove { Left, Right, Up, Down, First, Last }

/// Arrow-key movement in the launcher's icon grid (items fill rows left to right).
public static class GridNavigation
{
    public static int Move(int index, int count, int columns, GridMove move)
    {
        if (count <= 0) return -1;
        columns = Math.Max(1, columns);
        index = Math.Clamp(index, 0, count - 1);
        return move switch
        {
            GridMove.Left => Math.Max(0, index - 1),
            GridMove.Right => Math.Min(count - 1, index + 1),
            GridMove.Up => index - columns >= 0 ? index - columns : index,
            GridMove.Down => index + columns < count ? index + columns
                : index / columns < (count - 1) / columns ? count - 1   // a shorter last row: land on its last item
                : index,
            GridMove.First => 0,
            GridMove.Last => count - 1,
            _ => index,
        };
    }
}
