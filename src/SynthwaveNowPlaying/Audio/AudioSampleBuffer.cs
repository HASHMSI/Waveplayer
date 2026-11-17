using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SynthwaveNowPlaying;

// The recorder writes PCM blocks; the FFT thread reads a recent snapshot.
internal sealed class AudioSampleBuffer
{
    private readonly float[] _ring = new float[16384];
    private int _write;
    private readonly object _gate = new();
    private long _lastSignalTick;

    public bool HasRecentSignal => Environment.TickCount64 - Volatile.Read(ref _lastSignalTick) < 80;

    public bool AddBlock(ReadOnlySpan<float> samples, float rms)
    {
        lock (_gate)
        {
            foreach (var sample in samples)
            {
                _ring[_write] = Math.Clamp(sample, -1f, 1f);
                if (++_write == _ring.Length) _write = 0;
            }
        }
        var hasSignal = rms >= .0004f;
        if (hasSignal) Volatile.Write(ref _lastSignalTick, Environment.TickCount64);
        return hasSignal;
    }

    public void CopyLatest(Span<float> destination)
    {
        destination.Clear();
        lock (_gate)
        {
            var available = Math.Min(destination.Length, _ring.Length);
            var start = (_write - available + _ring.Length) % _ring.Length;
            var padding = destination.Length - available;
            var first = Math.Min(available, _ring.Length - start);
            _ring.AsSpan(start, first).CopyTo(destination.Slice(padding, first));
            if (first < available)
                _ring.AsSpan(0, available - first).CopyTo(destination.Slice(padding + first));
        }
    }
}

