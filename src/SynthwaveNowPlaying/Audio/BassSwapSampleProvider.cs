using NAudio.Dsp;
using NAudio.Wave;

namespace SynthwaveNowPlaying;

/// <summary>
/// Splits a local playback stream around the kick/bass range. The high-passed
/// signal and its complement sum to the original at gain 1.
/// Gain changes are smoothed per sample on the audio thread.
/// </summary>
internal sealed class BassSwapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[] _highPass;
    private readonly float _smoothing;
    private float _targetBassGain = 1;
    private float _bassGain = 1;

    public BassSwapSampleProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
        _highPass = new BiQuadFilter[WaveFormat.Channels];
        for (var channel = 0; channel < _highPass.Length; channel++)
            _highPass[channel] = BiQuadFilter.HighPassFilter(WaveFormat.SampleRate, 130, .707f);
        _smoothing = 1 - MathF.Exp(-1f / (WaveFormat.SampleRate * .025f));
    }

    public WaveFormat WaveFormat { get; }
    public float TargetBassGain
    {
        get => Volatile.Read(ref _targetBassGain);
        set => Volatile.Write(ref _targetBassGain, Math.Clamp(value, 0, 1));
    }

    public int Read(Span<float> buffer)
    {
        var read = _source.Read(buffer);
        var channels = WaveFormat.Channels;
        for (var frame = 0; frame < read; frame += channels)
        {
            _bassGain += (TargetBassGain - _bassGain) * _smoothing;
            for (var channel = 0; channel < channels && frame + channel < read; channel++)
            {
                var sample = buffer[frame + channel];
                var low = sample - _highPass[channel].Transform(sample);
                buffer[frame + channel] = sample + (_bassGain - 1) * low;
            }
        }
        return read;
    }
}
