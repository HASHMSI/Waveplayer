using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SynthwaveNowPlaying;

public partial class MainWindow : Window
{
    private IReadOnlyDictionary<string, BitmapSource?> CaptureThemePreviews()
    {
        // Capture once, then recolor the small image without changing the playing widget.
        var snapshot = CaptureWidgetPreview();
        var source = _displayPalette;
        return new Dictionary<string, BitmapSource?>
        {
            ["Neon Grid"] = RecolorPreview(snapshot, source.Primary, source.Secondary, Color.FromRgb(255, 62, 189), Color.FromRgb(61, 220, 255)),
            ["Violet Dusk"] = RecolorPreview(snapshot, source.Primary, source.Secondary, Color.FromRgb(218, 91, 239), Color.FromRgb(255, 177, 100)),
            ["Ocean Drive"] = RecolorPreview(snapshot, source.Primary, source.Secondary, Color.FromRgb(76, 214, 255), Color.FromRgb(103, 126, 255))
        };
    }

    private BitmapSource? CaptureWidgetPreview()
    {
        try
        {
            if (Shell.ActualWidth < 1 || Shell.ActualHeight < 1) return null;
            var width = (int)Math.Ceiling(Shell.ActualWidth);
            var height = (int)Math.Ceiling(Shell.ActualHeight);
            var preview = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            preview.Render(Shell);
            preview.Freeze();
            return preview;
        }
        catch { return null; }
    }

    internal static BitmapSource? RecolorPreview(BitmapSource? source, Color fromPrimary, Color fromSecondary,
        Color toPrimary, Color toSecondary)
    {
        if (source is null) return null;
        try
        {
            var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);
            var fromPrimaryHue = Hue(fromPrimary);
            var fromSecondaryHue = Hue(fromSecondary);
            var toPrimaryHue = Hue(toPrimary);
            var toSecondaryHue = Hue(toSecondary);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                // Keep album artwork intact. Only the widget chrome and synthwave scene change.
                if (x < width * .23 && y > height * .26 && y < height * .9) continue;
                var index = y * stride + x * 4;
                if (pixels[index + 3] < 100) continue;
                var color = Color.FromRgb(pixels[index + 2], pixels[index + 1], pixels[index]);
                var max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255d;
                var min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255d;
                var saturation = max == 0 ? 0 : (max - min) / max;
                if (saturation < .22 || max < .12) continue;
                var hue = Hue(color);
                var primaryDistance = HueDistance(hue, fromPrimaryHue);
                var secondaryDistance = HueDistance(hue, fromSecondaryHue);
                var isSun = x > width * .66 && y > height * .22 && y < height * .83;
                if (!isSun && Math.Min(primaryDistance, secondaryDistance) > 56) continue;
                var targetHue = primaryDistance <= secondaryDistance ? toPrimaryHue : toSecondaryHue;
                var recolored = FromHsv(targetHue, saturation, max);
                pixels[index] = recolored.B;
                pixels[index + 1] = recolored.G;
                pixels[index + 2] = recolored.R;
            }
            var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch { return source; }
    }
}
