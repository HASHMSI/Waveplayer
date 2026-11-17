using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SynthwaveNowPlaying;

public sealed class SynthwaveBackground : FrameworkElement
{
    private static readonly Brush NeonBackground = Linear(Color.FromRgb(8, 9, 25), Color.FromRgb(23, 10, 38));
    private static readonly Brush OceanBackground = Linear(Color.FromRgb(5, 15, 32), Color.FromRgb(7, 27, 47));
    private static readonly Brush VioletBackground = Linear(Color.FromRgb(20, 8, 36), Color.FromRgb(38, 9, 44));
    private static readonly Brush NeonGlow = Radial(Color.FromArgb(86, 255, 38, 177));
    private static readonly Brush OceanGlow = Radial(Color.FromArgb(86, 44, 186, 255));
    private static readonly Brush NeonSun = Linear(Color.FromRgb(255, 226, 112), Color.FromRgb(255, 56, 157));
    private static readonly Brush OceanSun = Linear(Color.FromRgb(95, 237, 255), Color.FromRgb(60, 101, 255));
    private static readonly Brush SunStripe = Solid(Color.FromRgb(16, 10, 34));
    private static readonly Brush StarPink = Solid(Color.FromRgb(245, 177, 255));
    private static readonly Brush StarCyan = Solid(Color.FromRgb(100, 222, 255));
    private static readonly Pen GridPen = MakePen(Color.FromArgb(82, 255, 59, 201), .7);
    private static readonly Pen CyanPen = MakePen(Color.FromArgb(58, 64, 222, 255), .65);
    private static readonly Pen ScanPen = MakePen(Color.FromArgb(10, 230, 220, 255), .55);
    private LinearGradientBrush _albumBackground = new(Color.FromRgb(10, 7, 28), Color.FromRgb(24, 8, 39), 90);
    private RadialGradientBrush _albumGlow = new(Color.FromArgb(72, 255, 62, 189), Color.FromArgb(0, 255, 62, 189))
    {
        Center = new Point(.78, .12), GradientOrigin = new Point(.78, .12), RadiusX = .58, RadiusY = .82
    };
    private LinearGradientBrush _albumSun = new(Color.FromRgb(255, 62, 189), Color.FromRgb(61, 221, 255), 90);
    private Brush _albumAccent = new SolidColorBrush(Color.FromRgb(255, 62, 189));
    private Brush _albumSecondary = new SolidColorBrush(Color.FromRgb(61, 221, 255));
    private Pen _albumGridPen;
    private Pen _albumSecondaryPen;
    private readonly DispatcherTimer _motion = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private double _phase;
    private bool _motionEnabled;
    private bool _albumPaletteActive;
    public string Theme { get; set; } = "Neon Grid";

    public bool MotionEnabled
    {
        get => _motionEnabled;
        set
        {
            if (_motionEnabled == value) return;
            _motionEnabled = value;
            if (!IsLoaded) return;
            if (value) _motion.Start(); else _motion.Stop();
            InvalidateVisual();
        }
    }

    public SynthwaveBackground()
    {
        _albumGridPen = MakePen(Color.FromArgb(78, 255, 62, 189), .7);
        _albumSecondaryPen = MakePen(Color.FromArgb(58, 61, 221, 255), .65);
        Loaded += (_, _) => { if (_motionEnabled) _motion.Start(); };
        Unloaded += (_, _) => _motion.Stop();
        _motion.Tick += (_, _) => { _phase += .025; InvalidateVisual(); };
    }

    public void ApplyAlbumPalette(Color primary, Color secondary, TimeSpan duration)
    {
        _albumPaletteActive = true;
        _albumBackground = new LinearGradientBrush(Darken(primary, .13), Darken(secondary, .16), 90);
        _albumGlow = new RadialGradientBrush(WithAlpha(primary, 76), WithAlpha(primary, 0))
        {
            Center = new Point(.78, .12), GradientOrigin = new Point(.78, .12), RadiusX = .58, RadiusY = .82
        };
        _albumSun = new LinearGradientBrush(primary, secondary, 90);
        _albumAccent = new SolidColorBrush(primary);
        _albumSecondary = new SolidColorBrush(secondary);
        _albumGridPen = MakePen(WithAlpha(primary, 76), .7);
        _albumSecondaryPen = MakePen(WithAlpha(secondary, 60), .65);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var ocean = Theme == "Ocean Drive";
        var background = _albumPaletteActive ? _albumBackground : ocean ? OceanBackground : Theme == "Violet Dusk" ? VioletBackground : NeonBackground;
        var glow = _albumPaletteActive ? _albumGlow : ocean ? OceanGlow : NeonGlow;
        dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
        dc.DrawRectangle(glow, null, new Rect(0, 0, width, height));

        // Sparse 80s star field with a slow neon shimmer.
        for (var i = 0; i < 24; i++)
        {
            var x = ((i * 73.7) % 997) / 997d * width;
            var y = ((i * 31.9) % 419) / 419d * height * .48;
            var twinkle = (65 + (Math.Sin(_phase * .7 + i * 1.9) + 1) * 55) / 255d;
            dc.PushOpacity(twinkle);
            var star = _albumPaletteActive ? i % 3 == 0 ? (Brush)_albumSecondary : _albumAccent : i % 3 == 0 ? StarCyan : StarPink;
            dc.DrawEllipse(star, null, new Point(x, y), i % 5 == 0 ? 1.15 : .65, i % 5 == 0 ? 1.15 : .65);
            dc.Pop();
        }

        var sunCenter = new Point(width * (.79 + Math.Sin(_phase * .18) * .012), height * (.43 + Math.Sin(_phase * .32) * .012));
        var sunRadius = Math.Min(42, height * .2);
        var sunRect = new Rect(sunCenter.X - sunRadius, sunCenter.Y - sunRadius, sunRadius * 2, sunRadius * 2);
        dc.DrawEllipse(_albumPaletteActive ? _albumSun : ocean ? OceanSun : NeonSun, null, sunCenter, sunRadius, sunRadius);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height * .46)));
        for (var i = 0; i < 7; i++)
        {
            var y = sunRect.Top + sunRadius * 1.05 + i * 5;
            dc.DrawRectangle(SunStripe, null, new Rect(sunRect.Left - 2, y, sunRect.Width + 4, i < 3 ? 2.2 : 3));
        }
        dc.Pop();

        var horizon = height * .69;
        for (var i = 0; i <= 11; i++)
        {
            var t = (i + 1) / 12d;
            var y = horizon + (height - horizon) * Math.Pow(t, 2 + Math.Sin(_phase) * .12);
            dc.DrawLine(i % 3 == 0 ? (_albumPaletteActive ? _albumSecondaryPen : CyanPen) : (_albumPaletteActive ? _albumGridPen : GridPen), new Point(0, y), new Point(width, y));
        }
        var vanish = new Point(width * (.52 + Math.Sin(_phase * .16) * .018), horizon);
        for (var i = -9; i <= 9; i++)
        {
            var bottomX = width * i / 9d + width / 2;
            dc.DrawLine(i % 3 == 0 ? (_albumPaletteActive ? _albumSecondaryPen : CyanPen) : (_albumPaletteActive ? _albumGridPen : GridPen), vanish, new Point(bottomX, height));
        }

        // Faint CRT scan lines add texture without masking the artwork or spectrum.
        for (var y = 2d; y < height; y += 4) dc.DrawLine(ScanPen, new Point(0, y), new Point(width, y));
    }

    private static Brush Linear(Color start, Color end)
    {
        var brush = new LinearGradientBrush(start, end, 90);
        brush.Freeze();
        return brush;
    }

    private static Brush Radial(Color color)
    {
        var brush = new RadialGradientBrush(Color.FromArgb(color.A, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B))
        {
            Center = new Point(.78, .12), GradientOrigin = new Point(.78, .12), RadiusX = .55, RadiusY = .8
        };
        brush.Freeze();
        return brush;
    }

    private static Brush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(Color color, double thickness)
    {
        var pen = new Pen(Solid(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Color Darken(Color color, double factor) => Color.FromRgb((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));
    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}

