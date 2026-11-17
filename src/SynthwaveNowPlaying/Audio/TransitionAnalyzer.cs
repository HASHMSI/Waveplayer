using NAudio.Wave;

namespace SynthwaveNowPlaying;

internal readonly record struct BeatGrid(double Bpm, double AnchorSeconds, double Confidence, bool Guided = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsReliable => Bpm is >= 70 and <= 200 && Confidence >= (Guided ? .34 : .38);
    [System.Text.Json.Serialization.JsonIgnore]
    public double PeriodSeconds => Bpm > 0 ? 60 / Bpm : double.PositiveInfinity;
    public double Nearest(double time) => AnchorSeconds + Math.Round((time - AnchorSeconds) / PeriodSeconds) * PeriodSeconds;
    public double FirstAtOrAfter(double time) => AnchorSeconds + Math.Ceiling((time - AnchorSeconds) / PeriodSeconds) * PeriodSeconds;
}

internal readonly record struct TransitionProfile(
    double IntroSilence, double OutroSilence, double IntroRms, double OutroRms,
    BeatGrid IntroBeat, BeatGrid OutroBeat, double DurationSeconds = 0,
    double OutroBlendSeconds = 0, double IntroAttack = 0, ClassicTransitionCues? Classic = null,
    TransitionFeatures? Features = null)
{
    public TransitionProfile ForTiming(AutomixTiming timing) =>
        timing == AutomixTiming.Classic && Classic is { } cues
            ? new(cues.IntroSilence, cues.OutroSilence, cues.IntroRms, cues.OutroRms, cues.IntroBeat, cues.OutroBeat, DurationSeconds)
            : this;
}

internal sealed record ClassicTransitionCues(double IntroSilence, double OutroSilence, double IntroRms,
    double OutroRms, BeatGrid IntroBeat, BeatGrid OutroBeat);

/// <summary>Analyses short intro/outro windows on a worker thread; uncertain beats stay unused.</summary>
internal static class TransitionAnalyzer
{
    private const double WindowSeconds = .02;

    public static TransitionProfile Analyze(string file, CancellationToken cancellationToken = default,
        BpmEstimate knownTempo = default)
    {
        using var reader = new AudioFileReader(file);
        var duration = reader.TotalTime.TotalSeconds;
        if (duration < 1) return new TransitionProfile { DurationSeconds = duration };
        var first = Scan(reader, 0, Math.Min(15, duration), cancellationToken);
        // Look far enough back to find the last useful music before a long silent tail.
        var tailStart = Math.Max(0, duration - 45);
        var last = Scan(reader, tailStart, duration, cancellationToken);
        var classic = MeasureClassic(first.Energy, last.Energy, tailStart, duration);
        // Include music on the far side of long digital silence. Overlap the
        // windows so a cut at the boundary still has preceding musical context.
        var tail = TransitionDynamics.MeasureOutro(last.Energy, WindowSeconds);
        while (tailStart > 0 && tail.Seconds < 14)
        {
            var end = tailStart + 14;
            tailStart = Math.Max(0, end - 45);
            last = Scan(reader, tailStart, end, cancellationToken);
            tail = TransitionDynamics.MeasureOutro(last.Energy, WindowSeconds);
        }
        var musicalEnd = tail.Seconds > 0 ? Math.Min(duration, tailStart + tail.Seconds) : duration;
        var beatStart = Math.Max(0, musicalEnd - 12);
        var beatTail = Scan(reader, beatStart, musicalEnd, cancellationToken);
        var intro = first.ActiveRms > 0 ? Math.Max(0, first.FirstActive - .1) : 0;
        var introBeat = EstimateGuidedBeats(first.Energy, 0, first.FirstActive, first.LastActive, knownTempo);
        var outroBeat = EstimateGuidedBeats(beatTail.Energy, beatStart, beatTail.FirstActive, beatTail.LastActive, knownTempo);
        if (knownTempo.Bpm > 0)
        {
            introBeat = EstimateSpectralBeats(reader, 0, Math.Min(16, duration), knownTempo, introBeat, cancellationToken);
            outroBeat = EstimateSpectralBeats(reader, beatStart, musicalEnd - beatStart, knownTempo, outroBeat, cancellationToken);
        }
        var features = TransitionFeatures.Analyze(reader, intro, musicalEnd, introBeat, outroBeat, cancellationToken);
        return new TransitionProfile(
            Math.Min(intro, duration * .5),
            duration - musicalEnd,
            first.ActiveRms,
            last.ActiveRms,
            introBeat, outroBeat, duration, tail.BlendSeconds, TransitionDynamics.MeasureIntro(first.Energy, WindowSeconds, intro), classic, features);
    }

    // Both timing modes share the decoded RMS frames. Classic uses the original
    // fixed activity floor and the smoothed -13 dB cue within the final 45 s.
    private static ClassicTransitionCues MeasureClassic(float[] intro, float[] tail, double tailStart, double duration)
    {
        const double floor = .004;
        const int smoothFrames = 35;
        var smoothed = new double[tail.Length];
        double sum = 0;
        for (var i = 0; i < tail.Length; i++)
        {
            sum += tail[i];
            if (i >= smoothFrames) sum -= tail[i - smoothFrames];
            smoothed[i] = sum / Math.Min(i + 1, smoothFrames);
        }
        var referenceFrames = Math.Max(1, tail.Length - 100);
        var reference = smoothed.Take(referenceFrames).Order().ElementAtOrDefault((int)(referenceFrames * .8));
        var threshold = Math.Max(floor, reference * Math.Pow(10, -13d / 20));
        var last = Array.FindLastIndex(smoothed, level => level >= threshold);
        var end = Math.Clamp(tailStart + (last + 1) * WindowSeconds - .35, tailStart, duration);
        var beatStart = Math.Max(tailStart, end - 12);
        var beatTail = tail;
        if (end - beatStart >= 5)
        {
            var from = Math.Clamp((int)Math.Round((beatStart - tailStart) / WindowSeconds), 0, tail.Length);
            var to = Math.Clamp((int)Math.Round((end - tailStart) / WindowSeconds), from, tail.Length);
            beatStart = tailStart + from * WindowSeconds;
            beatTail = tail[from..to];
        }
        else beatStart = tailStart;

        static (double Start, double End, double Rms) Activity(float[] levels, double start)
        {
            var first = Array.FindIndex(levels, level => level >= floor);
            if (first < 0) return (start + levels.Length * WindowSeconds, start, 0);
            var last = Array.FindLastIndex(levels, level => level >= floor);
            var rms = Math.Sqrt(levels.Where(v => v >= floor).Average(v => (double)v * v));
            return (start + first * WindowSeconds, start + (last + 1) * WindowSeconds, rms);
        }
        var opening = Activity(intro, 0);
        var ending = Activity(beatTail, beatStart);
        return new(Math.Clamp(opening.Start - .1, 0, Math.Min(12, duration * .5)), duration - end,
            opening.Rms, Activity(tail, tailStart).Rms,
            EstimateBeats(intro, 0, opening.Start, opening.End),
            EstimateBeats(beatTail, beatStart, ending.Start, ending.End));
    }

    private static (double FirstActive, double LastActive, double ActiveRms, float[] Energy)
        Scan(AudioFileReader reader, double from, double to, CancellationToken cancellationToken = default)
    {
        reader.CurrentTime = TimeSpan.FromSeconds(from);
        var sampleRate = reader.WaveFormat.SampleRate;
        var channels = reader.WaveFormat.Channels;
        var frames = Math.Max(1, (int)Math.Round(sampleRate * WindowSeconds));
        var buffer = new float[frames * channels];
        var energy = new List<float>((int)((to - from) / WindowSeconds) + 1);
        var windows = (int)Math.Ceiling((to - from) / WindowSeconds);
        for (var window = 0; window < windows; window++)
        {
            if (window % 50 == 0) cancellationToken.ThrowIfCancellationRequested();
            var read = reader.Read(buffer.AsSpan());
            if (read == 0) break;
            double squares = 0;
            for (var i = 0; i < read; i++) squares += buffer[i] * buffer[i];
            var rms = Math.Sqrt(squares / read);
            energy.Add((float)rms);
        }
        var levels = energy.ToArray();
        var threshold = Math.Max(.00003, TransitionDynamics.Reference(levels) * .035);
        var first = Array.FindIndex(levels, v => v > threshold);
        if (first < 0) return (to, from, 0, levels);
        var last = Array.FindLastIndex(levels, v => v > threshold);
        var rmsActive = Math.Sqrt(levels.Where(v => v > threshold).Average(v => (double)v * v));
        var frameSeconds = (double)frames / sampleRate;
        return (from + first * frameSeconds, Math.Min(to, from + (last + 1) * frameSeconds), rmsActive, levels);
    }

    private static BeatGrid EstimateBeats(float[] energy, double start, double firstActive, double lastActive)
    {
        if (energy.Length < 220 || lastActive - firstActive < 5) return default;
        var onset = new float[energy.Length];
        for (var i = 3; i < energy.Length; i++)
        {
            var baseline = (energy[i - 1] + energy[i - 2] + energy[i - 3]) / 3;
            onset[i] = Math.Max(0, energy[i] - baseline);
        }
        var max = onset.Max();
        if (max < .006f) return default;
        var peaks = new List<int>();
        for (var i = 2; i < onset.Length - 2; i++)
            if (onset[i] > max * .22f && onset[i] >= onset[i - 1] && onset[i] > onset[i + 1] &&
                (peaks.Count == 0 || i - peaks[^1] >= 7)) peaks.Add(i);
        if (peaks.Count < 6) return default;

        var bestLag = 0;
        var bestCorrelation = 0.0;
        var scores = new double[45];
        for (var lag = 20; lag <= 43; lag++)
        {
            double product = 0, left = 0, right = 0;
            for (var i = lag; i < onset.Length; i++)
            {
                product += onset[i] * onset[i - lag];
                left += onset[i] * onset[i];
                right += onset[i - lag] * onset[i - lag];
            }
            scores[lag] = product / Math.Sqrt(left * right + 1e-12);
            if (scores[lag] > bestCorrelation) { bestCorrelation = scores[lag]; bestLag = lag; }
        }
        if (bestCorrelation < .43 || bestLag == 0) return default;
        var periodFrames = (double)bestLag;
        if (bestLag is > 20 and < 43)
        {
            var before = scores[bestLag - 1];
            var center = scores[bestLag];
            var after = scores[bestLag + 1];
            var denominator = before - 2 * center + after;
            if (Math.Abs(denominator) > 1e-8)
                periodFrames += Math.Clamp(.5 * (before - after) / denominator, -.5, .5);
        }

        // Fit a phase only to strong onsets; a steady pad or irregular percussion falls back.
        var bestHits = 0;
        var bestPhaseScore = 0.0;
        var bestAnchor = 0.0;
        foreach (var candidate in peaks)
        {
            var hits = 0;
            var slots = 0;
            double strength = 0;
            for (var beat = candidate % periodFrames; beat < onset.Length; beat += periodFrames)
            {
                var time = start + beat * WindowSeconds;
                if (time < firstActive || time > lastActive) continue;
                var index = (int)Math.Round(beat);
                var localPeak = 0f;
                for (var offset = -2; offset <= 2; offset++)
                    if (index + offset >= 0 && index + offset < onset.Length)
                        localPeak = Math.Max(localPeak, onset[index + offset]);
                slots++;
                if (localPeak > max * .22f) hits++;
                strength += localPeak / max;
            }
            if (slots < 5 || hits < 4) continue;
            var phaseScore = (double)hits / slots + strength / slots * .2;
            if (phaseScore > bestPhaseScore) { bestPhaseScore = phaseScore; bestHits = hits; bestAnchor = start + candidate * WindowSeconds; }
        }
        if (bestHits < 4 || bestPhaseScore < .62) return default;
        var hitFraction = Math.Min(1, bestPhaseScore / 1.2);
        var confidence = bestCorrelation * hitFraction;
        var bpm = 60 / (periodFrames * WindowSeconds);
        return new BeatGrid(bpm, bestAnchor, confidence);
    }

    // A full-track BPM supplies the period, not the phase. Fit the phase to
    // local attacks so an intro or outro can still reject an uncertain beat.
    private static BeatGrid EstimateGuidedBeats(float[] energy, double start, double firstActive,
        double lastActive, BpmEstimate tempo)
    {
        var fallback = EstimateBeats(energy, start, firstActive, lastActive);
        var pulseBpm = PulseBpm(tempo.Bpm);
        if (pulseBpm == 0 || tempo.Confidence < .19 ||
            energy.Length < 220 || lastActive - firstActive < 5) return fallback;
        if (fallback.IsReliable && Math.Abs(fallback.Bpm - pulseBpm) / pulseBpm < .035)
            return fallback;

        var period = 60 / (pulseBpm * WindowSeconds);
        var onset = new float[energy.Length];
        for (var i = 3; i < energy.Length; i++)
        {
            var baseline = (energy[i - 1] + energy[i - 2] + energy[i - 3]) / 3;
            onset[i] = Math.Max(0, energy[i] - baseline);
        }
        var peak = onset.Max();
        if (peak < .001f) return fallback;
        var threshold = peak * .18f;
        var from = Math.Clamp((int)Math.Ceiling((firstActive - start) / WindowSeconds), 0, onset.Length - 1);
        var to = Math.Clamp((int)Math.Floor((lastActive - start) / WindowSeconds), from, onset.Length - 1);
        double bestScore = 0, bestAnchor = 0;
        int bestHits = 0, bestSlots = 0;
        for (var phase = 0d; phase < period; phase += .5)
        {
            double strength = 0, offbeat = 0;
            int hits = 0, slots = 0;
            for (var beat = phase; beat <= to; beat += period)
            {
                if (beat < from) continue;
                var local = LocalAttack(onset, beat);
                strength += local;
                offbeat += LocalAttack(onset, beat + period / 2);
                if (local >= threshold) hits++;
                slots++;
            }
            if (slots < 8 || hits < slots * .42) continue;
            var hitRate = (double)hits / slots;
            var score = .62 * hitRate + .38 * strength / (slots * peak);
            // Evenly spread noise has no distinct beat phase.
            if (strength < offbeat * 1.12 || score <= bestScore) continue;
            bestScore = score;
            bestAnchor = start + phase * WindowSeconds;
            bestHits = hits;
            bestSlots = slots;
        }
        if (bestSlots < 8 || bestHits < 6 || bestScore < .43) return fallback;
        var confidence = bestScore * (.57 + .43 * tempo.Confidence);
        return new BeatGrid(pulseBpm, bestAnchor, Math.Clamp(confidence, 0, 1), Guided: true);
    }

    private static float LocalAttack(float[] onset, double position)
    {
        var center = (int)Math.Round(position);
        float peak = 0;
        for (var offset = -2; offset <= 2; offset++)
            if ((uint)(center + offset) < onset.Length)
                peak = Math.Max(peak, onset[center + offset]);
        return peak;
    }

    private static BeatGrid EstimateSpectralBeats(AudioFileReader reader, double start, double seconds,
        BpmEstimate tempo, BeatGrid fallback, CancellationToken token)
    {
        var pulseBpm = PulseBpm(tempo.Bpm);
        if (fallback.IsReliable || pulseBpm == 0 || tempo.Confidence < .19 || seconds < 5)
            return fallback;
        var flux = FullTrackBpmAnalyzer.ReadFlux(reader, start, seconds, token, brightFocus: true);
        if (flux.Count < FullTrackBpmAnalyzer.FrameRate * 5) return fallback;
        var localTempo = FullTrackBpmAnalyzer.EstimateFromFlux(flux, token);
        if (localTempo.Confidence < .18 || Math.Abs(PulseBpm(localTempo.Bpm) - pulseBpm) / pulseBpm > .045)
            return fallback;

        var rate = FullTrackBpmAnalyzer.FrameRate;
        var period = rate * 60 / pulseBpm;
        var onset = new float[flux.Count];
        double sum = 0;
        for (var i = 0; i < flux.Count; i++)
        {
            sum += flux[i] - (i >= 48 ? flux[i - 48] : 0);
            onset[i] = (float)Math.Max(0, flux[i] - .85 * sum / Math.Min(i + 1, 48));
        }
        var sorted = (float[])onset.Clone();
        Array.Sort(sorted);
        var reference = sorted[(int)(sorted.Length * .98)];
        if (reference < .0001f) return fallback;
        var threshold = reference * .45f;
        double bestScore = 0, bestAnchor = 0;
        for (var phase = 0d; phase < period; phase += .5)
        {
            double strength = 0, offbeat = 0;
            int hits = 0, slots = 0;
            for (var beat = phase; beat < onset.Length - 2; beat += period)
            {
                var peak = SpectralPeak(onset, beat);
                strength += Math.Min(peak / reference, 2);
                offbeat += Math.Min(SpectralPeak(onset, beat + period / 2) / reference, 2);
                if (peak >= threshold) hits++;
                slots++;
            }
            if (slots < 10 || hits < slots * .38 || strength < offbeat * 1.12) continue;
            var score = .65 * hits / slots + .35 * strength / slots;
            if (score <= bestScore) continue;
            bestScore = score;
            bestAnchor = start + phase / rate;
        }
        var confidence = bestScore * (.57 + .43 * tempo.Confidence) *
            Math.Clamp(.75 + localTempo.Confidence * .5, 0, 1);
        return confidence >= .34
            ? new BeatGrid(pulseBpm, bestAnchor, Math.Clamp(confidence, 0, 1), Guided: true)
            : fallback;
    }

    private static double PulseBpm(double bpm)
    {
        if (bpm is < 40 or > 240 || !double.IsFinite(bpm)) return 0;
        if (bpm < 70) bpm *= 2;
        if (bpm > 200) bpm /= 2;
        return bpm;
    }

    private static float SpectralPeak(float[] values, double position)
    {
        var center = (int)Math.Round(position);
        float peak = 0;
        for (var offset = -2; offset <= 2; offset++)
            if ((uint)(center + offset) < values.Length)
                peak = Math.Max(peak, values[center + offset]);
        return peak;
    }
}
