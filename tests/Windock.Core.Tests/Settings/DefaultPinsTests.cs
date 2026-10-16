using Windock.Core.Settings;

namespace Windock.Core.Tests.Settings;

public class DefaultPinsTests
{
    private const string Win = @"C:\Windows", Common = @"C:\ProgramData\Start", User = @"C:\Users\u\Start";

    [Fact]
    public void Default_pins_include_explorer_and_existing_shortcuts_in_order()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Windows\explorer.exe", @"C:\ProgramData\Start\Spotify.lnk", @"C:\ProgramData\Start\Google Chrome.lnk",
            @"C:\Users\u\Start\Discord Inc\Discord.lnk",
        };

        var pins = DefaultPins.Detect(existing.Contains, Win, Common, User);

        Assert.Equal(new[]
        {
            @"C:\Windows\explorer.exe", @"C:\ProgramData\Start\Google Chrome.lnk",
            @"C:\Users\u\Start\Discord Inc\Discord.lnk", @"C:\ProgramData\Start\Spotify.lnk",
        }, pins.Select(p => p.Path));
    }

    [Fact]
    public void Default_pins_prefer_user_start_menu_over_common()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\ProgramData\Start\Steam\Steam.lnk", @"C:\Users\u\Start\Steam\Steam.lnk",
        };

        var pins = DefaultPins.Detect(existing.Contains, Win, Common, User);

        Assert.Equal(new[] { @"C:\Users\u\Start\Steam\Steam.lnk" }, pins.Select(p => p.Path));
    }
}
