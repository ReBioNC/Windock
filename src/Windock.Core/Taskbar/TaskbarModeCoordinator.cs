using Windock.Core.Abstractions;
using Windock.Core.Settings;

namespace Windock.Core.Taskbar;

/// Applies the taskbar mode and guarantees the original taskbar state can always be restored (ARCHITECTURE §7.2).
public sealed class TaskbarModeCoordinator(ITaskbarController controller, RecoveryStateStore store, int currentPid)
{
    private int? _original;

    public TaskbarMode? CurrentMode { get; private set; }

    /// Undo whatever a previous, killed Windock session left behind.
    public void RecoverFromPreviousSession()
    {
        if (store.Read() is not { TaskbarModified: true } stale) return;
        controller.ShowTaskbarWindows();
        controller.SetAppBarState(stale.OriginalAbState);
        store.Clear();
    }

    public void Apply(TaskbarMode mode)
    {
        if (mode == TaskbarMode.Visible)
        {
            Restore();
            return;
        }

        if (_original is not { } original)
        {
            original = controller.GetAppBarState();
            store.Write(new TaskbarRecoveryState(true, original, currentPid));  // before touching the taskbar
            _original = original;
            controller.SetAppBarState(original | TaskbarStates.AutoHide);
        }
        else if (CurrentMode == TaskbarMode.Hidden && mode == TaskbarMode.AutoHide)
        {
            controller.ShowTaskbarWindows();
        }

        if (mode == TaskbarMode.Hidden && CurrentMode != TaskbarMode.Hidden) controller.HideTaskbarWindows();
        CurrentMode = mode;
    }

    /// After Explorer restarts or the machine resumes: put the current mode back in place.
    public void Reapply()
    {
        if (_original is not { } original || CurrentMode is null or TaskbarMode.Visible) return;
        controller.SetAppBarState(original | TaskbarStates.AutoHide);
        if (CurrentMode == TaskbarMode.Hidden) controller.HideTaskbarWindows();
    }

    public void Restore()
    {
        if (_original is { } original)
        {
            controller.ShowTaskbarWindows();
            controller.SetAppBarState(original);
            store.Clear();
            _original = null;
        }
        CurrentMode = TaskbarMode.Visible;
    }
}
