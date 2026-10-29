using Windock.Core.Island;
using Windock.Core.Settings;

namespace Windock.Core.Tests.Island;

public class ClipboardAndShelfTests
{
    [Fact]
    public void Newest_copy_comes_first_and_copying_again_moves_it_up()
    {
        var history = new ClipboardHistory();
        history.Add("one");
        history.Add("two");
        history.Add("one");

        Assert.Equal(new[] { "one", "two" }, history.Items.Select(i => i.Text));
    }

    [Fact]
    public void Blank_and_repeated_copies_change_nothing()
    {
        var history = new ClipboardHistory();
        history.Add("one");

        Assert.False(history.Add("   "));
        Assert.False(history.Add("one"));
        Assert.Single(history.Items);
    }

    [Fact]
    public void History_is_short_and_long_texts_are_cut()
    {
        var history = new ClipboardHistory();
        for (var i = 0; i < 30; i++) history.Add($"text {i}");
        history.Add(new string('x', 20_000));

        Assert.Equal(ClipboardHistory.MaxItems, history.Items.Count);
        Assert.Equal(ClipboardHistory.MaxLength, history.Items[0].Text!.Length);
    }

    [Fact]
    public void Pictures_join_the_history_and_the_same_picture_moves_up()
    {
        var history = new ClipboardHistory();
        history.Add("text");
        Assert.True(history.AddImage("a", new object(), 10, 20));
        history.Add("more");
        Assert.True(history.AddImage("a", new object(), 10, 20));

        Assert.Equal(3, history.Items.Count);
        Assert.Equal("a", history.Items[0].ImageKey);
        Assert.False(history.AddImage("a", new object(), 10, 20));
        Assert.False(history.AddImage("", new object(), 10, 20));
    }

    [Fact]
    public void Only_the_newest_pictures_are_kept_and_they_can_be_dropped()
    {
        var history = new ClipboardHistory();
        for (var i = 0; i < 8; i++) history.AddImage($"img {i}", new object(), 1, 1);
        history.Add("text");

        Assert.Equal(ClipboardHistory.MaxImages, history.Items.Count(i => i.IsImage));
        Assert.Equal("img 7", history.Items[1].ImageKey);

        history.RemoveImages();
        Assert.Equal(new[] { "text" }, history.Items.Select(i => i.Text));
    }

    [Fact]
    public void Keeping_images_is_on_by_default_and_saved()
    {
        Assert.True(SettingsDefaults.Create().Island.ClipboardImages);
        var s = SettingsDefaults.Create();
        s = s with { Island = s.Island with { ClipboardImages = false } };

        Assert.False(SettingsJson.Parse(SettingsJson.Serialize(s)).Island.ClipboardImages);
    }

    [Fact]
    public void Previews_are_one_line() =>
        Assert.Equal("line one line two", ClipboardHistory.Preview("line one\r\n\r\n  line two "));

    [Fact]
    public void Shelf_keeps_order_skips_duplicates_and_drops_missing_files()
    {
        var shelf = Shelf.Add(new[] { @"C:\a.txt" }, new[] { @"C:\b.png", @"c:\A.TXT" });

        Assert.Equal(new[] { @"C:\a.txt", @"C:\b.png" }, shelf);
        Assert.Equal(new[] { @"C:\b.png" }, Shelf.Prune(shelf, p => p.EndsWith(".png")));
        Assert.Equal(shelf, Shelf.Parse(Shelf.Serialize(shelf)));
        Assert.Empty(Shelf.Parse("nope"));
    }

    [Fact]
    public void A_full_shelf_keeps_the_newest()
    {
        var shelf = Shelf.Add(Array.Empty<string>(), Enumerable.Range(0, 30).Select(i => $@"C:\{i}.txt"));

        Assert.Equal(Shelf.MaxItems, shelf.Count);
        Assert.Equal(@"C:\29.txt", shelf[^1]);
    }
}
