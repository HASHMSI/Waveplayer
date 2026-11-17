using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SynthwaveNowPlaying;

internal sealed class SpectrumLab : UserControl
{
    private readonly RadioButton Pulse;
    private readonly RadioButton Glide;
    private readonly TextBlock SourceText;
    private readonly ArchivedVisualizer _pulse = new((_, _) => { });
    private readonly SpectrumView _glide = new();
    private readonly Func<float[], bool> _readTrack;
    private readonly float[] _bands = new float[56];
    private TimeSpan? _lastFrame;
    private bool _rendering;
    private bool _usingTrack;

    public bool UsesPulse => Pulse.IsChecked == true;
    public event Action<bool>? SelectionChanged;

    public SpectrumLab(Func<float[], bool> readTrack, bool usePulse, double sensitivity)
    {
        // WPF pack URIs resolve by assembly name. The archive shares that name,
        // so read this layout from the host assembly explicitly.
        using var layout = typeof(SpectrumLab).Assembly.GetManifestResourceStream("Settings.SpectrumLab.xaml")!;
        var root = (Grid)System.Windows.Markup.XamlReader.Load(layout);
        Content = root;
        Pulse = (RadioButton)root.FindName("Pulse");
        Glide = (RadioButton)root.FindName("Glide");
        SourceText = (TextBlock)root.FindName("SourceText");
        _readTrack = readTrack;
        Pulse.Content = _pulse.View;
        Glide.Content = _glide;
        Pulse.IsChecked = usePulse;
        Glide.IsChecked = !usePulse;
        Pulse.Checked += (_, _) => SelectionChanged?.Invoke(true);
        Glide.Checked += (_, _) => SelectionChanged?.Invoke(false);
        _pulse.ApplyPalette(Color.FromRgb(255, 132, 209), Color.FromRgb(142, 112, 238), TimeSpan.Zero);
        _glide.ApplyAlbumPalette(Color.FromRgb(110, 235, 234), Color.FromRgb(111, 126, 250), TimeSpan.Zero);
        SetSensitivity(sensitivity);
        Loaded += (_, _) => UpdateRendering();
        IsVisibleChanged += (_, _) => UpdateRendering();
        Unloaded += (_, _) => StopRendering();
    }

    public void SetSensitivity(double value)
    {
        _pulse.SetSensitivity(value);
        _glide.Sensitivity = value;
    }

    private void UpdateRendering()
    {
        if (!IsLoaded || !IsVisible) { StopRendering(); return; }
        if (_rendering) return;
        _rendering = true;
        CompositionTarget.Rendering += RenderFrame;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= RenderFrame;
        _rendering = false;
        _lastFrame = null;
    }

    private void RenderFrame(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (time == _lastFrame) return;
        _lastFrame = time;
        bool playing = _readTrack(_bands);
        if (playing != _usingTrack)
        {
            SourceText.Text = playing ? "NOW PLAYING" : "DEMO LOOP";
            _usingTrack = playing;
        }
        if (!playing) FillDemo(time.TotalSeconds);
        _pulse.SetSpectrum(_bands);
        _glide.SetSpectrum(_bands, time);
    }

    // A silent four-beat loop pairs short transients with a sustained chord.
    // Both renderers receive identical bands on each composition frame.
    private void FillDemo(double seconds)
    {
        double beat = seconds / .55 % 4;
        double kick = Math.Exp(-11 * (beat % 1));
        double snare = Math.Exp(-16 * ((beat + 1) % 2));
        double hat = Math.Exp(-22 * (beat % .5));
        double chord = Math.Max(0, Math.Sin(Math.PI * beat / 2));
        for (int i = 0; i < _bands.Length; i++)
        {
            double x = i / (double)(_bands.Length - 1);
            double bass = kick * Math.Exp(-85 * Math.Pow(x - .16, 2));
            double mids = (.65 * snare + .28 * chord) * Math.Exp(-26 * Math.Pow(x - .55, 2));
            double highs = .62 * hat * Math.Exp(-44 * Math.Pow(x - .87, 2));
            _bands[i] = (float)((bass + mids + highs) * (.80 + .20 * Math.Cos(i * 1.2)));
        }
    }
}
