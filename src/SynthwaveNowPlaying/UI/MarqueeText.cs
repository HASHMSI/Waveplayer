using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SynthwaveNowPlaying.UI;

/// <summary>Draws full metadata inside a fixed, edge-faded viewport.</summary>
public sealed class MarqueeText : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeText),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure |
            FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((MarqueeText)d).Refresh()));

    private static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset), typeof(double), typeof(MarqueeText),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    private FormattedText? _formatted;
    private double _animatedTextWidth;
    private double _animatedViewportWidth;
    private bool _animating;

    public MarqueeText()
    {
        ClipToBounds = true;
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => Stop();
        SizeChanged += (_, _) => Refresh();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = Format();
        return new Size(double.IsInfinity(availableSize.Width) ? measured.WidthIncludingTrailingWhitespace
                : Math.Min(measured.WidthIncludingTrailingWhitespace, availableSize.Width),
            measured.Height + 2);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        context.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        context.DrawText(_formatted ?? Format(), new Point(Offset, Math.Max(0, (ActualHeight - Format().Height) / 2)));
        context.Pop();
    }

    private FormattedText Format()
    {
        // Text changes invalidate the cached layout through the dependency-property callback.
        return _formatted ??= new FormattedText(Text ?? string.Empty,
            CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize, Foreground ?? Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private void Refresh()
    {
        _formatted = null;
        if (!IsLoaded || ActualWidth <= 0) return;
        var textWidth = Format().WidthIncludingTrailingWhitespace;
        var viewport = ActualWidth;
        if (textWidth <= viewport + 2) { Stop(); return; }
        if (_animating && Math.Abs(textWidth - _animatedTextWidth) < 1 &&
            Math.Abs(viewport - _animatedViewportWidth) < 1) return;
        Stop();
        _animating = true;
        _animatedTextWidth = textWidth;
        _animatedViewportWidth = viewport;
        // The mask stays fixed to the viewport. Its left edge is opaque during
        // the reading pause, then fades in as the text starts its exit.
        var leftEdge = new GradientStop(Colors.White, 0);
        OpacityMask = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(viewport, 0),
            GradientStops =
            [
                leftEdge,
                new GradientStop(Colors.White, .10),
                new GradientStop(Colors.White, .90),
                new GradientStop(Colors.Transparent, 1)
            ]
        };

        // Cubic acceleration and deceleration meet the cruise at roughly the
        // same speed. The full text clears the frame before its next entrance.
        const double speed = 28;
        const double hold = 2;
        const double acceleration = 1.2;
        var rampDistance = speed * acceleration / 3;
        var cruiseEnd = hold + acceleration + Math.Max(0, textWidth - 2 * rampDistance) / speed;
        var exit = cruiseEnd + acceleration;
        var reentry = exit + .35;
        var arrival = reentry + Math.Clamp(viewport / 48, 1.5, 6);
        var motion = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        motion.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        motion.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold))));
        motion.KeyFrames.Add(new EasingDoubleKeyFrame(-rampDistance,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold + acceleration)),
            new CubicEase { EasingMode = EasingMode.EaseIn }));
        motion.KeyFrames.Add(new LinearDoubleKeyFrame(-(textWidth - rampDistance),
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cruiseEnd))));
        motion.KeyFrames.Add(new EasingDoubleKeyFrame(-textWidth,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(exit)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        motion.KeyFrames.Add(new DiscreteDoubleKeyFrame(viewport,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(reentry))));
        motion.KeyFrames.Add(new EasingDoubleKeyFrame(0,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(arrival)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        BeginAnimation(OffsetProperty, motion);

        var edge = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        edge.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.White,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        edge.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.White,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold))));
        edge.KeyFrames.Add(new LinearColorKeyFrame(Colors.Transparent,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold + .55))));
        edge.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.Transparent,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(exit))));
        edge.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.White,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(reentry))));
        edge.KeyFrames.Add(new DiscreteColorKeyFrame(Colors.White,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(arrival))));
        leftEdge.BeginAnimation(GradientStop.ColorProperty, edge);
    }

    private void Stop()
    {
        _animating = false;
        BeginAnimation(OffsetProperty, null);
        Offset = 0;
        OpacityMask = null;
    }
}
