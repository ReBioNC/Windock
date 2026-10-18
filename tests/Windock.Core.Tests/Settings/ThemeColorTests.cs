using Windock.Core.Settings;

namespace Windock.Core.Tests.Settings;

public class ThemeColorTests
{
    [Fact]
    public void Parses_and_prints_hex()
    {
        Assert.True(ThemeColor.TryParse("#2f6fe4", out var c));
        Assert.Equal(new ThemeColor(0x2F, 0x6F, 0xE4), c);
        Assert.Equal("#2F6FE4", c.ToHex());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    public void Rejects_invalid_text(string? text) => Assert.False(ThemeColor.TryParse(text, out _));

    [Fact]
    public void Darken_mixes_toward_black()
    {
        var c = new ThemeColor(200, 100, 50);

        Assert.Equal(c, c.Darken(0));
        Assert.Equal(new ThemeColor(100, 50, 25), c.Darken(0.5));
        Assert.Equal(new ThemeColor(0, 0, 0), c.Darken(1));
    }
}
