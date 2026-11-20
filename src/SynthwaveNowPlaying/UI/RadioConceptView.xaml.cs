using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace SynthwaveNowPlaying.UI;

public partial class RadioConceptView : UserControl
{
    public event Action? Local, Pin, Settings, Hide, Previous, Next, Play, FavoriteRequested, EqRequested, EqResetRequested, RemixRequested;
    public RadioConceptView() => InitializeComponent();
    private int _transitionVersion;
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
        SpectrumHost.BeginAnimation(MarginProperty,new ThicknessAnimation(
            origin,target,TimeSpan.FromMilliseconds(440))
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
