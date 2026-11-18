using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.IO.Path;
using SynthwaveNowPlaying;

namespace WavePlayerSetup;

public partial class MainWindow
{
    private readonly MediaPlayer _demoPlayer = new();
    private string? _demoFile;
    private float[]? _spectrumFrames;
    private bool _spectrumPlaying;

    private void StopDemo()
    {
        _spectrumPlaying = false;
        _demoPlayer.Stop();
        _demoPlayer.Close();
        if (_demoFile is not null)
        {
            try { File.Delete(_demoFile); } catch (IOException) { }
            _demoFile = null;
        }
    }

    private void PlayDemo(string resourceName, bool spectrum = false)
    {
        StopDemo();
        using var source = typeof(MainWindow).Assembly.GetManifestResourceStream("WavePlayerSetup.Assets." + resourceName)
            ?? throw new InvalidOperationException("Demo audio is missing.");
        _demoFile = Path.Combine(Path.GetTempPath(), "WavePlayer-demo-" + Guid.NewGuid().ToString("N") + ".mp3");
        using (var destination = File.Create(_demoFile)) source.CopyTo(destination);
        _demoPlayer.Open(new Uri(_demoFile));
        _demoPlayer.Volume = .55;
        _spectrumPlaying = spectrum;
        _demoPlayer.Play();
    }

    private bool ReadDemoSpectrum(float[] output)
    {
        if (!_spectrumPlaying || _spectrumFrames is null) return false;
        int frame = (int)(_demoPlayer.Position.TotalSeconds * 60);
        if (frame < 0 || (frame + 1) * output.Length > _spectrumFrames.Length)
        {
            _spectrumPlaying = false;
            return false;
        }
        Array.Copy(_spectrumFrames, frame * output.Length, output, 0, output.Length);
        return true;
    }

    private UIElement BuildPlayback()
    {
        var panel = Stack();
        panel.Children.Add(Copy("Compare the playback options and choose your defaults. Audio plays when you press a preview button.", 12));
        panel.Children.Add(Copy("AUTOMIX TRANSITIONS", 11));
        var automix = new CheckBox { Content = "Enable Automix", IsChecked = _autoMix, Foreground = Brush("#F2E6FF"), Margin = new Thickness(0, 0, 0, 10) };
        automix.Checked += (_, _) => _autoMix = true;
        automix.Unchecked += (_, _) => _autoMix = false;
        panel.Children.Add(automix);
        foreach (var mode in new[] { "Adaptive (v2)", "Adaptive", "Classic" })
        {
            var card = new StackPanel();
            var radio = new RadioButton { Content = mode.ToUpperInvariant(), GroupName = "Mix", IsChecked = _timing == mode,
                Foreground = Brush("#FAECFF"), FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,6) };
            radio.Checked += (_, _) => _timing = mode;
            card.Children.Add(radio);
            card.Children.Add(Copy(mode switch
            {
                "Adaptive (v2)" => "Examines energy, beats and overlap; adds a restrained space echo to suitable endings.",
                "Adaptive" => "Original energy-aware transition.",
                _ => "A shorter transition, generally around 3–5 seconds."
            }, 11));
            card.Children.Add(new MixPreview { Adaptive = mode != "Classic", Height = 38, Margin = new Thickness(0, 0, 0, 8) });
            if (mode == "Adaptive (v2)")
                card.Children.Add(Copy("V2 plans each transition from your own songs; the diagram is illustrative.", 10));
            else
            {
                var play = new Button { Content = "▶  HEAR " + mode.ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Left };
                play.Click += (_, _) => PlayDemo(mode == "Adaptive" ? "mix-adaptive.mp3" : "mix-classic.mp3");
                card.Children.Add(play);
            }
            panel.Children.Add(Panel(card));
        }
        panel.Children.Add(Copy("Audio examples illustrate longer and shorter fades with Revolve → Don't Sleep Yet.", 10));
        _spectrumSection = Copy("LIVE SPECTRUM", 11);
        panel.Children.Add(_spectrumSection);
        using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("WavePlayerSetup.Assets.spectrum-demo.bin")!)
        using (var reader = new BinaryReader(stream))
        {
            _spectrumFrames = new float[(int)(stream.Length / sizeof(float))];
            for (int i = 0; i < _spectrumFrames.Length; i++) _spectrumFrames[i] = reader.ReadSingle();
        }
        var lab = new SpectrumLab(ReadDemoSpectrum, _pulse, 1.6);
        lab.SelectionChanged += value => _pulse = value;
        panel.Children.Add(lab);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var demo = new Button { Content = "▶  PLAY SPECTRUM SAMPLE", Margin = new Thickness(0,0,8,0) };
        demo.Click += (_, _) => PlayDemo("spectrum-demo.mp3", true);
        var stop = new Button { Content = "■  STOP" };
        stop.Click += (_, _) => StopDemo();
        controls.Children.Add(demo);
        controls.Children.Add(stop);
        panel.Children.Add(controls);
        return panel;
    }

    private static FrameworkElement CreatePlayerPreview()
    {
        var frame = new Border { Margin = new Thickness(15, 18, 15, 18), CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1), Background = Brush("#101425"), Padding = new Thickness(12) };
        frame.SetResourceReference(Border.BorderBrushProperty, "PreviewPrimary");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var cover = new Border { CornerRadius = new CornerRadius(5), Margin = new Thickness(0,0,12,0) };
        cover.SetResourceReference(Border.BackgroundProperty, "PreviewPrimary");
        grid.Children.Add(cover);
        var body = new StackPanel();
        Grid.SetColumn(body, 1);
        body.Children.Add(new TextBlock { Text = "WavePlayer  •  NOW PLAYING", FontSize = 10, FontWeight = FontWeights.Bold });
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Height = 30, VerticalAlignment = VerticalAlignment.Bottom };
        for (int i = 0; i < 30; i++)
        {
            var bar = new Rectangle { Width = 5, Height = 5 + 20 * Math.Abs(Math.Sin(i * .67)), Margin = new Thickness(1,0,1,0), VerticalAlignment = VerticalAlignment.Bottom };
            bar.SetResourceReference(Shape.FillProperty, i % 2 == 0 ? "PreviewPrimary" : "PreviewSecondary");
            bars.Children.Add(bar);
        }
        body.Children.Add(bars);
        var line = new Rectangle { Height = 3, Margin = new Thickness(0,10,0,0), HorizontalAlignment = HorizontalAlignment.Stretch };
        line.SetResourceReference(Shape.FillProperty, "PreviewSecondary");
        body.Children.Add(line);
        grid.Children.Add(body);
        frame.Child = grid;
        return frame;
    }
}

internal sealed class MixPreview : FrameworkElement
{
    public bool Adaptive { get; init; }
    private readonly System.Diagnostics.Stopwatch _clock = new();

    public MixPreview()
    {
        Loaded += (_, _) => { _clock.Start(); CompositionTarget.Rendering += OnFrame; };
        Unloaded += (_, _) => { CompositionTarget.Rendering -= OnFrame; _clock.Stop(); };
    }

    private void OnFrame(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(17,18,35)), null, new Rect(0,0,width,height), 5, 5);
        double t = (_clock.Elapsed.TotalSeconds / 5) % 1;
        for (int i = 0; i < 48; i++)
        {
            double x = (i + .5) * width / 48;
            double p = i / 47d;
            double overlap = Adaptive ? .34 : .18;
            double outgoing = Math.Clamp((.5 + overlap - p) / (2 * overlap), 0, 1);
            double incoming = 1 - outgoing;
            double shape = .45 + .4 * Math.Abs(Math.Sin(i * 1.43));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(190, 255, 85, 190)), null, new Rect(x, height/2 - shape*outgoing*height/2, Math.Max(1,width/90), shape*outgoing*height/2));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(190, 93, 215, 255)), null, new Rect(x, height/2, Math.Max(1,width/90), shape*incoming*height/2));
        }
        dc.DrawLine(new Pen(Brushes.White, 1), new Point(t * width, 2), new Point(t * width, height - 2));
    }
}
