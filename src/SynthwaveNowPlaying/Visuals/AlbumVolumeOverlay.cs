using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SynthwaveNowPlaying;

// Draws a short-lived water level over the cover; it never intercepts mouse input.
internal sealed class AlbumVolumeOverlay : FrameworkElement
{
    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(65, 6, 8, 25));
    private static readonly Brush LabelPanelBrush = new SolidColorBrush(Color.FromArgb(170, 9, 10, 28));
    private static readonly Typeface NumberFace = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface LabelFace = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private Brush _waterBrush = Brushes.Transparent;
    private Pen _wavePen = new(Brushes.White, 1.4);
    private Pen _panelPen = new(Brushes.White, .7);
    private double _targetVolume = 1;
    private double _displayVolume = 1;
    private double _opacity;
    private double _targetOpacity;
    private double _phase;
    private DateTime _lastInteraction;
    private DateTime _lastFrame;
    private bool _rendering;
    private int _textPercent = -1;
    private FormattedText? _percentageText;
    private FormattedText? _labelText;

    public AlbumVolumeOverlay()
    {
        IsHitTestVisible = false;
        SetPalette(Color.FromRgb(255, 62, 189), Color.FromRgb(61, 220, 255));
        Unloaded += (_, _) => StopAnimation();
    }

    public void SetPalette(Color primary, Color secondary)
    {
        _waterBrush = new LinearGradientBrush(
            Color.FromArgb(180, primary.R, primary.G, primary.B),
            Color.FromArgb(205, secondary.R, secondary.G, secondary.B), 90);
        _wavePen = new Pen(new SolidColorBrush(secondary), 1.6);
        _panelPen = new Pen(new SolidColorBrush(Color.FromArgb(185, primary.R, primary.G, primary.B)), .8);
        if (_rendering) InvalidateVisual();
    }

    public void SetIdleVolume(double volume)
    {
        _targetVolume = _displayVolume = Math.Clamp(volume, 0, 1);
        if (_rendering) InvalidateVisual();
    }

    public void ShowVolume(double volume)
    {
        _targetVolume = Math.Clamp(volume, 0, 1);
        _targetOpacity = 1;
        _lastInteraction = DateTime.UtcNow;
        if (_rendering) return;
        _lastFrame = _lastInteraction;
        _rendering = true;
        CompositionTarget.Rendering += RenderFrame;
        InvalidateVisual();
    }

    public void StopAnimation()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= RenderFrame;
        _rendering = false;
        _opacity = _targetOpacity = 0;
        InvalidateVisual();
    }

    private void RenderFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var seconds = Math.Clamp((now - _lastFrame).TotalSeconds, 0, .05);
        _lastFrame = now;
        if ((now - _lastInteraction).TotalSeconds > 1.15) _targetOpacity = 0;
        _displayVolume += (_targetVolume - _displayVolume) * (1 - Math.Exp(-seconds * 13));
        _opacity += (_targetOpacity - _opacity) * (1 - Math.Exp(-seconds * (_targetOpacity > 0 ? 13 : 7)));
        _phase += seconds * 5.8;
        InvalidateVisual();
        if (_targetOpacity == 0 && _opacity < .012) StopAnimation();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (_opacity < .01 || width < 10 || height < 10) return;

        dc.PushOpacity(_opacity);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), 7, 7));
        dc.DrawRectangle(DimBrush, null, new Rect(0, 0, width, height));

        // Rebuild only the short wave path; all brushes and text are cached across frames.
        var water = new StreamGeometry();
        var wave = new StreamGeometry();
        var level = (1 - _displayVolume) * height;
        var amplitude = Math.Min(3.8, height * .045);
        var step = Math.Max(2, width / 28);
        using (var fill = water.Open())
        using (var line = wave.Open())
        {
            var first = new Point(0, level + Math.Sin(_phase) * amplitude);
            fill.BeginFigure(first, true, true);
            line.BeginFigure(first, false, false);
            for (var x = step; x < width + step; x += step)
            {
                var position = new Point(Math.Min(width, x),
                    level + Math.Sin(x / Math.Max(1, width) * Math.PI * 3.4 + _phase) * amplitude);
                fill.LineTo(position, true, false);
                line.LineTo(position, true, false);
            }
            fill.LineTo(new Point(width, height), true, false);
            fill.LineTo(new Point(0, height), true, false);
        }
        water.Freeze();
        wave.Freeze();
        dc.DrawGeometry(_waterBrush, null, water);
        dc.DrawGeometry(null, _wavePen, wave);

        var panel = new Rect(width * .11, height * .26, width * .78, height * .48);
        dc.DrawRoundedRectangle(LabelPanelBrush, _panelPen, panel, 7, 7);
        var percentage = (int)Math.Round(_targetVolume * 100);
        if (_percentageText is null || percentage != _textPercent)
        {
            _textPercent = percentage;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            _percentageText = new FormattedText($"{percentage}%", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, NumberFace, 17, Brushes.White, dpi);
            _labelText = new FormattedText("APP VOLUME", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, LabelFace, 5.6, Brushes.White, dpi);
        }
        dc.DrawText(_percentageText, new Point((width - _percentageText.Width) / 2, panel.Top + 2));
        if (_labelText is not null)
            dc.DrawText(_labelText, new Point((width - _labelText.Width) / 2, panel.Bottom - _labelText.Height - 3));
        dc.Pop();
        dc.Pop();
    }
}
