using System.Globalization;
using System.Windows.Media;

namespace TaskbarMonitor.Theming;

// WCAG 2.x relative luminance and contrast ratio, plus small color helpers.
public static class Contrast
{
    public const double TextMinimum = 4.5;

    public static double Luminance(Color c)
    {
        static double Linear(byte v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    public static double Ratio(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static bool IsDark(Color c) => Luminance(c) < 0.18;

    public static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(from.A + (to.A - from.A) * amount),
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    // Pushes a color toward white or black (whichever the background calls for) until it reaches the ratio.
    public static Color Ensure(Color color, Color background, double minimum)
    {
        var target = IsDark(background) ? Colors.White : Colors.Black;
        var current = Opaque(color);
        for (int step = 0; step <= 20 && Ratio(current, background) < minimum; step++)
            current = Mix(Opaque(color), target, step * 0.05);
        return current;
    }

    // First candidate that is readable on the background, or the last one forced to be readable.
    public static Color FirstReadable(Color background, double minimum, params Color[] candidates)
    {
        foreach (var candidate in candidates)
            if (Ratio(candidate, background) >= minimum)
                return Opaque(candidate);
        return Ensure(candidates[^1], background, minimum);
    }

    public static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);

    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            return false;
        // #AARRGGBB keeps only the color: overlay text is always opaque.
        color = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Color Parse(string? text, Color fallback) => TryParse(text, out var c) ? c : fallback;
}
