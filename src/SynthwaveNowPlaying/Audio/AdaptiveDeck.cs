using NAudio.Wave;

namespace SynthwaveNowPlaying;

// The mixer reads both decks under one gate. Publishing their envelopes between
// buffers gives them the same output-frame origin, even if the UI thread stalls.
internal sealed class RenderGate(ISampleProvider mixer) : ISampleProvider
{
    public WaveFormat WaveFormat => mixer.WaveFormat;
    public int Read(Span<float> buffer) { lock (this) return mixer.Read(buffer); }
}

internal sealed class AdaptiveDeck(ISampleProvider input, BassSwapSampleProvider bass, double offset) : ISampleProvider
{
    public WaveFormat WaveFormat => input.WaveFormat;
    public long Frame { get; set; } = (long)Math.Round(offset * input.WaveFormat.SampleRate);
    public int Rate => WaveFormat.SampleRate;
    public float Gain { get; set; } = 1;
    public long Age { get; private set; }
    public bool Finished => _armed && !_incoming && Age >= _endFrame;
    private bool _armed, _incoming;
    private long _fadeFrames, _endFrame, _hitFrame;
    private int _delayFrames;
    private double _switchAt, _switchWidth;
    private EndingEffect _effect;
    private float[] _tap = [];
    private float[] _prefetch = [];
    private int _prefetchPosition;
    private readonly float[] _filter = new float[input.WaveFormat.Channels];

    // Decode the first packet before adding the incoming deck to the device.
    public void Prefetch()
    {
        _prefetch = new float[2048 * WaveFormat.Channels];
        var count = input.Read(_prefetch);
        Array.Resize(ref _prefetch, count);
        if (count == 0) throw new System.IO.EndOfStreamException("Empty transition entry");
    }

    public void ClearPrefetch() => _prefetchPosition = _prefetch.Length;

    private int ReadSource(Span<float> buffer)
    {
        int buffered = Math.Min(buffer.Length, _prefetch.Length - _prefetchPosition);
        if (buffered == 0) return input.Read(buffer);
        _prefetch.AsSpan(_prefetchPosition, buffered).CopyTo(buffer);
        _prefetchPosition += buffered;
        return buffered == buffer.Length ? buffered : buffered + input.Read(buffer[buffered..]);
    }

    public void Arm(TransitionPlan plan, bool incoming, long waitFrames, double bassSwitch, float gain)
    {
        _armed = true;
        _incoming = incoming;
        Age = -waitFrames;
        Gain = gain;
        _fadeFrames = Math.Max(1, (long)Math.Round(plan.FadeSeconds * Rate));
        _switchAt = bassSwitch;
        _switchWidth = plan.BeatAligned ? .035 : .12;
        _effect = incoming ? EndingEffect.Clean : plan.Effect;
        _endFrame = _fadeFrames;
        if (_effect == EndingEffect.Echo)
        {
            _hitFrame = Math.Max(0, (long)Math.Round((plan.EchoHit - plan.StartSeconds - .012) * Rate));
            _delayFrames = Math.Max(1, (int)Math.Round(plan.EchoDelay * Rate));
            _tap = new float[(int)(Rate * .12) * WaveFormat.Channels];
            _endFrame = Math.Max(_fadeFrames, _hitFrame + _delayFrames * 2 + _tap.Length / WaveFormat.Channels);
        }
    }

    public void Release()
    {
        _armed = false;
        _tap = [];
        Array.Clear(_filter);
        bass.TargetBassGain = 1;
    }

    public int Read(Span<float> buffer)
    {
        int channels = WaveFormat.Channels;
        if (!_armed)
        {
            int read = ReadSource(buffer);
            Frame += read / channels;
            return read;
        }
        int written = 0;
        while (written < buffer.Length)
        {
            if (Finished) break;
            int frames = Math.Min(64, (buffer.Length - written) / channels);
            if (frames == 0) break;
            if (Age < 0) frames = (int)Math.Min(frames, -Age);
            if (!_incoming) frames = (int)Math.Min(frames, _endFrame - Age);
            var block = buffer.Slice(written, frames * channels);
            if (_incoming && Age < 0) block.Clear();
            else
            {
                double progress = Math.Clamp((double)Age / _fadeFrames, 0, 1);
                var swap = Math.Clamp((progress - _switchAt + _switchWidth) / (2 * _switchWidth), 0, 1);
                swap = swap * swap * (3 - 2 * swap);
                bass.TargetBassGain = (float)(_incoming ? swap : 1 - swap);
                int got = ReadSource(block);
                // An outgoing echo can ring across the source EOF. The incoming
                // stream retains normal EOF semantics once it is the active track.
                if (_incoming && got < block.Length) { block = block[..got]; frames = got / channels; }
                else block[got..].Clear();
                Frame += got / channels;
                Render(block, progress);
            }
            Age += frames;
            written += block.Length;
            if (block.IsEmpty) break;
        }
        return written;
    }

    private void Render(Span<float> block, double progress)
    {
        int channels = WaveFormat.Channels;
        int frames = block.Length / channels;
        if (frames == 0) return;
        double end = Math.Clamp((double)(Age + frames) / _fadeFrames, 0, 1);
        double Envelope(double p) => _incoming ? Math.Sin(p * Math.PI / 2) : Math.Cos(p * Math.PI / 2);
        float dry = (float)Envelope(progress);
        float increment = ((float)Envelope(end) - dry) / frames;
        // Logarithmic low-pass sweep with a dry blend that reaches zero
        // gradually. Coefficients update per short block, gain per frame.
        float wet = (float)(progress * progress * .86);
        float pole = _effect == EndingEffect.Filter
            ? (float)Math.Exp(-2 * Math.PI * (14500 * Math.Pow(.085, progress)) / Rate) : 0;
        for (int frame = 0; frame < frames; frame++, dry += increment)
        {
            long tapIndex = Age + frame - _hitFrame;
            for (int c = 0; c < channels; c++)
            {
                int index = frame * channels + c;
                float x = block[index], echo = 0;
                if (_effect == EndingEffect.Filter)
                {
                    _filter[c] = x + pole * (_filter[c] - x);
                    x += wet * (_filter[c] - x);
                }
                else if (_effect == EndingEffect.Echo)
                {
                    int tapFrames = _tap.Length / channels;
                    if ((ulong)tapIndex < (ulong)tapFrames)
                    {
                        // Taper both ends of the captured hit to avoid clicks.
                        float window = Math.Min(1, Math.Min(tapIndex, tapFrames - 1 - tapIndex) / (Rate * .008f));
                        _tap[(int)tapIndex * channels + c] = x * window;
                    }
                    var first = tapIndex - _delayFrames;
                    var second = first - _delayFrames;
                    if ((ulong)first < (ulong)tapFrames) echo += .22f * _tap[(int)first * channels + c];
                    if ((ulong)second < (ulong)tapFrames) echo += .09f * _tap[(int)second * channels + c];
                }
                block[index] = (x * dry + echo) * Gain;
            }
        }
    }
}

