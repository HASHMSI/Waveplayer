using NAudio.Wave;

namespace SynthwaveNowPlaying;

/// <summary>Fast peak protection for the single local playback mix.</summary>
internal sealed class PeakLimiterSampleProvider(ISampleProvider source) : ISampleProvider
{
    private float _gain = 1;
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        var count = source.Read(buffer);
        var channels = WaveFormat.Channels;
        for (var frame = 0; frame < count; frame += channels)
        {
            var peak = 0f;
            for (var channel = 0; channel < channels && frame + channel < count; channel++)
                peak = Math.Max(peak, Math.Abs(buffer[frame + channel]));
            var target = peak > .94f ? .94f / peak : 1f;
            _gain = target < _gain ? target : Math.Min(1f, _gain + .00008f);
            for (var channel = 0; channel < channels && frame + channel < count; channel++)
                buffer[frame + channel] *= _gain;
        }
        return count;
    }
}
