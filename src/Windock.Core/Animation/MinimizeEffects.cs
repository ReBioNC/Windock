using Windock.Core.Layout;

namespace Windock.Core.Animation;

public enum MinimizeEffect { Off, Genie, Scale, Fold, Squeeze, Drop }

/// One piece of a window snapshot drawn in an animation frame. Source is a part of the snapshot in 0..1
/// units; Destination is where it goes on screen; Shade darkens it (0 = none, 1 = black).
public readonly record struct EffectPiece(RectD Source, RectD Destination, double Opacity, double Shade);

/// The minimize effects as pure geometry: for progress t (0 = the window, 1 = inside its dock icon) each
/// effect says which parts of the snapshot to draw where. Restoring plays the same frames backwards.
public static class MinimizeEffects
{
    public const int GenieStrips = 48, FoldStrips = 8;
    public const double FoldedHeight = 10;

    public static IReadOnlyList<EffectPiece> Frame(MinimizeEffect effect, RectD window, RectD icon, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return effect switch
        {
            MinimizeEffect.Genie => Genie(window, icon, t),
            MinimizeEffect.Scale => Whole(Lerp(window, icon, InOut(t)), FadeAtEnd(t)),
            MinimizeEffect.Fold => Fold(window, icon, t),
            MinimizeEffect.Squeeze => Squeeze(window, icon, t),
            MinimizeEffect.Drop => Drop(window, icon, t),
            _ => Array.Empty<EffectPiece>(),
        };
    }

    /// Bends toward the icon (bottom rows first), then slides down into it.
    private static IReadOnlyList<EffectPiece> Genie(RectD w, RectD icon, double t)
    {
        var a = InOut(Clamp01(t / 0.45));
        var b = Math.Pow(Clamp01((t - 0.45) / 0.55), 2);
        var pieces = new EffectPiece[GenieStrips];
        for (var i = 0; i < GenieStrips; i++)
        {
            var v = (double)i / GenieStrips;
            var bend = a * Math.Pow(v, 1.6);
            var cx = Lerp(Lerp(w.CenterX, icon.CenterX, bend), icon.CenterX, b);
            var width = Lerp(Lerp(w.Width, icon.Width, bend), icon.Width, b);
            var y = Lerp(w.Y + v * w.Height, icon.Y + v * icon.Height, b);
            var h = Lerp(w.Height / GenieStrips, icon.Height / GenieStrips, b);
            pieces[i] = new EffectPiece(new RectD(0, v, 1, 1.0 / GenieStrips), new RectD(cx - width / 2, y, width, h + 0.5), FadeAtEnd(t), 0);
        }
        return pieces;
    }

    /// Folds into a thin strip like paper (alternate panels shaded), then drops into the icon.
    private static IReadOnlyList<EffectPiece> Fold(RectD w, RectD icon, double t)
    {
        var a = InOut(Clamp01(t / 0.55));
        var b = InOut(Clamp01((t - 0.55) / 0.45));
        var folded = Math.Min(FoldedHeight, w.Height);
        var total = Lerp(w.Height, folded, a);
        var width = Lerp(w.Width, icon.Width, b);
        var cx = Lerp(w.CenterX, icon.CenterX, b);
        var top = Lerp(Lerp(w.Y, w.Y + w.Height / 2 - folded / 2, a), icon.Y + icon.Height / 2 - folded / 2, b);
        var each = total / FoldStrips;
        var pieces = new EffectPiece[FoldStrips];
        for (var i = 0; i < FoldStrips; i++)
            pieces[i] = new EffectPiece(new RectD(0, (double)i / FoldStrips, 1, 1.0 / FoldStrips),
                new RectD(cx - width / 2, top + i * each, width, Math.Max(each, 0.6)), FadeAtEnd(t), i % 2 == 1 ? 0.35 * a : 0);
        return pieces;
    }

    /// Squeezes thin like jelly, then shoots down into the icon.
    private static IReadOnlyList<EffectPiece> Squeeze(RectD w, RectD icon, double t)
    {
        var a = InOut(Clamp01(t / 0.4));
        var b = Cubic(Clamp01((t - 0.4) / 0.6));
        var width = Lerp(Lerp(w.Width, icon.Width * 1.4, a), icon.Width, b);
        var height = Lerp(Lerp(w.Height, w.Height * 1.15, a), icon.Height, b);
        var cx = Lerp(Lerp(w.CenterX, icon.CenterX, a * 0.6), icon.CenterX, b);
        var cy = Lerp(w.CenterY, icon.CenterY, b);
        return Whole(new RectD(cx - width / 2, cy - height / 2, width, height), FadeAtEnd(t));
    }

    /// Sinks toward the dock while shrinking a little and fading out.
    private static IReadOnlyList<EffectPiece> Drop(RectD w, RectD icon, double t)
    {
        var e = InOut(t);
        var scale = Lerp(1, 0.45, e);
        var width = w.Width * scale;
        var height = w.Height * scale;
        var cx = Lerp(w.CenterX, icon.CenterX, e);
        var cy = Lerp(w.CenterY, icon.CenterY, e);
        return Whole(new RectD(cx - width / 2, cy - height / 2, width, height), 1 - e);
    }

    private static EffectPiece[] Whole(RectD destination, double opacity) =>
        new[] { new EffectPiece(new RectD(0, 0, 1, 1), destination, opacity, 0) };

    /// The last bit blends into the icon instead of stopping on top of it.
    private static double FadeAtEnd(double t) => t > 0.85 ? 1 - (t - 0.85) / 0.15 * 0.9 : 1;

    private static RectD Lerp(RectD a, RectD b, double k) =>
        new(Lerp(a.X, b.X, k), Lerp(a.Y, b.Y, k), Lerp(a.Width, b.Width, k), Lerp(a.Height, b.Height, k));

    private static double Lerp(double a, double b, double k) => a + (b - a) * k;
    private static double Clamp01(double x) => Math.Clamp(x, 0, 1);
    private static double InOut(double x) => x < 0.5 ? 2 * x * x : 1 - Math.Pow(-2 * x + 2, 2) / 2;
    private static double Cubic(double x) => x * x * x;
}
