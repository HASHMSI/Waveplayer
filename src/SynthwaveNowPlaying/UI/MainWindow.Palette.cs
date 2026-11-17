using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;

namespace SynthwaveNowPlaying;

public partial class MainWindow : Window
{
    // Album colors are sampled once per artwork and faded through new brushes.
    private void SetAlbumArt(BitmapSource? image)
    {
        ApplyUiPalette(_settings.Background == "Custom" || image is null ? ThemePalette() : ExtractAlbumPalette(image));
        AlbumArtImage.BeginAnimation(OpacityProperty, null);
        AlbumArtImage.Opacity = 0;
        AlbumArtImage.Source = image;
        AlbumArtImage.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(280))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void ApplyUiPalette(AlbumPalette palette)
    {
        if (_hasActivePalette && palette == _activePalette) return;
        _activePalette = palette;
        _hasActivePalette = true;
        _fadeStartPalette = _displayPalette;
        _paletteFadeStarted = DateTime.UtcNow;
        _paletteTimer.Start();
        UpdatePaletteFrame();
    }

    private void UpdatePaletteFrame()
    {
        var progress = Math.Clamp((DateTime.UtcNow - _paletteFadeStarted).TotalMilliseconds / 620, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        var primary = Blend(_fadeStartPalette.Primary, _activePalette.Primary, eased);
        var secondary = Blend(_fadeStartPalette.Secondary, _activePalette.Secondary, eased);
        _displayPalette = new AlbumPalette(primary, secondary);
        // Replace DynamicResource brushes: WPF styles may freeze shared brush instances.
        Resources["AlbumPrimaryBrush"] = new SolidColorBrush(primary);
        Resources["AlbumSecondaryBrush"] = new SolidColorBrush(secondary);
        Spectrum.ApplyAlbumPalette(primary, secondary, TimeSpan.Zero);
        _archived.ApplyPalette(primary, secondary, TimeSpan.Zero);
        AlbumVolume.SetPalette(primary, secondary);
        SynthBackground.ApplyAlbumPalette(primary, secondary, TimeSpan.Zero);
        if (PlaylistPopup.IsOpen) PlaylistBackground.ApplyAlbumPalette(primary, secondary, TimeSpan.Zero);
        if (progress >= 1) _paletteTimer.Stop();
    }

    private static Color Blend(Color start, Color end, double fraction) => Color.FromRgb(
        (byte)Math.Round(start.R + (end.R - start.R) * fraction),
        (byte)Math.Round(start.G + (end.G - start.G) * fraction),
        (byte)Math.Round(start.B + (end.B - start.B) * fraction));

    private AlbumPalette ThemePalette() => _settings.Background switch
    {
        "Custom" => CustomAlbumPalette(),
        "Ocean Drive" => new AlbumPalette(Color.FromRgb(76, 214, 255), Color.FromRgb(103, 126, 255)),
        "Violet Dusk" => new AlbumPalette(Color.FromRgb(218, 91, 239), Color.FromRgb(255, 177, 100)),
        _ => new AlbumPalette(Color.FromRgb(255, 62, 189), Color.FromRgb(61, 220, 255))
    };

    private AlbumPalette CustomAlbumPalette()
    {
        var (primary, secondary) = CustomPalette.Resolve(_settings);
        return new AlbumPalette(primary, secondary);
    }

    private static AlbumPalette ExtractAlbumPalette(BitmapSource image)
    {
        // Quantize saturated cover pixels into 4-bit RGB buckets, then choose two distinct accents.
        try
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            if (width <= 0 || height <= 0) return DefaultAlbumPalette;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            var weights = new double[4096];
            var redSums = new double[4096];
            var greenSums = new double[4096];
            var blueSums = new double[4096];
            var step = Math.Max(1, Math.Min(width, height) / 48);

            for (var y = step / 2; y < height; y += step)
            for (var x = step / 2; x < width; x += step)
            {
                var offset = y * stride + x * 4;
                var alpha = pixels[offset + 3];
                if (alpha < 150) continue;
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                var max = Math.Max(red, Math.Max(green, blue));
                var min = Math.Min(red, Math.Min(green, blue));
                var saturation = max == 0 ? 0 : (max - min) / (double)max;
                var brightness = max / 255d;
                if (saturation < .24 || brightness < .14 || brightness > .98) continue;
                var weight = saturation * (1 - Math.Abs(brightness - .58) * .45);
                var key = ((red >> 4) << 8) | ((green >> 4) << 4) | (blue >> 4);
                weights[key] += weight;
                redSums[key] += red * weight;
                greenSums[key] += green * weight;
                blueSums[key] += blue * weight;
            }

            var candidates = Enumerable.Range(0, weights.Length).Where(index => weights[index] > 0)
                .OrderByDescending(index => weights[index]).Take(32).ToArray();
            if (candidates.Length == 0) return DefaultAlbumPalette;
            Color BucketColor(int index) => Color.FromRgb(
                (byte)Math.Clamp(redSums[index] / weights[index], 0, 255),
                (byte)Math.Clamp(greenSums[index] / weights[index], 0, 255),
                (byte)Math.Clamp(blueSums[index] / weights[index], 0, 255));

            var primary = NormalizeAccent(BucketColor(candidates[0]));
            var primaryHue = Hue(primary);
            var secondaryIndex = candidates.Skip(1).FirstOrDefault(index =>
            {
                var distance = HueDistance(primaryHue, Hue(BucketColor(index)));
                return distance is >= 30 and <= 165;
            }, -1);
            var secondary = secondaryIndex >= 0
                ? NormalizeAccent(BucketColor(secondaryIndex))
                : FromHsv((primaryHue + 145) % 360, .78, .88);
            return new AlbumPalette(primary, secondary);
        }
        catch { return DefaultAlbumPalette; }
    }

    private static readonly AlbumPalette DefaultAlbumPalette = new(Color.FromRgb(255, 62, 189), Color.FromRgb(61, 220, 255));

    private static Color NormalizeAccent(Color color)
    {
        var hue = Hue(color);
        var max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255d;
        var min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255d;
        var saturation = max == 0 ? 0 : (max - min) / max;
        return FromHsv(hue, Math.Clamp(Math.Max(.7, saturation), 0, .96), Math.Clamp(Math.Max(.76, max), .76, .92));
    }

    private static double Hue(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        if (delta == 0) return 0;
        var hue = max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        return hue < 0 ? hue + 360 : hue;
    }

    private static double HueDistance(double first, double second)
    {
        var difference = Math.Abs(first - second) % 360;
        return Math.Min(difference, 360 - difference);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var section = hue / 60;
        var x = chroma * (1 - Math.Abs(section % 2 - 1));
        var (r, g, b) = section switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        var match = value - chroma;
        return Color.FromRgb((byte)Math.Round((r + match) * 255), (byte)Math.Round((g + match) * 255), (byte)Math.Round((b + match) * 255));
    }

    private readonly record struct AlbumPalette(Color Primary, Color Secondary);
}
