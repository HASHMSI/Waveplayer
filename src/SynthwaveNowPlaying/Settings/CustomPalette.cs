using System.Windows.Media;

namespace SynthwaveNowPlaying;

internal static class CustomPalette
{
    public static readonly Color DefaultPrimary = Color.FromRgb(255, 71, 184);
    public static readonly Color DefaultSecondary = Color.FromRgb(69, 216, 255);

    public static Color Parse(string? text, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            var converted = ColorConverter.ConvertFromString(text.Trim());
            if (converted is Color color) return Color.FromRgb(color.R, color.G, color.B);
        }
        catch { }
        return fallback;
    }

    public static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static (Color Primary, Color Secondary) Resolve(AppSettings settings) => Resolve(
        Parse(settings.CustomPrimary, DefaultPrimary),
        Parse(settings.CustomSecondary, DefaultSecondary), settings.CustomMix);

    public static (Color Primary, Color Secondary) Resolve(Color primary, Color secondary, double mix)
    {
        // Balance favors one color while keeping the other visible at both extremes.
        mix = double.IsFinite(mix) ? Math.Clamp(mix, 0, 1) : .5;
        var bias = mix * 2 - 1;
        if (bias < 0) secondary = Blend(secondary, primary, -bias * .74);
        else if (bias > 0) primary = Blend(primary, secondary, bias * .74);
        return (primary, secondary);
    }

    private static Color Blend(Color first, Color second, double amount) => Color.FromRgb(
        (byte)Math.Round(first.R + (second.R - first.R) * amount),
        (byte)Math.Round(first.G + (second.G - first.G) * amount),
        (byte)Math.Round(first.B + (second.B - first.B) * amount));
}
