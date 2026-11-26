using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace SynthwaveNowPlaying.UI;

public partial class RadioConceptView : UserControl
{
    public event Action? Local, Pin, Settings, Hide, Previous, Next, Play, FavoriteRequested, EqRequested, EqResetRequested, RemixRequested, ShuffleRequested;
    public RadioConceptView()
    {
        InitializeComponent();
        Loaded += (_, _) => { StartRoadVideo(); StartRenderProbe(); };
    }
    private void StartRenderProbe()
    {
        var destination=Environment.GetEnvironmentVariable("WAVEPLAYER_RENDER_PROBE");
        if (string.IsNullOrEmpty(destination)) return;
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        var intervals=new List<double>();
        TimeSpan? last=null;
        EventHandler frame=(_,args)=>
        {
            var time=((RenderingEventArgs)args).RenderingTime;
            if(last is {} prior && time>prior) intervals.Add((time-prior).TotalMilliseconds);
            last=time;
        };
        CompositionTarget.Rendering+=frame;
        var timer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromSeconds(9) };
        timer.Tick+=(_,_)=>
        {
            timer.Stop(); CompositionTarget.Rendering-=frame;
            var ordered=intervals.Order().ToArray();
            if(ordered.Length==0)return;
            System.IO.File.WriteAllText(destination,System.Text.Json.JsonSerializer.Serialize(new {
                Frames=ordered.Length,Seconds=elapsed.Elapsed.TotalSeconds,
                AverageIntervalMs=ordered.Average(),P95IntervalMs=ordered[(int)((ordered.Length-1)*.95)],
                StallsOver50ms=ordered.Count(x=>x>50),RenderTier=RenderCapability.Tier>>16
            }));
        };
        timer.Start();
    }
    private void StartRoadVideo()
    {
        if (Environment.GetEnvironmentVariable("WAVEPLAYER_DISABLE_VIDEO") == "1") return;
        var packaged = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SynthwaveRoad-frames.zip");
        if (RoadVideo.OpenCompressed(packaged)) { RoadVideo_Opened(this,new RoutedEventArgs()); return; }
        var cache = System.IO.Path.Combine(SynthwaveNowPlaying.LabData.DataDirectory, "SynthwaveRoad-640x218.bgra");
        if (RoadVideo.Open(cache)) RoadVideo_Opened(this,new RoutedEventArgs());
    }
    private void ConsoleSurface_SizeChanged(object s, SizeChangedEventArgs e) =>
        ConsoleSurface.Clip=new RectangleGeometry(new Rect(e.NewSize),9,9);
    private void RoadVideo_Opened(object s, RoutedEventArgs e)
    {
        Scenery.Visibility=Visibility.Collapsed;
        RoadVideo.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(800))
        { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } });
    }
    private int _transitionVersion;
    public void UpdatePinState(bool pinned)
    {
        PinControl.ToolTip=pinned?"Pinned · click to unlock movement":"Unpinned · drag to move";
        PinGlyph.Opacity=pinned?1:.5;
    }
    private Color? _videoPrimary, _videoSecondary;
    public void ApplyVideoPalette(Color primary,Color secondary)
    {
        if(primary==_videoPrimary&&secondary==_videoSecondary)return;
        _videoPrimary=primary;_videoSecondary=secondary;
        var tint=(LinearGradientBrush)VideoPaletteTint.Background;
        if(tint.IsFrozen)
        {
            tint=tint.Clone();
            VideoPaletteTint.Background=tint;
        }
        // A compositor tint preserves the decoded frames and their cache.
        // Palette work happens once per artwork, not once per video frame.
        tint.GradientStops[0].BeginAnimation(GradientStop.ColorProperty,new ColorAnimation(
            Color.FromArgb(190,primary.R,primary.G,primary.B),TimeSpan.FromMilliseconds(620))
            {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});
        tint.GradientStops[1].BeginAnimation(GradientStop.ColorProperty,new ColorAnimation(
            Color.FromArgb(165,secondary.R,secondary.G,secondary.B),TimeSpan.FromMilliseconds(620))
            {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});
    }
    private bool _radioMode;
    public void TransitionMode(Action apply)
    {
        var version=++_transitionVersion;
        IsHitTestVisible=false;
        var exit=new DoubleAnimation(Hero.Opacity,.08,TimeSpan.FromMilliseconds(150))
        {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseIn}};
        exit.Completed+=(_,_)=>
        {
            if(version!=_transitionVersion)return;
            apply();
            Hero.BeginAnimation(OpacityProperty,new DoubleAnimation(.08,1,TimeSpan.FromMilliseconds(380))
            {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});
            var motion=new TranslateTransform(); MetadataPanel.RenderTransform=motion;
            motion.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(5,0,TimeSpan.FromMilliseconds(420))
            {EasingFunction=new QuarticEase {EasingMode=EasingMode.EaseOut}});
            IsHitTestVisible=true;
        };
        Hero.BeginAnimation(OpacityProperty,exit);
    }
    private void Local_Click(object s, RoutedEventArgs e) => Local?.Invoke();
    private void Pin_Click(object s, RoutedEventArgs e) => Pin?.Invoke();
    private void Settings_Click(object s, RoutedEventArgs e) => Settings?.Invoke();
    private void Close_Click(object s, RoutedEventArgs e) => Hide?.Invoke();
    private void Previous_Click(object s, RoutedEventArgs e) => Previous?.Invoke();
    private void Next_Click(object s, RoutedEventArgs e) => Next?.Invoke();
    private void Play_Click(object s, RoutedEventArgs e) => Play?.Invoke();
    private void Shuffle_Click(object s,RoutedEventArgs e)=>ShuffleRequested?.Invoke();
    private void Favorite_Click(object s, RoutedEventArgs e)
    {
        FavoriteRequested?.Invoke();
        Favorite.ApplyTemplate();
        if (Favorite.Template.FindName("HeartPulse", Favorite) is FrameworkElement pulse &&
            pulse.RenderTransform is ScaleTransform scale)
        {
            // WPF may freeze transforms shared by a control template.
            // Give this button an editable instance before attaching clocks.
            if (scale.IsFrozen) { scale = scale.Clone(); pulse.RenderTransform = scale; }
            // Replace the active clock on rapid clicks; never accumulate timers.
            var bounce = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(430), FillBehavior = FillBehavior.Stop };
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(.84, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(_favorite ? 1.24 : 1.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130)), new CubicEase { EasingMode = EasingMode.EaseOut }));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(.97, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(290)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(430)), new SineEase { EasingMode = EasingMode.EaseOut }));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
        }
        if (Favorite.Template.FindName("HeartRing", Favorite) is FrameworkElement ring &&
            ring.RenderTransform is ScaleTransform ringScale)
        {
            if (ringScale.IsFrozen) { ringScale = ringScale.Clone(); ring.RenderTransform = ringScale; }
            var expand = new DoubleAnimation(.45, 1.35, TimeSpan.FromMilliseconds(440))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            ringScale.BeginAnimation(ScaleTransform.ScaleXProperty, expand);
            ringScale.BeginAnimation(ScaleTransform.ScaleYProperty, expand);
            ring.BeginAnimation(OpacityProperty, new DoubleAnimation(_favorite ? .65 : .3, 0, TimeSpan.FromMilliseconds(440)) { FillBehavior = FillBehavior.Stop });
        }
    }
    private void Eq_Click(object s, RoutedEventArgs e) => EqRequested?.Invoke();
    private void EqReset_Click(object s, RoutedEventArgs e) => EqResetRequested?.Invoke();
    private void Remix_Click(object s, RoutedEventArgs e) => RemixRequested?.Invoke();
    public void SetMode(bool radio)
    {
        _radioMode=radio;
        Shuffle.Visibility=radio?Visibility.Collapsed:Visibility.Visible;
        EqReset.Visibility=Visibility.Collapsed;
        ModeLabel.Text=radio?" / RADIO":"";
        CoverHost.Visibility=radio?Visibility.Collapsed:Visibility.Visible;
        // Clear the previous mode's held animation before reading the new target.
        // Otherwise WPF returns the old animated margin (92 px) in radio mode.
        var target=radio?new Thickness(2,0,2,0):new Thickness(92,0,5,0);
        var origin=SpectrumHost.Margin;
        SpectrumHost.BeginAnimation(MarginProperty,null);
        SpectrumHost.Margin=target;
        MetadataPanel.Margin=radio?new Thickness(13,5,70,0):new Thickness(96,7,8,0);
        NowLabel.Text=radio?"NOW ON AIR":"LOCAL LIBRARY";
        Station.FontSize=radio?16:13;
        LiveBadge.Visibility=radio?Visibility.Visible:Visibility.Collapsed;
        Format.Visibility=FooterStatus.Visibility=Favorite.Visibility=FormatDivider.Visibility=StatusDivider.Visibility=radio?Visibility.Visible:Visibility.Collapsed;
        SeekHost.Visibility=Eq.Visibility=Remix.Visibility=radio?Visibility.Collapsed:Visibility.Visible;
        // Animate pixels, not layout: a Margin animation would remeasure the
        // entire player (including the video and spectrum) on every UI frame.
        var width=Math.Max(1,Hero.ActualWidth-target.Left-target.Right);
        var oldWidth=Math.Max(1,Hero.ActualWidth-origin.Left-origin.Right);
        var stretch=new ScaleTransform(oldWidth/width,1);
        var slide=new TranslateTransform(origin.Left-target.Left,0);
        SpectrumHost.RenderTransformOrigin=new Point(0,.5);
        SpectrumHost.RenderTransform=new TransformGroup {Children={stretch,slide}};
        stretch.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(stretch.ScaleX,1,TimeSpan.FromMilliseconds(440))
            {EasingFunction=new QuarticEase {EasingMode=EasingMode.EaseOut}});
        slide.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(slide.X,0,TimeSpan.FromMilliseconds(440))
            {EasingFunction=new QuarticEase {EasingMode=EasingMode.EaseOut}});
        CoverHost.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(380)));
    }
    private bool _favorite;
    public void SetFavorite(bool favorite)
    {
        _favorite = favorite;
        Favorite.Content = favorite ? "♥" : "♡";
        Favorite.ToolTip = favorite ? "Remove from favorites" : "Favorite channel";
        var target = favorite ? Color.FromRgb(255, 142, 173) : Color.FromRgb(226, 177, 199);
        var brush = new SolidColorBrush((Favorite.Foreground as SolidColorBrush)?.Color ?? target);
        Favorite.Foreground = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target, TimeSpan.FromMilliseconds(240)));
    }
    public void UpdateTrack(string text)
    {
        if(Metadata.Text==text)return;
        Metadata.Text=text;
        Metadata.BeginAnimation(OpacityProperty,new DoubleAnimation(.25,1,TimeSpan.FromMilliseconds(280)));
    }
    public void UpdatePlayback(bool playing, bool connected)
    {
        PlayGlyph.Text = playing ? "Ⅱ" : "▶";
        if (connected) SetStatus(playing ? "LIVE" : "PAUSED", playing ? "LIVE STREAM" : "PAUSED");
    }
    public void SetStatus(string status, string detail) { Status.Text = status; FooterStatus.Text = detail; }
    public void Tune(string name, int index)
    {
        Station.Text = name.ToUpperInvariant();
        Scenery.SetChannel(index);
        // New metadata enters as a single unit, with no repeating background timer.
        Hero.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(450))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
