using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Text.Json.Serialization;

namespace SynthwaveNowPlaying;

internal readonly record struct MusicalKey(int Tonic, bool Minor, double Confidence, bool Reliable)
{
    private static readonly string[] Notes = ["C", "D♭", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B"];
    private static readonly int[] MajorWheel = [8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1];
    private static readonly int[] MinorWheel = [5, 12, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10];
    [JsonIgnore] public string Name => $"{Notes[Math.Clamp(Tonic, 0, 11)]} {(Minor ? "minor" : "major")}";
    [JsonIgnore] public string Camelot => $"{(Minor ? MinorWheel : MajorWheel)[Math.Clamp(Tonic, 0, 11)]}{(Minor ? "A" : "B")}";
    [JsonIgnore] public string Display => Reliable ? $"{Name} · {Camelot}" : "UNSURE";
}

/// <summary>
/// Bounded-memory spectral pitch-class analysis. Uses published Krumhansl–Kessler
/// and Temperley profiles, implemented here independently; no native keyfinder or ML runtime.
/// A key is a tonal estimate, not proof of a song's original production key.
/// </summary>
internal static class MusicalKeyAnalyzer
{
    private const int Rate = 11025, Size = 8192, Hop = 2048, Resolution = 36;
    private static readonly double[][] Profiles = [
        [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88],
        [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17],
        [5, 2, 3.5, 2, 4.5, 4, 2, 4.5, 2, 3.5, 1.5, 4],
        [5, 2, 3.5, 4.5, 2, 4, 2, 4.5, 3.5, 2, 1.5, 4],
        [1, 0, 1, 0, 1, 1, 0, 1, 0, 1, 0, 1],
        [1, 0, 1, 1, 0, 1, 0, 1, 1, 0, 1, 0]
    ];

    public static MusicalKey Analyze(string file, CancellationToken token)
    {
        using var reader = new AudioFileReader(file);
        var duration = reader.TotalTime.TotalSeconds;
        if (duration < 4 || reader.WaveFormat.Channels > 2) return default;
        var seconds = Math.Min(24, duration);
        var sections = new List<double[]>();
        var histogram = new double[Resolution];
        // Non-overlapping views of short tracks; spread bounded views over long tracks.
        int count = Math.Min(7, Math.Max(1, (int)Math.Ceiling(duration / seconds)));
        for (int n = 0; n < count; n++)
        {
            token.ThrowIfCancellationRequested();
            var start = count == 1 ? 0 : (duration - seconds) * n / (count - 1);
            var section = Measure(reader, start, seconds, token);
            sections.Add(section);
            for (int k = 0; k < Resolution; k++) histogram[k] += section[k];
        }
        // 36 pitch classes preserve sub-semitone tuning. Find the strongest
        // common offset before folding, rather than assuming A=440 Hz exactly.
        var tuning = Enumerable.Range(0, 3).OrderByDescending(offset =>
            Enumerable.Range(0, 12).Sum(note => histogram[note * 3 + offset])).First();
        if (tuning == 2) tuning = -1;
        var global = Fold(histogram, tuning);
        var scores = Scores(global);
        var ordered = Enumerable.Range(0, 24).OrderByDescending(k => scores[k]).ToArray();
        int best = ordered[0];
        double sum = global.Sum();
        if (sum <= 0) return default;
        var sectionWinners = sections.Select(section => Scores(Fold(section, tuning)))
            .Where(view => view.Max() > .45).ToArray();
        double agreement = sectionWinners.Length == 0 ? 0 : sectionWinners.Count(view =>
            Array.IndexOf(view, view.Max()) == best) / (double)sectionWinners.Length;
        double margin = scores[best] - scores[ordered[1]];
        double confidence = Math.Clamp(.5 * Math.Clamp((scores[best] - .35) / .5, 0, 1)
            + .3 * Math.Clamp(margin / .15, 0, 1) + .2 * agreement, 0, 1);
        bool reliable = scores[best] >= .60 && margin >= .025 && confidence >= .60 && agreement >= .5 &&
            global.Count(v => v > sum * .055) >= 4 && global.Max() / sum > .12;
        return new(best % 12, best >= 12, confidence, reliable);
    }

    private static double[] Measure(AudioFileReader reader, double start, double seconds, CancellationToken token)
    {
        reader.CurrentTime = TimeSpan.FromSeconds(start);
        // Keep stereo channels separate: mono summation can cancel a wide synth pad.
        ISampleProvider input = reader;
        if (reader.WaveFormat.SampleRate != Rate) input = new WdlResamplingSampleProvider(input, Rate);
        int channels = input.WaveFormat.Channels;
        var samples = new float[Size * channels];
        var window = new float[Size];
        var fft = new FftProcessor(Size, FftWindowType.Hann);
        var spectrum = new Complex[fft.SpectrumLength];
        var power = new double[spectrum.Length];
        var chroma = new double[Resolution];
        int filled = input.Read(samples.AsSpan());
        int frames = (int)Math.Max(0, (seconds * Rate - Size) / Hop) + 1;
        for (int frame = 0; frame < frames && filled >= Size * channels; frame++)
        {
            token.ThrowIfCancellationRequested();
            Array.Clear(power);
            for (int channel = 0; channel < channels; channel++)
            {
                for (int i = 0; i < Size; i++) window[i] = samples[i * channels + channel];
                fft.RealForward(window, spectrum);
                for (int i = 1; i < power.Length; i++)
                    power[i] += spectrum[i].Real * spectrum[i].Real + spectrum[i].Imaginary * spectrum[i].Imaginary;
            }
            var peaks = new List<(double Pitch, double Weight)>();
            double strongest = power.Max();
            for (int k = 40; k < Math.Min(power.Length - 2, 3500 * Size / Rate); k++)
            {
                if (power[k] < strongest * .0005 || power[k] <= power[k - 1] || power[k] <= power[k + 1]) continue;
                double left = Math.Log(power[k - 1] + 1e-20), mid = Math.Log(power[k] + 1e-20), right = Math.Log(power[k + 1] + 1e-20);
                double delta = Math.Clamp(.5 * (left - right) / (left - 2 * mid + right - 1e-20), -.5, .5);
                double hz = (k + delta) * Rate / Size;
                double pitch = 69 + 12 * Math.Log2(hz / 440);
                peaks.Add((pitch, Math.Sqrt(power[k])));
            }
            if (peaks.Count >= 3)
            {
                // Balance registers so a loud bass pedal cannot by itself
                // redefine the tonic of a quieter chord progression.
                var octaves = peaks.GroupBy(p => (int)Math.Floor(p.Pitch / 12))
                    .ToDictionary(group => group.Key, group => group.Sum(p => p.Weight));
                foreach (var peak in peaks)
                {
                    double position = (peak.Pitch % 12 + 12) % 12 * 3;
                    int center = (int)Math.Round(position);
                    chroma[center % Resolution] += peak.Weight /
                        octaves[(int)Math.Floor(peak.Pitch / 12)] / octaves.Count;
                }
            }
            Array.Copy(samples, Hop * channels, samples, 0, (Size - Hop) * channels);
            int read = input.Read(samples.AsSpan((Size - Hop) * channels, Hop * channels));
            filled = (Size - Hop) * channels + read;
        }
        return chroma;
    }

    private static double[] Fold(double[] histogram, int tuning)
    {
        var result = new double[12];
        for (int i = 0; i < Resolution; i++)
            result[((int)Math.Round((i - tuning) / 3d) + 12) % 12] += histogram[i];
        return result;
    }

    private static double[] Scores(double[] chroma)
    {
        var scores = new double[24];
        double mean = chroma.Average(), energy = chroma.Sum(x => (x - mean) * (x - mean));
        if (energy < 1e-16) return scores;
        for (int key = 0; key < 24; key++)
            for (int view = 0; view < 3; view++)
            {
                var profile = Profiles[(key >= 12 ? 1 : 0) + view * 2];
                // Account for fifth/third overtones in polyphonic recordings;
                // raw piano-style profiles can mistake a strong overtone for the tonic.
                double average = profile.Average() * (view < 2 ? 1.37 : 1), numerator = 0, denominator = 0;
                for (int i = 0; i < 12; i++)
                {
                    int note = (i - key % 12 + 12) % 12;
                    double p = profile[note] - average;
                    if (view < 2) p += .25 * profile[(note + 5) % 12] + .12 * profile[(note + 8) % 12];
                    numerator += (chroma[i] - mean) * p; denominator += p * p;
                }
                scores[key] += (view == 0 ? .4 : view == 1 ? .2 : .4) * numerator / Math.Sqrt(energy * denominator);
            }
        return scores;
    }
}
