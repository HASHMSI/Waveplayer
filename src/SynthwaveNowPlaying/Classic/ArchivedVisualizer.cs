using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Media;

namespace SynthwaveNowPlaying;

// The two player DLLs have the same assembly identity. Load the archived binary
// in its own context, sharing framework types (including WPF) with the host.
internal sealed class ArchivedVisualizer
{
    private static readonly Lazy<Assembly> Assembly = new(() =>
#if SETUP
        new AssemblyLoadContext("Waveplayer Classic").LoadFromStream(
            typeof(ArchivedVisualizer).Assembly.GetManifestResourceStream("Setup.Classic.dll")!));
#else
        new AssemblyLoadContext("Waveplayer Classic").LoadFromAssemblyPath(
            Path.Combine(AppContext.BaseDirectory, "Classic", "SynthwaveNowPlaying.dll")));
#endif

    public FrameworkElement View { get; }
    public Func<bool> HasRecentSignal { get; }
    public Func<bool> IsSettled { get; }
    public Func<float[]> Compute { get; }
    public Func<ReadOnlySpan<float>, float, bool> AddBlock { get; }
    public Action<ReadOnlySpan<float>> SetSpectrum { get; }
    public Action<double> SetSensitivity { get; }
    public Action<bool> SetEditing { get; }
    public Action<int, double> SetBandGain { get; }
    public Action<Color, Color, TimeSpan> ApplyPalette { get; }

    public ArchivedVisualizer(Action<int, float> onGainChanged)
    {
        object Create(string name, params object[] args) =>
            Activator.CreateInstance(Assembly.Value.GetType("SynthwaveNowPlaying." + name, true)!, args)!;

        var samples = Create("AudioSampleBuffer");
        AddBlock = Bind<Func<ReadOnlySpan<float>, float, bool>>(samples, "AddBlock");
        HasRecentSignal = Bind<Func<bool>>(samples, "get_HasRecentSignal");
        Compute = Bind<Func<float[]>>(Create("SpectrumAnalyzer", samples), "Compute");
        View = (FrameworkElement)Create("SpectrumView");
        IsSettled = Bind<Func<bool>>(View, "get_IsSettled");
        SetSpectrum = Bind<Action<ReadOnlySpan<float>>>(View, "SetSpectrum");
        SetSensitivity = Bind<Action<double>>(View, "set_Sensitivity");
        SetEditing = Bind<Action<bool>>(View, "set_IsEditing");
        SetBandGain = Bind<Action<int, double>>(View, "SetBandGain");
        ApplyPalette = Bind<Action<Color, Color, TimeSpan>>(View, "ApplyAlbumPalette");
        View.GetType().GetEvent("BandGainChanged")!.AddEventHandler(View, onGainChanged);
    }

    // Bind once at construction. The playback and render threads call delegates directly.
    private static T Bind<T>(object target, string method) where T : Delegate =>
        target.GetType().GetMethod(method)!.CreateDelegate<T>(target);
}
