using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SynthwaveNowPlaying;

internal readonly record struct BpmEstimate(double Bpm, double Confidence);

/// <summary>Estimates tempo from spectral onsets and checks agreement across sections.</summary>
internal static class FullTrackBpmAnalyzer
{
    private const int SampleRate = 11025;
    private const int FftSize = 1024;
    private const int Hop = 128;
    private const int Bands = 28;
    private static readonly double FrameRate = (double)SampleRate / Hop;

    public static BpmEstimate Analyze(string file, CancellationToken cancellationToken)
    {
        using var reader = new AudioFileReader(file);
        var duration = reader.TotalTime.TotalSeconds;
        if (duration <= 240)
            return EstimateConsensus(ReadFlux(reader, 0, duration, cancellationToken), cancellationToken);
        // Sample across a long track, including its second half. Each section
        // starts with fresh resampler and FFT history so a seek is never an onset.
        var votes = new List<BpmEstimate>();
        foreach (var fraction in new[] { .5, .2, .8, .35, .65, .06, .94 })
        {
            var flux = ReadFlux(reader, (duration - 32) * fraction, 32, cancellationToken);
            var estimate = EstimateFromFlux(flux, cancellationToken);
            if (estimate.Bpm > 0 && estimate.Confidence >= .16) votes.Add(estimate);
        }
        return Combine(votes, default);
    }

    private static List<float> ReadFlux(AudioFileReader reader, double start, double seconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        reader.CurrentTime = TimeSpan.FromSeconds(start);
        ISampleProvider source = reader;
        if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
        else if (source.WaveFormat.Channels != 1) return [];
        if (source.WaveFormat.SampleRate != SampleRate)
            source = new WdlResamplingSampleProvider(source, SampleRate);

        var fft = new FftProcessor(FftSize, FftWindowType.Hann);
        var frame = new float[FftSize];
        var spectrum = new NAudio.Dsp.Complex[fft.SpectrumLength];
        var previousBands = new double[Bands];
        var bandStarts = new int[Bands + 1];
        for (var band = 0; band <= Bands; band++)
        {
            var frequency = 45 * Math.Pow(5000d / 45, band / (double)Bands);
            bandStarts[band] = Math.Clamp((int)(frequency * FftSize / SampleRate), 1, FftSize / 2);
        }
        if (Fill(source, frame, 0, FftSize) < FftSize) return [];
        var flux = new List<float>();
        var maxFrames = (int)(seconds * FrameRate);
        for (var index = 0; index < maxFrames; index++)
        {
            if (index % 128 == 0) cancellationToken.ThrowIfCancellationRequested();
            fft.RealForward(frame, spectrum);
            double onset = 0;
            for (var band = 0; band < Bands; band++)
            {
                double energy = 0;
                var end = Math.Max(bandStarts[band] + 1, bandStarts[band + 1]);
                for (var bin = bandStarts[band]; bin < end; bin++)
                {
                    var value = spectrum[bin];
                    energy += Math.Sqrt(value.Real * value.Real + value.Imaginary * value.Imaginary);
                }
                var level = Math.Log(1 + energy * 150 / (end - bandStarts[band]));
                onset += Math.Max(0, level - previousBands[band]) * (band < 9 ? 1.25 : 1);
                previousBands[band] = level;
            }
            flux.Add((float)onset);
            Array.Copy(frame, Hop, frame, 0, FftSize - Hop);
            if (Fill(source, frame, FftSize - Hop, Hop) < Hop) break;
        }
        return flux;
    }

    private static BpmEstimate EstimateConsensus(IReadOnlyList<float> flux, CancellationToken cancellationToken)
    {
        var wholeTrack = EstimateFromFlux(flux, cancellationToken);
        var window = (int)(32 * FrameRate);
        var stride = (int)(24 * FrameRate);
        if (flux.Count < window * 2) return wholeTrack;

        // Separate windows prevent an intro, breakdown, or tempo change from
        // dominating the BPM of the musical body.
        var votes = new List<BpmEstimate>();
        for (var offset = 0; offset + window <= flux.Count; offset += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slice = new float[window];
            for (var index = 0; index < window; index++) slice[index] = flux[offset + index];
            var estimate = EstimateFromFlux(slice, cancellationToken);
            if (estimate.Bpm > 0 && estimate.Confidence >= .09) votes.Add(estimate);
        }
        return Combine(votes, wholeTrack);
    }

    private static BpmEstimate Combine(List<BpmEstimate> votes, BpmEstimate wholeTrack)
    {
        if (votes.Count < 2) return wholeTrack;

        var bestWeight = 0d;
        var bestBpm = 0d;
        var bestCount = 0;
        foreach (var candidate in votes)
        {
            double weight = 0, bpmSum = 0;
            var count = 0;
            foreach (var vote in votes)
            {
                if (Math.Abs(vote.Bpm - candidate.Bpm) / candidate.Bpm > .035) continue;
                var strength = Math.Max(.01, vote.Confidence * vote.Confidence);
                weight += strength;
                bpmSum += vote.Bpm * strength;
                count++;
            }
            if (weight <= bestWeight) continue;
            bestWeight = weight;
            bestBpm = bpmSum / weight;
            bestCount = count;
        }
        if (bestCount < 2) return wholeTrack;
        var agreement = (double)bestCount / votes.Count;
        if (agreement < .45) return wholeTrack.Confidence >= .32 ? wholeTrack : default;
        if (wholeTrack.Bpm > 0 && Math.Abs(wholeTrack.Bpm - bestBpm) / bestBpm < .035)
            bestBpm = bestBpm * .75 + wholeTrack.Bpm * .25;
        return new BpmEstimate(bestBpm, Math.Clamp(Math.Sqrt(bestWeight / bestCount) * (0.65 + agreement * .35), 0, 1));
    }

    private static int Fill(ISampleProvider source, float[] buffer, int offset, int count)
    {
        var filled = 0;
        while (filled < count)
        {
            var read = source.Read(buffer.AsSpan(offset + filled, count - filled));
            if (read == 0) break;
            filled += read;
        }
        return filled;
    }

    internal static BpmEstimate EstimateFromFlux(IReadOnlyList<float> flux, CancellationToken token = default)
    {
        if (flux.Count < FrameRate * 8) return default;
        var enhanced = new double[flux.Count];
        double moving = 0, energy = 0;
        const int meanWidth = 48;
        for (var i = 0; i < flux.Count; i++)
        {
            moving += flux[i] - (i >= meanWidth ? flux[i - meanWidth] : 0);
            var localMean = moving / Math.Min(i + 1, meanWidth);
            enhanced[i] = Math.Max(0, flux[i] - localMean * .85);
            energy += enhanced[i] * enhanced[i];
        }
        if (energy / flux.Count < 1e-7) return default;

        var minLag = (int)(FrameRate * 60 / 190);
        var maxLag = (int)(FrameRate * 60 / 55);
        var correlations = new double[maxLag * 3 + 1];
        for (var lag = minLag; lag < correlations.Length; lag++)
        {
            token.ThrowIfCancellationRequested();
            double product = 0, left = 0, right = 0;
            for (var i = lag; i < enhanced.Length; i++)
            {
                var a = enhanced[i];
                var b = enhanced[i - lag];
                product += a * b;
                left += a * a;
                right += b * b;
            }
            correlations[lag] = product / Math.Sqrt(left * right + 1e-12);
        }

        var bestLag = 0;
        var bestScore = 0d;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var bpm = FrameRate * 60 / lag;
            var prior = Math.Exp(-.5 * Math.Pow(Math.Log(bpm / 110) / .32, 2));
            var score = correlations[lag] + .38 * correlations[lag * 2] + .16 * correlations[lag * 3];
            score *= .72 + .28 * prior;
            if (score > bestScore) { bestScore = score; bestLag = lag; }
        }
        if (bestLag == 0 || bestScore < .075) return default;
        var baseline = correlations[minLag..(maxLag + 1)].Order().ElementAt((maxLag - minLag) / 2);
        if (correlations[bestLag] < baseline + .055) return default;

        var period = (double)bestLag;
        if (bestLag > minLag && bestLag < maxLag)
        {
            var before = correlations[bestLag - 1];
            var center = correlations[bestLag];
            var after = correlations[bestLag + 1];
            var denominator = before - 2 * center + after;
            if (Math.Abs(denominator) > 1e-8)
                period += Math.Clamp(.5 * (before - after) / denominator, -.5, .5);
        }
        var result = FrameRate * 60 / period;
        // Integer autocorrelation lags can favor half-time when the true beat
        // period falls between frames. Strong, regular onset spacing resolves it.
        var onsetRate = EstimateOnsetRate(enhanced);
        if (onsetRate.Bpm > 0 && onsetRate.Confidence >= .72 &&
            Math.Abs(onsetRate.Bpm - result * 2) / onsetRate.Bpm < .045)
            return new BpmEstimate(onsetRate.Bpm, Math.Max(onsetRate.Confidence, Math.Clamp(bestScore, 0, 1)));
        return new BpmEstimate(result, Math.Clamp(bestScore, 0, 1));
    }

    private static BpmEstimate EstimateOnsetRate(double[] enhanced)
    {
        if (enhanced.Length < FrameRate * 8) return default;
        var ordered = (double[])enhanced.Clone();
        Array.Sort(ordered);
        var threshold = Math.Max(ordered[(int)(ordered.Length * .99)] * .45, ordered[^1] * .05);
        if (threshold <= 1e-5) return default;
        var peaks = new List<int>();
        var minimumGap = (int)(FrameRate * .23);
        for (var index = 1; index < enhanced.Length - 1; index++)
        {
            if (enhanced[index] < threshold || enhanced[index] < enhanced[index - 1] ||
                enhanced[index] <= enhanced[index + 1]) continue;
            if (peaks.Count > 0 && index - peaks[^1] < minimumGap)
            {
                if (enhanced[index] > enhanced[peaks[^1]]) peaks[^1] = index;
                continue;
            }
            peaks.Add(index);
        }
        if (peaks.Count < 9) return default;
        var gaps = new List<int>();
        for (var index = 1; index < peaks.Count; index++)
        {
            var gap = peaks[index] - peaks[index - 1];
            if (gap >= FrameRate * 60 / 190 && gap <= FrameRate * 60 / 55) gaps.Add(gap);
        }
        if (gaps.Count < 8) return default;
        gaps.Sort();
        var median = gaps[gaps.Count / 2];
        var coherent = gaps.Where(gap => Math.Abs(gap - median) <= median * .08).ToArray();
        return new BpmEstimate(FrameRate * 60 / coherent.Average(), (double)coherent.Length / gaps.Count);
    }
}
