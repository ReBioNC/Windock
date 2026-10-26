using Windock.Core.Abstractions;
using Windock.Core.Settings;

namespace Windock.Core.Taskbar;

/// Applies the taskbar mode and guarantees the original taskbar state can always be restored (ARCHITECTURE §7.2).
public sealed class TaskbarModeCoordinator(ITaskbarController controller, RecoveryStateStore store, int currentPid)
{
    private int? _original;

    public TaskbarMode? CurrentMode { get; private set; }

    /// Deals with whatever a previous Windock session left behind (killed, or signed out while starting with
    /// Windows). keepMode Hidden/AutoHide: the taskbar is already the way this session wants it, so it is
    /// taken over as-is (no flash of the full taskbar at sign-in) and its recorded original state is kept for the
    /// eventual restore. Otherwise the original state is put back. True when a previous state was found.
    public bool RecoverFromPreviousSession(TaskbarMode? keepMode = null)
    {
        if (store.Read() is not { TaskbarModified: true } stale) return false;
        if (keepMode is TaskbarMode.Hidden or TaskbarMode.AutoHide)
        {
            _original = stale.OriginalAbState;
            store.Write(new TaskbarRecoveryState(true, stale.OriginalAbState, currentPid));
            controller.SetAppBarState(stale.OriginalAbState | TaskbarStates.AutoHide);
            if (keepMode == TaskbarMode.Hidden) controller.HideTaskbarWindows();
            CurrentMode = keepMode;
            return true;
        }
        controller.ShowTaskbarWindows();
        controller.SetAppBarState(stale.OriginalAbState);
        store.Clear();
        return true;
    }

    /// Signing out while Windock starts with Windows: leave the taskbar auto-hidden and the recovery state in
    /// place, so the next sign-in starts without the full taskbar and RecoverFromPreviousSession takes it over.
    /// Restore() does nothing until the next Apply (a cancelled shutdown re-applies with the same original).
    public void LeaveForNextSession() => _leftForNextSession = true;

    private bool _leftForNextSession;

    public void Apply(TaskbarMode mode)
    {
        _leftForNextSession = false;
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
        if (_leftForNextSession)
        {
            CurrentMode = TaskbarMode.Visible;
            return;
        }
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
