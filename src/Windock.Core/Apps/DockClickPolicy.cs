namespace Windock.Core.Apps;

public abstract record ClickAction
{
    public sealed record Launch(string Target) : ClickAction;
    public sealed record Activate(nint Hwnd) : ClickAction;
    public sealed record Minimize(nint Hwnd) : ClickAction;
    public sealed record OpenRecycleBin : ClickAction { public static readonly OpenRecycleBin Instance = new(); }
    public sealed record None : ClickAction { public static readonly None Instance = new(); }
    /// Several windows: show their live previews to pick from, like the Windows taskbar.
    public sealed record ShowWindows : ClickAction { public static readonly ShowWindows Instance = new(); }
}

/// What a left click on a dock item does (DESIGN.md §5).
public static class DockClickPolicy
{
    public static ClickAction Decide(DockItem item, nint foreground, bool shift)
    {
        switch (item.Kind)
        {
            case DockItemKind.RecycleBin: return ClickAction.OpenRecycleBin.Instance;
            case DockItemKind.Separator: return ClickAction.None.Instance;
        }

        if (shift || !item.IsRunning)
            return item.LaunchTarget is { } target ? new ClickAction.Launch(target) : ClickAction.None.Instance;

        if (item.Windows.Count > 1) return ClickAction.ShowWindows.Instance;

        return item.Windows.Any(w => w.Handle == foreground)
            ? new ClickAction.Minimize(foreground)
            : new ClickAction.Activate(item.Windows[0].Handle);
    }
}
