using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SynthwaveNowPlaying;

// Four observations per second are enough for ranking entry points. Beat phase
// still uses the finer beat grid. Only intro/outro descriptors are cached.
internal sealed record CueSection(double Start, CueBin[] Bins, double? BarAnchor)
{
    public const double Step = .25;
    public CueBin At(double time)
    {
        int i = (int)Math.Floor((time - Start) / Step);
        return (uint)i < Bins.Length ? Bins[i] : default;
    }
}

internal readonly record struct CueBin(float Level, float Voice, float Attack, float Tone);
internal sealed record TransitionFeatures(CueSection Intro, CueSection Outro, bool NaturalTail, double TailEnd, double LastHit)
{
    public static TransitionFeatures Analyze(AudioFileReader reader, double intro, double end, BeatGrid firstBeat,
        BeatGrid lastBeat, CancellationToken token)
    {
        var duration = reader.TotalTime.TotalSeconds;
        var head = Measure(reader, intro, Math.Min(duration, intro + 16), firstBeat, token, out _);
        var tail = Measure(reader, Math.Max(0, end - 17), Math.Min(duration, end + 4), lastBeat, token, out var hits);
        double lastHit = hits.Where(t => t <= end + .04 && lastBeat.IsReliable &&
            Math.Abs(t - lastBeat.Nearest(t)) < .09).DefaultIfEmpty(-1).Last();
        int last = Array.FindLastIndex(tail.Bins, b => b.Level > .012);
        double tailEnd = last < 0 ? end : Math.Min(duration, tail.Start + (last + 1) * CueSection.Step);
        // Protect existing decay: sustained fall over several blocks, little
        // new attack, and a quiet ending. This is an acoustic cue, not a stem.
        bool natural = false;
        for (int span = 4; span <= 14 && last >= span; span++)
        {
            var segment = tail.Bins.AsSpan(last - span, span + 1);
            if (segment[0].Level < .10 || segment[^1].Level > segment[0].Level * .34) continue;
            int rebounds = 0, attacks = 0;
            for (int k = 1; k < segment.Length; k++)
            {
                if (segment[k].Level > segment[k - 1].Level * 1.2 + .012) rebounds++;
                if (segment[k].Attack > .28) attacks++;
            }
            if (rebounds == 0 && attacks <= 1) { natural = true; break; }
        }
        return new(head, tail, natural, tailEnd, lastHit);
    }

    private static CueSection Measure(AudioFileReader reader, double start, double end, BeatGrid beat,
        CancellationToken token, out List<double> hits)
    {
        reader.CurrentTime = TimeSpan.FromSeconds(start);
        ISampleProvider mono = reader;
        if (reader.WaveFormat.Channels == 2) mono = new StereoToMonoSampleProvider(reader);
        hits = [];
        if (mono.WaveFormat.Channels != 1) return new(start, [], null);
        const int rate = 8192, size = 256, perBin = 8;
        if (mono.WaveFormat.SampleRate != rate) mono = new WdlResamplingSampleProvider(mono, rate);
        var fft = new FftProcessor(size, FftWindowType.Hann);
        var pcm = new float[size];
        var spectrum = new NAudio.Dsp.Complex[fft.SpectrumLength];
        var bins = new List<CueBin>();
        var accents = new double[4];
        double prev = 0, prevMid = 0, prevBass = 0;
        int count = (int)Math.Ceiling((end - start) / CueSection.Step);
        for (int n = 0; n < count; n++)
        {
            token.ThrowIfCancellationRequested();
            double sum = 0, voice = 0, attack = 0, tone = 0, modulation = 0;
            for (int j = 0; j < perBin; j++)
            {
                int got = mono.Read(pcm.AsSpan());
                pcm.AsSpan(got).Clear();
                fft.RealForward(pcm, spectrum);
                double power = 1e-14, low = 0, vowel = 0, upper = 0, peak = 0;
                for (int k = 2; k < spectrum.Length; k++)
                {
                    var z = spectrum[k]; double p = z.Real * z.Real + z.Imaginary * z.Imaginary;
                    power += p;
                    peak = Math.Max(peak, p);
                    if (k < 9) low += p;
                    else if (k < 34) vowel += p;
                    else if (k < 98) upper += p;
                }
                double rms = Math.Sqrt(power);
                double rise = Math.Clamp((rms - prev) / (rms + .00001), 0, 1);
                double bass = Math.Sqrt(low);
                double bassRise = Math.Clamp((bass - prevBass) / (bass + .00001), 0, 1);
                double transient = Math.Max(rise, bassRise * Math.Min(1, 2 * low / power));
                double time = start + n * CueSection.Step + j * (size / (double)rate);
                if (transient > .32 && rms > .0001)
                {
                    hits.Add(time);
                    if (beat.IsReliable)
                    {
                        var slot = (int)Math.Round((time - beat.AnchorSeconds) / beat.PeriodSeconds);
                        if (Math.Abs(time - (beat.AnchorSeconds + slot * beat.PeriodSeconds)) < .09)
                            accents[(slot % 4 + 4) % 4] += transient * rms * (.35 + low / power);
                    }
                }
                attack = Math.Max(attack, transient);
                sum += power;
                var middle = Math.Sqrt(vowel + upper);
                modulation += Math.Min(1, Math.Abs(middle - prevMid) / (middle + prevMid + .00001));
                // A voice-like frame has energy in both formant regions and
                // several partials. Pure pads, sub bass and isolated clicks score low.
                voice += Math.Clamp(4 * Math.Sqrt(vowel * upper) / power, 0, 1)
                    * Math.Clamp((.85 - peak / power) * 2, 0, 1) * (1 - rise);
                tone += peak / power;
                prev = rms; prevMid = middle; prevBass = bass;
            }
            bins.Add(new((float)Math.Sqrt(sum / perBin),
                (float)(voice / perBin * Math.Clamp(.35 + 4 * modulation / perBin, 0, 1)),
                (float)attack, (float)(tone / perBin)));
        }
        float reference = bins.Select(b => b.Level).OrderDescending().Skip(bins.Count / 5).FirstOrDefault();
        for (int i = 0; i < bins.Count; i++)
        {
            float relative = Math.Min(2, bins[i].Level / Math.Max(.00001f, reference));
            bins[i] = bins[i] with { Level = relative, Voice = bins[i].Voice * Math.Min(1, relative * 3) };
        }
        var order = Enumerable.Range(0, 4).OrderByDescending(i => accents[i]).ToArray();
        double? bar = beat.IsReliable && end - start >= 8 * beat.PeriodSeconds &&
            accents[order[0]] > 1.35 * accents[order[1]] && accents[order[1]] > 0
            ? beat.AnchorSeconds + order[0] * beat.PeriodSeconds : null;
        return new(start, bins.ToArray(), bar);
    }
}

