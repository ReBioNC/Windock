using Windock.Core.Apps;
using Windock.Core.Settings;

namespace Windock.Core.Tests.Apps;

public class AttentionTrackerTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Request_marks_once()
    {
        var t = new AttentionTracker();

        Assert.True(t.Request("a"));
        Assert.False(t.Request("a"));
        Assert.True(t.Has("a"));
    }

    [Fact]
    public void Clear_unmarks()
    {
        var t = new AttentionTracker();
        t.Request("a");

        Assert.True(t.Clear("a"));
        Assert.False(t.Has("a"));
        Assert.False(t.Clear("a"));
    }

    [Fact]
    public void Once_bursts_a_single_time()
    {
        var t = new AttentionTracker();
        t.Request("a");

        Assert.Equal(new[] { "a" }, t.DueBursts(S(0), AttentionRepeat.Once));
        Assert.Empty(t.DueBursts(S(30), AttentionRepeat.Once));
        Assert.False(t.HasPendingBursts(AttentionRepeat.Once));
        Assert.True(t.Has("a"));  // still marked until opened
    }

    [Fact]
    public void UntilOpened_bursts_every_interval()
    {
        var t = new AttentionTracker();
        t.Request("a");

        Assert.Equal(new[] { "a" }, t.DueBursts(S(0), AttentionRepeat.UntilOpened));
        Assert.Empty(t.DueBursts(S(9), AttentionRepeat.UntilOpened));
        Assert.Equal(new[] { "a" }, t.DueBursts(S(10), AttentionRepeat.UntilOpened));
        Assert.True(t.HasPendingBursts(AttentionRepeat.UntilOpened));
    }

    [Fact]
    public void Retain_drops_keys_that_left_the_dock()
    {
        var t = new AttentionTracker();
        t.Request("a");
        t.Request("b");

        t.Retain(new[] { "b", "c" });

        Assert.Equal(new[] { "b" }, t.Keys);
    }

    [Fact]
    public void Nothing_pending_without_requests() => Assert.False(new AttentionTracker().HasPendingBursts(AttentionRepeat.UntilOpened));
}
