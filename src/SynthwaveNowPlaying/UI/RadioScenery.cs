using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SynthwaveNowPlaying.UI;

/// <summary>Resolution-independent dusk scene. Only palette changes animate;
/// the actual audio spectrum owns the continuous animation budget.</summary>
public sealed class RadioScenery : FrameworkElement
{
    private readonly SolidColorBrush _sky = new(Color.FromRgb(63, 48, 91));
    private readonly Color[] _colors = [Color.FromRgb(37, 45, 82), Color.FromRgb(77, 48, 89), Color.FromRgb(38, 25, 65),
        Color.FromRgb(65, 28, 49), Color.FromRgb(30, 44, 91), Color.FromRgb(23, 67, 75), Color.FromRgb(47, 25, 65)];
    public RadioScenery() { IsHitTestVisible = false; _sky.Changed += (_, _) => InvalidateVisual(); }
    public void SetChannel(int index) => _sky.BeginAnimation(SolidColorBrush.ColorProperty,
        new ColorAnimation(_colors[Math.Abs(index) % _colors.Length], TimeSpan.FromMilliseconds(650)));
    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        if (w < 1 || h < 1) return;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        dc.DrawRectangle(new LinearGradientBrush(_sky.Color, Color.FromRgb(17, 29, 53), 90), null, new Rect(0, 0, w, h));
        var horizon = h * .58;
        dc.DrawEllipse(new LinearGradientBrush(Color.FromArgb(175,255,164,155), Color.FromArgb(140,194,92,157),90),null,
            new Point(w*.61,horizon),h*.23,h*.23);
        var mountain = new StreamGeometry();
        using(var g=mountain.Open())
        {
            g.BeginFigure(new Point(w*.69,horizon+5),true,true);
            foreach(var p in new[]{new Point(w*.78,horizon-5),new Point(w*.84,horizon-2),new Point(w*.92,horizon-14),new Point(w,horizon-8),new Point(w,h),new Point(w*.69,h)}) g.LineTo(p,true,false);
        }
        mountain.Freeze(); dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(145,17,24,50)),null,mountain);
        for(int i=0;i<15;i++)
        {
            var y=horizon+(h-horizon)*i/15d;
            var span=(i+1)*w*.009;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)(42-i*2),244,152,183)),.4),new Point(w*.61-span,y),new Point(w*.61+span,y));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(20,133,191,219)),.35),new Point(0,y+1),new Point(w,y+1));
        }
        dc.Pop();
    }
}
