using Windock.Core.Animation;
using Windock.Core.Layout;
using Windock.Core.Settings;

namespace Windock.Core.Tests.Animation;

public class MinimizeEffectsTests
{
    private static readonly RectD Window = new(200, 100, 800, 500);
    private static readonly RectD Icon = new(900, 1010, 48, 48);
    public static readonly TheoryData<MinimizeEffect> Effects = new()
    {
        MinimizeEffect.Genie, MinimizeEffect.Scale, MinimizeEffect.Fold, MinimizeEffect.Squeeze, MinimizeEffect.Drop,
    };

    private static RectD Bounds(IEnumerable<EffectPiece> pieces)
    {
        var list = pieces.ToList();
        var x = list.Min(p => p.Destination.X);
        var y = list.Min(p => p.Destination.Y);
        return new RectD(x, y, list.Max(p => p.Destination.Right) - x, list.Max(p => p.Destination.Bottom) - y);
    }

    [Theory, MemberData(nameof(Effects))]
    public void Starts_exactly_on_the_window(MinimizeEffect effect)
    {
        var pieces = MinimizeEffects.Frame(effect, Window, Icon, 0);
        var b = Bounds(pieces);

        Assert.Equal(Window.X, b.X, 3);
        Assert.Equal(Window.Y, b.Y, 3);
        Assert.Equal(Window.Width, b.Width, 3);
        Assert.Equal(Window.Height, b.Height, 0);  // strips overlap by half a pixel to avoid seams
        Assert.All(pieces, p => Assert.Equal(1, p.Opacity, 3));
        Assert.All(pieces, p => Assert.Equal(0, p.Shade, 3));
    }

    [Theory, MemberData(nameof(Effects))]
    public void Ends_on_the_icon_and_almost_invisible(MinimizeEffect effect)
    {
        var pieces = MinimizeEffects.Frame(effect, Window, Icon, 1);
        var b = Bounds(pieces);

        Assert.InRange(b.X + b.Width / 2, Icon.X, Icon.Right);
        Assert.InRange(b.Y + b.Height / 2, Icon.Y, Icon.Bottom);
        Assert.True(b.Width <= Window.Width * 0.5, $"{effect} still {b.Width} wide");
        Assert.All(pieces, p => Assert.True(p.Opacity <= 0.1 + 1e-9, $"{effect} opacity {p.Opacity}"));
    }

    [Theory, MemberData(nameof(Effects))]
    public void Sources_cover_the_whole_snapshot(MinimizeEffect effect)
    {
        var sources = MinimizeEffects.Frame(effect, Window, Icon, 0.5).Select(p => p.Source).ToList();

        Assert.Equal(0, sources.Min(s => s.Y), 6);
        Assert.Equal(1, sources.Max(s => s.Bottom), 6);
        Assert.All(sources, s => Assert.Equal(1, s.Width, 6));
    }

    [Fact]
    public void Genie_bends_the_bottom_toward_the_icon_first()
    {
        var pieces = MinimizeEffects.Frame(MinimizeEffect.Genie, Window, Icon, 0.3);

        Assert.True(pieces[^1].Destination.Width < pieces[0].Destination.Width);
        Assert.True(Math.Abs(pieces[^1].Destination.CenterX - Icon.CenterX) < Math.Abs(pieces[0].Destination.CenterX - Icon.CenterX));
    }

    [Fact]
    public void Fold_shades_every_other_panel_while_folding()
    {
        var pieces = MinimizeEffects.Frame(MinimizeEffect.Fold, Window, Icon, 0.5);

        Assert.Equal(MinimizeEffects.FoldStrips, pieces.Count);
        Assert.True(pieces[1].Shade > 0);
        Assert.Equal(0, pieces[0].Shade);
    }

    [Fact]
    public void Fold_folds_to_the_icon_height_so_the_restore_is_visible_from_the_start()
    {
        var folded = MinimizeEffects.Frame(MinimizeEffect.Fold, Window, Icon, 0.55);
        var height = folded.Max(p => p.Destination.Bottom) - folded.Min(p => p.Destination.Y);

        Assert.Equal(Icon.Height, height, 0);
    }

    [Fact]
    public void Off_draws_nothing_and_progress_is_clamped()
    {
        Assert.Empty(MinimizeEffects.Frame(MinimizeEffect.Off, Window, Icon, 0.5));
        Assert.Equal(MinimizeEffects.Frame(MinimizeEffect.Scale, Window, Icon, 1), MinimizeEffects.Frame(MinimizeEffect.Scale, Window, Icon, 3));
    }
}

public class AnimationSettingsTests
{
    [Fact]
    public void Defaults_are_genie_with_restore()
    {
        Assert.Equal(new AnimationSettings(MinimizeEffect.Genie, true, 450, true), SettingsDefaults.Create().Animations);
    }

    [Fact]
    public void Round_trips_and_section_is_reported()
    {
        var s = SettingsDefaults.Create() with { Animations = new AnimationSettings(MinimizeEffect.Fold, false, 900, false) };

        Assert.Equal(s.Animations, SettingsJson.Parse(SettingsJson.Serialize(s)).Animations);
    }

    [Theory]
    [InlineData(10, 200)]
    [InlineData(99999, 1500)]
    public void Duration_is_clamped(int input, int expected) =>
        Assert.Equal(expected, SettingsJson.Parse("{ \"animations\": { \"durationMs\": " + input + " } }").Animations.DurationMs);

    [Fact]
    public void Unknown_effect_falls_back_to_genie() =>
        Assert.Equal(MinimizeEffect.Genie, SettingsJson.Parse("{ \"animations\": { \"effect\": \"Swirl\" } }").Animations.Effect);
}
