using Windock.Core.Settings;
using Windock.Core.Taskbar;
using Windock.Core.Tests.Fakes;

namespace Windock.Core.Tests.Taskbar;

public sealed class TaskbarModeCoordinatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "windock-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTaskbarController _controller;
    private readonly RecoveryStateStore _store;

    public TaskbarModeCoordinatorTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new RecoveryStateStore(_dir);
        _controller = new FakeTaskbarController(Path.Combine(_dir, "state.json")) { State = 2 };
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private TaskbarModeCoordinator New() => new(_controller, _store, currentPid: 1234);

    [Fact]
    public void Hidden_writes_state_before_touching_taskbar()
    {
        New().Apply(TaskbarMode.Hidden);

        Assert.Equal(new[] { "Get", "Set:3", "Hide" }, _controller.Calls);
        Assert.Equal(new TaskbarRecoveryState(true, 2, 1234), _store.Read());
    }

    [Fact]
    public void AutoHide_sets_autohide_without_hiding_windows()
    {
        var c = New();

        c.Apply(TaskbarMode.AutoHide);

        Assert.Equal(new[] { "Get", "Set:3" }, _controller.Calls);
        Assert.Equal(TaskbarMode.AutoHide, c.CurrentMode);
    }

    [Fact]
    public void Restore_shows_and_restores_original_state_then_clears_file()
    {
        var c = New();
        c.Apply(TaskbarMode.Hidden);
        _controller.Calls.Clear();

        c.Restore();

        Assert.Equal(new[] { "Show", "Set:2" }, _controller.Calls);
        Assert.Null(_store.Read());
        Assert.Equal(TaskbarMode.Visible, c.CurrentMode);
    }

    [Fact]
    public void Restore_is_idempotent_and_noop_when_untouched()
    {
        var c = New();

        c.Restore();
        c.Apply(TaskbarMode.Visible);

        Assert.Empty(_controller.Calls);
    }

    [Fact]
    public void Switching_hidden_to_autohide_keeps_original_state()
    {
        var c = New();
        c.Apply(TaskbarMode.Hidden);
        _controller.State = 3;

        c.Apply(TaskbarMode.AutoHide);
        c.Restore();

        Assert.Equal("Set:2", _controller.Calls.Last());
        Assert.Contains("Show", _controller.Calls.Skip(3));
    }

    [Fact]
    public void Switching_autohide_to_hidden_hides_windows()
    {
        var c = New();
        c.Apply(TaskbarMode.AutoHide);
        _controller.Calls.Clear();

        c.Apply(TaskbarMode.Hidden);

        Assert.Equal(new[] { "Hide" }, _controller.Calls);
    }

    [Fact]
    public void Recover_restores_state_left_by_killed_session() // Review Focus 3
    {
        _store.Write(new TaskbarRecoveryState(true, 2, 999));
        _controller.State = 3;

        var recovered = New().RecoverFromPreviousSession();

        Assert.True(recovered);
        Assert.Equal(new[] { "Show", "Set:2" }, _controller.Calls);
        Assert.Null(_store.Read());
    }

    [Fact]
    public void Recover_does_nothing_without_state_file()
    {
        Assert.False(New().RecoverFromPreviousSession());

        Assert.Empty(_controller.Calls);
    }

    [Fact]
    public void Reapply_hides_again_when_mode_is_hidden()
    {
        var c = New();
        c.Apply(TaskbarMode.Hidden);
        _controller.Calls.Clear();

        c.Reapply();

        Assert.Equal(new[] { "Set:3", "Hide" }, _controller.Calls);
    }

    [Fact]
    public void Reapply_does_nothing_when_visible()
    {
        var c = New();

        c.Reapply();

        Assert.Empty(_controller.Calls);
    }

    [Fact]
    public void Sign_out_while_starting_with_windows_leaves_the_taskbar_hidden()
    {
        var c = New();
        c.Apply(TaskbarMode.Hidden);
        _controller.Calls.Clear();

        c.LeaveForNextSession();
        c.Restore();

        Assert.Empty(_controller.Calls);
        Assert.Equal(new TaskbarRecoveryState(true, 2, 1234), _store.Read());
    }

    [Fact]
    public void Next_session_takes_the_hidden_taskbar_over_without_showing_it()
    {
        _store.Write(new TaskbarRecoveryState(true, 2, 999));
        _controller.State = 3;
        var c = New();

        Assert.True(c.RecoverFromPreviousSession(TaskbarMode.Hidden));
        c.Apply(TaskbarMode.Hidden);  // the normal start-up apply is then a no-op

        Assert.Equal(new[] { "Set:3", "Hide" }, _controller.Calls);
        Assert.Equal(new TaskbarRecoveryState(true, 2, 1234), _store.Read());
        _controller.Calls.Clear();
        c.Restore();
        Assert.Equal(new[] { "Show", "Set:2" }, _controller.Calls);  // the original from before Windock, not 3
    }

    [Fact]
    public void Next_session_in_windows_mode_restores_the_original()
    {
        _store.Write(new TaskbarRecoveryState(true, 2, 999));

        Assert.True(New().RecoverFromPreviousSession(keepMode: null));

        Assert.Equal(new[] { "Show", "Set:2" }, _controller.Calls);
        Assert.Null(_store.Read());
    }

    [Fact]
    public void Cancelled_shutdown_reapplies_with_the_same_original()
    {
        var c = New();
        c.Apply(TaskbarMode.Hidden);
        c.LeaveForNextSession();
        c.Restore();
        _controller.State = 3;  // still auto-hidden
        c.Apply(TaskbarMode.Hidden);
        _controller.Calls.Clear();

        c.Restore();

        Assert.Equal(new[] { "Show", "Set:2" }, _controller.Calls);
    }

    [Fact]
    public void Corrupt_state_file_reads_as_null()
    {
        File.WriteAllText(Path.Combine(_dir, "state.json"), "{ broken");

        Assert.Null(_store.Read());
    }
}
