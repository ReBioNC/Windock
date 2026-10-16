namespace Windock.Core.Abstractions;

public static class TaskbarStates
{
    public const int AutoHide = 0x1;
}

/// The Windows taskbar: its AppBar state (ABM_GETSTATE/ABM_SETSTATE) and window visibility.
public interface ITaskbarController
{
    int GetAppBarState();
    void SetAppBarState(int state);
    void HideTaskbarWindows();
    void ShowTaskbarWindows();
}
