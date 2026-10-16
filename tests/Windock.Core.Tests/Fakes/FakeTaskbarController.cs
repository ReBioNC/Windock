using Windock.Core.Abstractions;

namespace Windock.Core.Tests.Fakes;

/// Records every call; "Set" also records whether the recovery file existed at that moment.
internal sealed class FakeTaskbarController(string stateFile) : ITaskbarController
{
    public List<string> Calls { get; } = new();
    public int State { get; set; }

    public int GetAppBarState() { Calls.Add("Get"); return State; }

    public void SetAppBarState(int state)
    {
        Calls.Add($"Set:{state}" + (File.Exists(stateFile) ? "" : ":nostate"));
        State = state;
    }

    public void HideTaskbarWindows() => Calls.Add("Hide");

    public void ShowTaskbarWindows() => Calls.Add("Show");
}
