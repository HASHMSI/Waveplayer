using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SynthwaveNowPlaying;

/// <summary>A small nonactivating overlay. Its progress is supplied by the player's clock.</summary>
public partial class AutomixOverlay : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private DispatcherTimer? _previewTimer;
    private bool _completed, _closing, _paused;
    internal bool Completed => _completed;

    internal void SetScale(double scale)
    {
        scale = double.IsFinite(scale) ? Math.Clamp(scale, .65, 1.8) : 1;
        Width = 280 * scale;
        Height = 132 * scale;
        if (IsLoaded)
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 14;
            Top = work.Top + 14;
        }
    }

    public AutomixOverlay()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            // Do not take focus, appear in Alt-Tab, or intercept clicks over another app.
            SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x08000000 | 0x80 | 0x20);
        };
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 14; Top = work.Top + 14;
            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            Slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(320))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Spin();
        };
        Closed += (_, _) => { _previewTimer?.Stop(); DiscRotation.BeginAnimation(RotateTransform.AngleProperty, null); _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    internal void SetTracks(string oldTitle, string oldArtist, BitmapSource? oldArt,
        string newTitle, string newArtist, BitmapSource? newArt)
    {
        if (_closing) return;
        if (!_completed) { OldTitle.Text = oldTitle; OldArtist.Text = oldArtist; OldBackdrop.Source = OldCover.Source = oldArt; }
        NewTitle.Text = CompleteTitle.Text = newTitle;
        NewArtist.Text = CompleteArtist.Text = newArtist;
        NewBackdrop.Source = NewCover.Source = CompleteBackdrop.Source = CompleteCover.Source = newArt;
    }

    internal void SetProgress(double progress)
    {
        if (_closing) return;
        var target = Math.Clamp(progress, 0, 1) * Math.Max(0, ClippedSurface.ActualWidth - 18);
        ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(90)));
    }

    private void Spin()
    {
        var angle = DiscRotation.Angle;
        DiscRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        DiscRotation.Angle = angle;
        DiscRotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(angle, angle + 360, TimeSpan.FromSeconds(2.5)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    internal void SetPaused(bool paused)
    {
        if (_paused == paused || _completed || _closing) return;
        _paused = paused;
        if (paused)
        {
            double angle = DiscRotation.Angle;
            DiscRotation.BeginAnimation(RotateTransform.AngleProperty, null); DiscRotation.Angle = angle;
        }
        else Spin();
        MixStatus.Text = paused ? "PAUSED" : "MIXING";
    }

    internal void Complete()
    {
        if (_completed || _closing) return;
        _completed = true; _previewTimer?.Stop();
        DiscRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        SetProgress(1); HeaderState.Text = "NOW PLAYING"; MixStatus.Text = "HANDOFF COMPLETE";
        CompleteView.Visibility = Visibility.Visible;
        CompleteView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
        MixView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)));
        _ = ExitAfterHandoffAsync();
    }

    private async Task ExitAfterHandoffAsync()
    {
        try { await Task.Delay(1400, _lifetime.Token); Dismiss(); }
        catch (OperationCanceledException) { }
    }

    internal void Dismiss()
    {
        if (_closing) return;
        _closing = true; _previewTimer?.Stop();
        DiscRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(280));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
        Slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(24, TimeSpan.FromMilliseconds(280))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
    }

    internal void RunPreview()
    {
        MixStatus.Text = "SILENT PREVIEW";
        var clock = Stopwatch.StartNew();
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _previewTimer.Tick += (_, _) => { SetProgress(clock.Elapsed.TotalSeconds / 7); if (clock.Elapsed.TotalSeconds >= 7) Complete(); };
        _previewTimer.Start();
    }

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ClippedSurface.Clip = new RectangleGeometry(new Rect(e.NewSize), 10, 10);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
