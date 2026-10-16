using Windock.Core.Abstractions;

namespace Windock.Core.Apps;

public enum DockItemKind { App, Separator, RecycleBin }

public sealed record DockItem(
    string Key,
    DockItemKind Kind,
    AppId? AppId,
    string DisplayName,
    string? LaunchTarget,
    string? IconPath,
    bool IsPinned,
    IReadOnlyList<WindowInfo> Windows,
    bool IsActive)
{
    public const string PinnedSeparatorKey = "sep:pinned";
    public const string TrashSeparatorKey = "sep:trash";
    public const string RecycleBinKey = "recyclebin";

    public bool IsRunning => Windows.Count > 0;

    public bool Equals(DockItem? other) =>
        other is not null
        && Key == other.Key && Kind == other.Kind && AppId == other.AppId && DisplayName == other.DisplayName
        && LaunchTarget == other.LaunchTarget && IconPath == other.IconPath && IsPinned == other.IsPinned
        && IsActive == other.IsActive && Windows.SequenceEqual(other.Windows);

    public override int GetHashCode() => HashCode.Combine(Key, Kind, AppId, IsPinned, IsActive, Windows.Count);
}

public sealed record DockItemsChangedEventArgs(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Updated);
