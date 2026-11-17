using NAudio.Wave;

namespace SynthwaveNowPlaying;

internal sealed class SpectrumTap(ISampleProvider source, AudioSampleBuffer samples,
    ArchivedVisualizer archived, Func<bool> classicSelected, AutoResetEvent signal, Action wakeRenderer) : ISampleProvider
{
    // Feed the original 20 ms analysis blocks from local playback. No system
    // recorder or archived player window is created.
    private readonly float[] _classicPacket = new float[Math.Max(1, source.WaveFormat.SampleRate / 50)];
    private int _classicCount;
    private double _classicEnergy;

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(Span<float> output)
    {
        int count = source.Read(output);
        int channels = WaveFormat.Channels;
        Span<float> mono = stackalloc float[512];
        int frames = count / channels;
        bool classic = classicSelected();
        if (!classic) { _classicCount = 0; _classicEnergy = 0; }
        bool active = false;
        for (int start = 0; start < frames; start += mono.Length)
        {
            int length = Math.Min(mono.Length, frames - start);
            float energy = 0;
            for (int i = 0; i < length; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += output[(start + i) * channels + c];
                mono[i] = sum / channels;
                float power = mono[i] * mono[i];
                if (classic)
                {
                    _classicPacket[_classicCount++] = mono[i];
                    _classicEnergy += power;
                    if (_classicCount == _classicPacket.Length)
                    {
                        active |= archived.AddBlock(_classicPacket, (float)Math.Sqrt(_classicEnergy / _classicCount));
                        _classicCount = 0;
                        _classicEnergy = 0;
                    }
                }
                else energy += power;
            }
            if (!classic) active |= samples.AddBlock(mono[..length], MathF.Sqrt(energy / length));
        }
        if (active)
        {
            signal.Set();
            wakeRenderer();
        }
        return count;
    }
}
