using Windock.Core.Apps;

namespace Windock.Core.Abstractions;

public sealed record WindowInfo(nint Handle, AppId AppId, string DisplayName, string Title, string? ExePath, bool IsMinimized);

/// Top-level application windows, kept current from window events.
public interface IWindowSource
{
    IReadOnlyList<WindowInfo> Snapshot();

    nint Foreground { get; }

    event EventHandler<WindowInfo>? WindowAdded;
    event EventHandler<nint>? WindowRemoved;
    event EventHandler<WindowInfo>? WindowChanged;
    event EventHandler<nint>? ForegroundChanged;
}
