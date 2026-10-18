using System.Globalization;

namespace Windock.Core.Settings;

/// "#RRGGBB" colors used by the theme.
public readonly record struct ThemeColor(byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out ThemeColor color)
    {
        color = default;
        var hex = text?.Trim().TrimStart('#');
        if (hex is null || hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new ThemeColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// Canonical "#RRGGBB" (upper case), or null when the text is not a color.
    public static string? Normalize(string? text) => TryParse(text, out var c) ? c.ToHex() : null;

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// Mixes toward black: 0 = unchanged, 1 = black.
    public ThemeColor Darken(double amount)
    {
        var k = 1 - Math.Clamp(amount, 0, 1);
        return new ThemeColor((byte)Math.Round(R * k), (byte)Math.Round(G * k), (byte)Math.Round(B * k));
    }
}
