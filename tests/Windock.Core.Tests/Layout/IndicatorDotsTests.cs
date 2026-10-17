using Windock.Core.Layout;

namespace Windock.Core.Tests.Layout;

public class IndicatorDotsTests
{
    [Fact]
    public void No_windows_no_dots() => Assert.Empty(IndicatorDots.Offsets(0));

    [Fact]
    public void One_window_one_centered_dot() => Assert.Equal(new[] { 0.0 }, IndicatorDots.Offsets(1));

    [Fact]
    public void Two_windows_two_dots_around_center() => Assert.Equal(new[] { -3.5, 3.5 }, IndicatorDots.Offsets(2));

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void Three_or_more_windows_three_dots(int windows) => Assert.Equal(new[] { -7.0, 0.0, 7.0 }, IndicatorDots.Offsets(windows));
}
