using Windock.Core.Island;
using Windock.Core.Tests.Fakes;

namespace Windock.Core.Tests.Island;

public class TasksAndNotesTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 14, 0, 0);

    [Theory]
    [InlineData("Call mom 14:30", "Call mom", 14, 30)]
    [InlineData("Call mom 9.15", "Call mom", 9, 15)]
    [InlineData("Call mom 9pm", "Call mom", 21, 0)]
    [InlineData("Lunch 12:00 am", "Lunch", 0, 0)]
    [InlineData("Lunch 12 pm", "Lunch", 12, 0)]
    public void A_time_at_the_end_becomes_the_reminder(string input, string text, int hour, int minute)
    {
        var (t, due) = TaskList.ParseInput(input);

        Assert.Equal(text, t);
        Assert.Equal(new TimeSpan(hour, minute, 0), due);
    }

    [Theory]
    [InlineData("Buy 2")]          // a bare number is not a time
    [InlineData("Meet at 25:00")]  // not a time of day
    [InlineData("14:30")]          // nothing to do
    [InlineData("Plain task")]
    public void Other_text_stays_as_it_is(string input)
    {
        var (t, due) = TaskList.ParseInput(input);

        Assert.Equal(input, t);
        Assert.Null(due);
    }

    [Fact]
    public void The_list_shows_open_tasks_by_time_then_todays_finished_ones()
    {
        var tasks = new[]
        {
            new IslandTask("a", "later", new TimeSpan(18, 0, 0), false, Now),
            new IslandTask("b", "done today", null, true, Now),
            new IslandTask("c", "soon", new TimeSpan(15, 0, 0), false, Now),
            new IslandTask("d", "done yesterday", null, true, Now.AddDays(-1)),
            new IslandTask("e", "open from yesterday", null, false, Now.AddDays(-1)),
        };

        Assert.Equal(new[] { "c", "a", "e", "b" }, TaskList.Visible(tasks, Now).Select(t => t.Id));
        Assert.DoesNotContain(TaskList.Prune(tasks, Now), t => t.Id == "d");
    }

    [Fact]
    public void Reminders_fire_once_for_todays_open_tasks_whose_time_came()
    {
        var tasks = new[]
        {
            new IslandTask("due", "Call mom", new TimeSpan(13, 59, 0), false, Now),
            new IslandTask("later", "Gym", new TimeSpan(18, 0, 0), false, Now),
            new IslandTask("reminded", "Old", new TimeSpan(9, 0, 0), false, Now, Reminded: true),
            new IslandTask("done", "Done", new TimeSpan(9, 0, 0), true, Now),
            new IslandTask("yesterday", "Y", new TimeSpan(9, 0, 0), false, Now.AddDays(-1)),
        };

        Assert.Equal("due", Assert.Single(TaskList.DueReminders(tasks, Now)).Id);
    }

    [Fact]
    public void Tasks_and_notes_round_trip()
    {
        var tasks = new[] { new IslandTask("a", "Call mom", new TimeSpan(14, 30, 0), false, Now, true) };

        Assert.Equal(tasks, TaskList.Parse(TaskList.Serialize(tasks)));
        Assert.Equal(new[] { "one", "two\nlines" }, NoteBook.Parse(NoteBook.Serialize(new[] { "one", "two\nlines" })));
        Assert.Equal(new[] { "" }, NoteBook.Parse("broken"));
        Assert.Empty(TaskList.Parse("broken"));
    }

    [Fact]
    public void A_held_card_stays_open_when_the_pointer_leaves()
    {
        var scheduler = new ManualDelayScheduler();
        var model = new IslandModel(scheduler, () => TimeSpan.FromSeconds(2));
        model.PointerEntered(true);
        model.Hold(true);
        model.PointerLeft();

        Assert.Empty(scheduler.Active);
        model.Hold(false);
        scheduler.RunAll();
        Assert.False(model.Expanded);
    }
}
