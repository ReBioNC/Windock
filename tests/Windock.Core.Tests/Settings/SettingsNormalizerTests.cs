using Windock.Core.Settings;

namespace Windock.Core.Tests.Settings;

public class SettingsNormalizerTests
{
    private static DockSettings WithAppearance(Func<AppearanceSettings, AppearanceSettings> change)
    {
        var d = SettingsDefaults.Create();
        return d with { Appearance = change(d.Appearance) };
    }

    [Theory]
    [InlineData(10, 32)]
    [InlineData(200, 96)]
    [InlineData(64, 64)]
    public void Icon_size_is_clamped(int input, int expected)
    {
        var s = SettingsNormalizer.Normalize(WithAppearance(a => a with { IconSize = input, Magnification = new(true, 128) }));

        Assert.Equal(expected, s.Appearance.IconSize);
    }

    [Theory]
    [InlineData(48, 40, 48)]
    [InlineData(48, 300, 128)]
    [InlineData(96, 80, 96)]
    public void Magnification_size_is_clamped_between_icon_size_and_128(int icon, int mag, int expected)
    {
        var s = SettingsNormalizer.Normalize(WithAppearance(a => a with { IconSize = icon, Magnification = new(true, mag) }));

        Assert.Equal(expected, s.Appearance.Magnification.Size);
    }

    [Theory]
    [InlineData(1.5, BlurMode.Standard, 1.0)]
    [InlineData(-1, BlurMode.Standard, 0.0)]
    [InlineData(0.1, BlurMode.Standard, 0.1)]
    [InlineData(0.1, BlurMode.Off, 0.30)]
    public void Opacity_is_clamped_and_has_floor_without_blur(double input, BlurMode blur, double expected)
    {
        var s = SettingsNormalizer.Normalize(WithAppearance(a => a with { BackgroundOpacity = input, Blur = blur }));

        Assert.Equal(expected, s.Appearance.BackgroundOpacity, 6);
    }

    [Fact]
    public void Valid_settings_are_unchanged()
    {
        var d = SettingsDefaults.Create();

        Assert.Equal(d, SettingsNormalizer.Normalize(d));
    }
}
