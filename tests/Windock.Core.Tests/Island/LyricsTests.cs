using Windock.Core.Island;

namespace Windock.Core.Tests.Island;

public class LyricsTests
{
    private const string Song = "[ar:Ariana Grande]\n[00:12.50]It's so strange\n[00:15.10]\n[00:16.00][01:16.00]Not that I miss you\n";

    [Fact]
    public void Lrc_lines_are_read_in_time_order_with_gaps_and_repeats()
    {
        var lines = Lrc.Parse(Song);

        Assert.Equal(4, lines.Count);
        Assert.Equal(new LyricLine(TimeSpan.FromSeconds(12.5), "It's so strange"), lines[0]);
        Assert.Equal("♪", lines[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(76), lines[3].Time);
    }

    [Fact]
    public void The_current_line_is_the_last_one_that_started()
    {
        var lines = Lrc.Parse(Song);

        Assert.Equal(-1, Lrc.LineAt(lines, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, Lrc.LineAt(lines, TimeSpan.FromSeconds(13)));
        Assert.Equal(2, Lrc.LineAt(lines, TimeSpan.FromSeconds(40)));
        Assert.Equal(3, Lrc.LineAt(lines, TimeSpan.FromSeconds(80)));
    }

    [Theory]
    [InlineData("twilight zone", "Ariana Grande", "twilight zone", "Ariana Grande")]
    [InlineData("Stephen Sanchez - Until I Found You (Official Video)", "StephenSanchezVEVO", "Until I Found You", "Stephen Sanchez")]
    [InlineData("Until I Found You [Lyrics]", "Stephen Sanchez - Topic", "Until I Found You", "Stephen Sanchez")]
    [InlineData("Blinding Lights (Official Audio)", "The Weeknd", "Blinding Lights", "The Weeknd")]
    public void Video_titles_are_cleaned_up(string title, string artist, string cleanTitle, string cleanArtist) =>
        Assert.Equal((cleanTitle, cleanArtist), LyricsQuery.Clean(title, artist));

    [Fact]
    public void Urls_carry_only_the_song_details()
    {
        var url = LyricsQuery.GetUrl("Until I Found You", "Stephen Sanchez", "", TimeSpan.FromSeconds(177.4));

        Assert.Equal("https://lrclib.net/api/get?track_name=Until%20I%20Found%20You&artist_name=Stephen%20Sanchez&duration=177", url);
    }

    [Fact]
    public void Replies_from_get_and_search_are_read()
    {
        Assert.Equal(4, LyricsQuery.ParseResponse("{\"syncedLyrics\":\"" + Song.Replace("\n", "\\n") + "\"}")?.Count);
        Assert.Single(LyricsQuery.ParseResponse("[{\"syncedLyrics\":null},{\"syncedLyrics\":\"[00:01.00]hi\"}]")!);
        Assert.Null(LyricsQuery.ParseResponse("{\"plainLyrics\":\"no timing\",\"syncedLyrics\":null}"));
        Assert.Null(LyricsQuery.ParseResponse("not json"));
    }
}
