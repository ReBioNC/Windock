using Windock.Core.Settings;

namespace Windock.Core.Apps;

/// Pure edits of the pinned-app list used by drag and drop, menus and "Pin ke Windock".
public static class PinListEditor
{
    private const string AppsFolderPrefix = @"shell:AppsFolder\";

    /// Programs and shortcuts can be pinned; documents and folders cannot (yet).
    public static bool CanPin(string path) =>
        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase);

    /// Inserts at index (clamped); an app that is already pinned is moved there instead of duplicated.
    public static IReadOnlyList<PinEntry> Insert(IReadOnlyList<PinEntry> pins, PinEntry pin, int index, Func<PinEntry, AppId> idOf)
    {
        var id = idOf(pin);
        var list = pins.ToList();
        var existing = list.FindIndex(p => idOf(p) == id);
        if (existing >= 0) return Move(pins, existing, index);
        list.Insert(Math.Clamp(index, 0, list.Count), pin);
        return list;
    }

    /// Moves the pin at from to the gap index to, counted in the list before the move (0 = before the first).
    public static IReadOnlyList<PinEntry> Move(IReadOnlyList<PinEntry> pins, int from, int to)
    {
        var list = pins.ToList();
        var pin = list[from];
        list.RemoveAt(from);
        var target = Math.Clamp(to > from ? to - 1 : to, 0, list.Count);
        list.Insert(target, pin);
        return list;
    }

    public static IReadOnlyList<PinEntry> Remove(IReadOnlyList<PinEntry> pins, AppId id, Func<PinEntry, AppId> idOf) =>
        pins.Where(p => idOf(p) != id).ToList();
}
