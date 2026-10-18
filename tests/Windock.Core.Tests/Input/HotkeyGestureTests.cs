using Windock.Core.Input;

namespace Windock.Core.Tests.Input;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+W", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x57)]
    [InlineData("alt + ctrl + f12", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x7B)]
    [InlineData("Win+Shift+5", HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x35)]
    [InlineData("Control+Alt+F24", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x87)]
    public void Parses_gestures(string text, HotkeyModifiers mods, int vk)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(vk, g.VirtualKey);
    }

    [Theory]
    [InlineData("Ctrl+W")]
    [InlineData("W")]
    [InlineData("Ctrl+Alt+Enter")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+W+Q")]
    [InlineData("Ctrl+Ctrl+W")]
    [InlineData("Ctrl+Alt+F25")]
    [InlineData("")]
    public void Rejects_invalid_gestures(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Fact]
    public void ToString_is_canonical()
    {
        Assert.True(HotkeyGesture.TryParse("shift+ctrl+alt+w", out var g));
        Assert.Equal("Ctrl+Alt+Shift+W", g.ToString());
        Assert.True(HotkeyGesture.TryParse("win+alt+f3", out var f));
        Assert.Equal("Alt+Win+F3", f.ToString());
    }

    [Fact]
    public void Space_is_a_key()
    {
        Assert.True(HotkeyGesture.TryParse("ctrl+alt+space", out var g));
        Assert.Equal(HotkeyGesture.VkSpace, g.VirtualKey);
        Assert.Equal("Ctrl+Alt+Space", g.ToString());
    }

    [Fact]
    public void MinModifiers_allows_single_modifier_gestures()
    {
        Assert.False(HotkeyGesture.TryParse("Alt+Space", out _));
        Assert.True(HotkeyGesture.TryParse("Alt+Space", out var g, minModifiers: 1));
        Assert.Equal(HotkeyModifiers.Alt, g.Modifiers);
        Assert.Equal("Alt+Space", g.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_bare_key_is_never_a_gesture(int min) => Assert.False(HotkeyGesture.TryParse("Space", out _, min));

    [Fact]
    public void Modifier_values_match_win32_MOD_constants()
    {
        Assert.Equal(1, (int)HotkeyModifiers.Alt);
        Assert.Equal(2, (int)HotkeyModifiers.Ctrl);
        Assert.Equal(4, (int)HotkeyModifiers.Shift);
        Assert.Equal(8, (int)HotkeyModifiers.Win);
    }
}
