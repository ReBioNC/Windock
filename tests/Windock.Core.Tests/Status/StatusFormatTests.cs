using Windock.Core.Status;

namespace Windock.Core.Tests.Status;

public class StatusFormatTests
{
    private static readonly DateTime Afternoon = new(2026, 10, 18, 13, 5, 9);

    [Theory]
    [InlineData(true, false, "13:05")]
    [InlineData(true, true, "13:05:09")]
    [InlineData(false, false, "1:05 PM")]
    [InlineData(false, true, "1:05:09 PM")]
    public void Clock_formats(bool h24, bool seconds, string expected) => Assert.Equal(expected, StatusFormat.Clock(Afternoon, h24, seconds));

    [Fact]
    public void Dates_are_indonesian()
    {
        Assert.Equal("Min, 18 Okt", StatusFormat.ShortDate(Afternoon));
        Assert.Equal("Minggu, 18 Oktober 2026", StatusFormat.LongDate(Afternoon));
    }

    [Fact]
    public void Next_tick_is_the_next_minute_or_second()
    {
        Assert.Equal(TimeSpan.FromSeconds(51), StatusFormat.UntilNextTick(Afternoon, showSeconds: false));
        Assert.Equal(TimeSpan.FromMilliseconds(750), StatusFormat.UntilNextTick(Afternoon.AddMilliseconds(250), showSeconds: true));
    }

    [Theory]
    [InlineData(80, true, true, null, "Baterai 80% · mengisi daya")]
    [InlineData(100, false, true, null, "Baterai penuh · tersambung ke listrik")]
    [InlineData(55, false, false, 130.0, "Baterai 55% · sisa 2 j 10 m")]
    [InlineData(55, false, false, null, "Baterai 55%")]
    public void Battery_tooltip(int percent, bool charging, bool plugged, double? minutes, string expected) =>
        Assert.Equal(expected, StatusFormat.BatteryTooltip(percent, charging, plugged, minutes is { } m ? TimeSpan.FromMinutes(m) : null));

    [Theory]
    [InlineData(0x0409, "EN")]
    [InlineData(0x0421, "ID")]
    [InlineData(0x0411, "JA")]
    public void Language_codes(int langId, string expected) => Assert.Equal(expected, StatusFormat.LanguageCode(langId));

    [Fact]
    public void Volume_glyph_follows_level_and_mute()
    {
        Assert.Equal(StatusGlyphs.Mute, StatusGlyphs.Volume(0.5, muted: true));
        Assert.Equal(StatusGlyphs.Mute, StatusGlyphs.Volume(0, muted: false));
        Assert.NotEqual(StatusGlyphs.Volume(0.2, false), StatusGlyphs.Volume(0.9, false));
        Assert.Equal("Volume 45%", StatusFormat.VolumeTooltip(0.45, false));
    }

    [Fact]
    public void Battery_glyph_rounds_to_tens()
    {
        Assert.Equal("", StatusGlyphs.BatteryLevel(3, false));
        Assert.Equal("", StatusGlyphs.BatteryLevel(100, false));
        Assert.Equal("", StatusGlyphs.BatteryLevel(100, true));
    }
}
