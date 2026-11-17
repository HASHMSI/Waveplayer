using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SynthwaveNowPlaying;

public sealed class SpectrumView : FrameworkElement
{
    private const int BarCount = 56;
    private static readonly double[] BandBounds = [20, Math.Sqrt(60 * 150), Math.Sqrt(150 * 400), Math.Sqrt(400 * 1000), Math.Sqrt(1000 * 3000), Math.Sqrt(3000 * 8000), 20000];
    private static readonly double[] BandPositions = BuildBandPositions();
    private LinearGradientBrush _barBrush = new(Color.FromRgb(255, 62, 189), Color.FromRgb(61, 221, 255), 90);
    private static readonly Brush CapBrush = Solid(Color.FromArgb(190, 255, 221, 255));
    private Brush _bandFillBrush = new SolidColorBrush(Color.FromArgb(18, 255, 89, 212));
    private static readonly Brush MarkerDot = Solid(Color.FromRgb(255, 242, 255));
    private Pen _zeroLine;
    private Pen _primaryMarker;
    private Pen _secondaryMarker;
    private Pen _markerOutline;
    private TimeSpan? _lastFrame;
    private readonly float[] _smooth = new float[BarCount];
    private readonly double[] _bandGains = new double[6];
    private readonly double[] _gainMultipliers = [1, 1, 1, 1, 1, 1];
    private readonly int[] _barBands = new int[BarCount];
    private bool _editing;
    private bool _dragging;
    public double Sensitivity { get; set; } = 1;
    public event Action<int, float>? BandGainChanged;

    public SpectrumView()
    {
        _zeroLine = MakePen(Color.FromArgb(65, 129, 229, 255), .7);
        _primaryMarker = MakePen(Color.FromRgb(255, 112, 220), 1.4);
        _secondaryMarker = MakePen(Color.FromRgb(93, 226, 255), 1.4);
        _markerOutline = MakePen(Color.FromRgb(255, 100, 218), .7);
        for (var i = 0; i < _barBands.Length; i++)
        {
            var frequency = 20 * Math.Pow(1000d, (i + .5) / BarCount);
            _barBands[i] = GetBandForFrequency(frequency);
        }
    }

    public void ApplyAlbumPalette(Color primary, Color secondary, TimeSpan duration)
    {
        _barBrush = new LinearGradientBrush(primary, secondary, 90);
        _bandFillBrush = new SolidColorBrush(Color.FromArgb(24, primary.R, primary.G, primary.B));
        _primaryMarker = MakePen(primary, 1.4);
        _secondaryMarker = MakePen(secondary, 1.4);
        _markerOutline = MakePen(primary, .7);
        _zeroLine = MakePen(Color.FromArgb(72, secondary.R, secondary.G, secondary.B), .7);
        InvalidateVisual();
    }

    public bool IsSettled
    {
        get
        {
            foreach (var value in _smooth) if (value > 0) return false;
            return true;
        }
    }

    public bool IsEditing
    {
        get => _editing;
        set { _editing = value; Cursor = value ? System.Windows.Input.Cursors.SizeNS : System.Windows.Input.Cursors.Arrow; InvalidateVisual(); }
    }

    public void SetBandGain(int band, double gain)
    {
        if ((uint)band >= _bandGains.Length) return;
        gain = Math.Clamp(gain, -12, 12);
        if (Math.Abs(_bandGains[band] - gain) < .05) return;
        _bandGains[band] = gain;
        _gainMultipliers[band] = Math.Pow(10, gain / 20d);
        InvalidateVisual();
    }

    public void SetSpectrum(ReadOnlySpan<float> values, TimeSpan frameTime)
    {
        if (values.Length != BarCount || frameTime == _lastFrame) return;
        double elapsed = _lastFrame is { } last
            ? Math.Clamp((frameTime - last).TotalSeconds, 0, .08) : 1d / 60;
        _lastFrame = frameTime;
        // Exponential envelopes retain the same response at different refresh rates.
        float attack = (float)(1 - Math.Exp(-elapsed / .023));
        float release = (float)(1 - Math.Exp(-elapsed / .080));
        for (int i = 0; i < BarCount; i++)
        {
            float target = (float)Math.Clamp(values[i] * Sensitivity * _gainMultipliers[_barBands[i]], 0, 1);
            _smooth[i] += (target - _smooth[i]) * (target > _smooth[i] ? attack : release);
            if (target == 0 && _smooth[i] < .001f) _smooth[i] = 0;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 10 || height < 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var gap = Math.Max(2, width / 190d);
        var barWidth = (width - gap * (BarCount - 1)) / BarCount;
        var baseline = height - 3;
        var maxHeight = height - 10;

        for (var i = 0; i < BarCount; i++)
        {
            var barHeight = Math.Max(2, _smooth[i] * maxHeight);
            var x = i * (barWidth + gap);
            var rect = new Rect(x, baseline - barHeight, Math.Max(1.5, barWidth), barHeight);
            dc.DrawRectangle(_barBrush, null, rect);
            if (barHeight > maxHeight * .48)
            {
                var cap = new Rect(x, baseline - barHeight - 2, Math.Max(1.5, barWidth), 2);
                dc.DrawRectangle(CapBrush, null, cap);
            }
        }

        if (_editing)
        {
            var zeroY = baseline - maxHeight / 2;
            dc.DrawLine(_zeroLine, new Point(0, zeroY), new Point(width, zeroY));
            for (var band = 0; band < _bandGains.Length; band++)
            {
                var (start, end) = BandRange(band, width);
                dc.DrawRectangle(_bandFillBrush, null, new Rect(start, 1, Math.Max(1, end - start), height - 2));
                var markerY = baseline - ((_bandGains[band] + 12) / 24d) * maxHeight;
                var pen = band % 2 == 0 ? _primaryMarker : _secondaryMarker;
                dc.DrawLine(pen, new Point(start + 1, markerY), new Point(Math.Max(start + 2, end - 1), markerY));
                dc.DrawEllipse(MarkerDot, _markerOutline, new Point((start + end) / 2, markerY), 2.2, 2.2);
            }
        }
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_editing)
        {
            _dragging = true;
            CaptureMouse();
            UpdateGainFromPointer(e.GetPosition(this));
            e.Handled = true;
        }
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (_editing && _dragging && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            UpdateGainFromPointer(e.GetPosition(this));
            e.Handled = true;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_dragging) { _dragging = false; ReleaseMouseCapture(); e.Handled = true; }
        base.OnMouseLeftButtonUp(e);
    }

    private void UpdateGainFromPointer(Point point)
    {
        // Visual drag reports one of six EQ gains; playback applies it only to local audio.
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var frequency = 20 * Math.Pow(20000d / 20, Math.Clamp(point.X / ActualWidth, 0, 1));
        var band = GetBandForFrequency(frequency);
        var gain = Math.Round(12 - Math.Clamp(point.Y / ActualHeight, 0, 1) * 24, 1);
        if (Math.Abs(_bandGains[band] - gain) < .05) return;
        _bandGains[band] = gain;
        _gainMultipliers[band] = Math.Pow(10, gain / 20d);
        BandGainChanged?.Invoke(band, (float)gain);
        InvalidateVisual();
    }

    private static int GetBandForFrequency(double frequency)
    {
        for (var band = 0; band < 6; band++) if (frequency < BandBounds[band + 1]) return band;
        return 5;
    }

    private static (double Start, double End) BandRange(int band, double width)
    {
        var start = BandPositions[band] * width;
        var end = BandPositions[band + 1] * width;
        return (start, Math.Min(width, end));
    }

    private static double[] BuildBandPositions()
    {
        var positions = new double[BandBounds.Length];
        for (var i = 0; i < positions.Length; i++) positions[i] = Math.Log(BandBounds[i] / 20, 1000d) / Math.Log(20000d / 20, 1000d);
        return positions;
    }

    private static Brush Linear(Color start, Color end)
    {
        var brush = new LinearGradientBrush(start, end, 90);
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

}

