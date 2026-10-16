using Windock.Core.Animation;

namespace Windock.Core.Tests.Animation;

public class EasingTests
{
    [Fact]
    public void OutCubic_values()
    {
        Assert.Equal(0, Easing.OutCubic(0));
        Assert.Equal(1, Easing.OutCubic(1));
        Assert.Equal(0.875, Easing.OutCubic(0.5), 6);
    }

    [Fact]
    public void InCubic_values()
    {
        Assert.Equal(0.125, Easing.InCubic(0.5), 6);
        Assert.Equal(1, Easing.InCubic(2));
        Assert.Equal(0, Easing.InCubic(-1));
    }
}
