using NAudio.Dsp;
using NAudio.Wave;

namespace SynthwaveNowPlaying;

/// <summary>Six-band equalizer that only processes the app's own AudioFileReader stream.</summary>
internal sealed class LocalEqualizer : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly float[] _frequencies = [60, 150, 400, 1000, 3000, 8000];
    private readonly float[] _gains = new float[6];
    private BiQuadFilter[][] _filters;

    public LocalEqualizer(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, _channels);
        _filters = new BiQuadFilter[_frequencies.Length][];
        for (var band = 0; band < _filters.Length; band++) _filters[band] = CreateFilterRow(band);
    }

    public WaveFormat WaveFormat { get; }
    public bool Enabled { get; set; }

    public void SetGain(int band, float gainDb)
    {
        if ((uint)band >= _gains.Length) return;
        gainDb = Math.Clamp(gainDb, -12, 12);
        if (Math.Abs(_gains[band] - gainDb) < .05f) return;
        _gains[band] = gainDb;
        Volatile.Write(ref _filters[band], CreateFilterRow(band));
    }

    public int Read(Span<float> buffer)
    {
        var read = _source.Read(buffer);
        if (!Enabled) return read;
        for (var i = 0; i < read; i++)
        {
            var channel = i % _channels;
            var sample = buffer[i];
            for (var band = 0; band < _frequencies.Length; band++)
            {
                var filters = Volatile.Read(ref _filters[band]);
                if (filters.Length > 0) sample = filters[channel].Transform(sample);
            }
            buffer[i] = Math.Clamp(sample, -1.2f, 1.2f);
        }
        return read;
    }

    private BiQuadFilter[] CreateFilterRow(int band)
    {
        // Low-rate sources cannot represent EQ bands at or above Nyquist.
        if (_frequencies[band] >= _sampleRate * .5f) return [];
        var filters = new BiQuadFilter[_channels];
        for (var channel = 0; channel < filters.Length; channel++)
            filters[channel] = BiQuadFilter.PeakingEQ(_sampleRate, _frequencies[band], 1.0f, _gains[band]);
        return filters;
    }
}
