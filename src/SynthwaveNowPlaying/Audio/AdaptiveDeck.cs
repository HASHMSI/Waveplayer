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
    private bool _v2;
    private bool _dropCut;
    private bool _sweepEcho;
    private bool _roll;
    private float[] _rollHistory = [];
    private int _rollWrite;
    private long _rollFreezeAge = -1;
    private int _rollDivisor = 1;
    private long _rollStageAge;
    private readonly float[] _rollLast = new float[input.WaveFormat.Channels];
    private readonly float[] _rollStageOrigin = new float[input.WaveFormat.Channels];
    private readonly float[] _sweepFilter = new float[input.WaveFormat.Channels];
    private float[] _space = [];
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

    public void Arm(TransitionPlan plan, bool incoming, long waitFrames, double bassSwitch, float gain, bool v2 = false)
    {
        _armed = true;
        _incoming = incoming;
        Age = -waitFrames;
        Gain = gain;
        _v2 = v2;
        _dropCut = v2 && plan.Effect == EndingEffect.DropCut;
        _sweepEcho = v2 && plan.Effect is EndingEffect.SweepEcho or EndingEffect.NeonRoll;
        _roll = v2 && !incoming && plan.Effect == EndingEffect.NeonRoll;
        _rollWrite = 0;
        _rollFreezeAge = -1;
        _rollDivisor = 1;
        Array.Clear(_rollLast);
        Array.Clear(_rollStageOrigin);
        _rollHistory = _roll ? new float[(int)(Rate * Math.Clamp(plan.EchoDelay > 0 ? plan.EchoDelay : .24, .14, .38)) * WaveFormat.Channels] : [];
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
        else if (_effect is EndingEffect.SpaceEcho or EndingEffect.SweepEcho or EndingEffect.NeonRoll)
        {
            var delay = _sweepEcho ? Math.Clamp(plan.EchoDelay > 0 ? plan.EchoDelay : .24, .14, .38) : .18;
            _delayFrames = Math.Max(1, (int)Math.Round(Rate * delay));
            _space = new float[_delayFrames * WaveFormat.Channels];
            _endFrame = _fadeFrames + (long)(Rate * .72);
        }
    }

    public void Release()
    {
        _armed = false;
        _tap = [];
        _space = [];
        _rollHistory = [];
        Array.Clear(_filter);
        Array.Clear(_sweepFilter);
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
            if (Age < 0)
            {
                if (_incoming) block.Clear();
                else
                {
                    // The decoder is armed before the cue. Keep playing the old
                    // track normally; effect rings cannot use a negative age.
                    int got = ReadSource(block);
                    block[got..].Clear();
                    Frame += got / channels;
                    for (int i = 0; i < block.Length; i++) block[i] *= Gain;
                }
            }
            else
            {
                double progress = Math.Clamp((double)Age / _fadeFrames, 0, 1);
                var swap = _dropCut
                    ? Math.Clamp((progress - _switchAt + .045) / .045, 0, 1)
                    : Math.Clamp((progress - _switchAt + _switchWidth) / (2 * _switchWidth), 0, 1);
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
        double Envelope(double p)
        {
            if (_sweepEcho)
                return _incoming ? Smooth((p - (_switchAt - .07)) / .21)
                    : 1 - Smooth((p - (_switchAt - .22)) / .24);
            if (_dropCut)
            {
                static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
                return _incoming
                    ? Smooth((p - (_switchAt - .005)) / .04)
                    : 1 - Smooth((p - (_switchAt - .09)) / .09);
            }
            var shape = _incoming ? Math.Sin(p * Math.PI / 2) : Math.Cos(p * Math.PI / 2);
            // A modest center trim gives two dense recordings room to coexist.
            return _v2 ? shape * (1 - .06 * Math.Pow(Math.Sin(Math.PI * p), 2)) : shape;
        }
        float dry = (float)Envelope(progress);
        float increment = ((float)Envelope(end) - dry) / frames;
        // Logarithmic low-pass sweep with a dry blend that reaches zero
        // gradually. Coefficients update per short block, gain per frame.
        float wet = (float)(progress * progress * .86);
        float pole = _effect == EndingEffect.Filter
            ? (float)Math.Exp(-2 * Math.PI * (14500 * Math.Pow(.085, progress)) / Rate) : 0;
        var build = Smooth(progress / Math.Max(.1, _switchAt));
        var cutoff = _incoming ? 700 * Math.Pow(26, Smooth((progress - _switchAt + .07) / .21))
            : 45 * Math.Pow(55, build);
        var sweepPole = _sweepEcho ? (float)Math.Exp(-2 * Math.PI * cutoff / Rate) : 0;
        bool delayActive = _effect is EndingEffect.SpaceEcho or EndingEffect.SweepEcho or EndingEffect.NeonRoll;
        for (int frame = 0; frame < frames; frame++, dry += increment)
        {
            long tapIndex = Age + frame - _hitFrame;
            int spaceBase = delayActive
                ? (int)((Age + frame) % _delayFrames) * channels : 0;
            float spaceLeft = delayActive ? _space[spaceBase] : 0;
            float spaceRight = delayActive && channels > 1
                ? _space[spaceBase + 1] : spaceLeft;
            var sampleProgress = (Age + frame) / (double)_fadeFrames;
            var rollStart = Math.Max(.05, _switchAt - Math.Min(.24, 1.2 * Rate / _fadeFrames));
            if (_roll && _rollFreezeAge < 0 && sampleProgress >= rollStart)
            {
                _rollFreezeAge = Age + frame;
                _rollStageAge = Age + frame;
            }
            var rollFraction = Math.Clamp((sampleProgress - rollStart) / Math.Max(.02, _switchAt - rollStart), 0, 1);
            int rollDivisor = rollFraction < .42 ? 1 : rollFraction < .75 ? 2 : 4;
            if (_roll && _rollFreezeAge >= 0 && rollDivisor != _rollDivisor)
            {
                _rollDivisor = rollDivisor;
                _rollStageAge = Age + frame;
                _rollLast.CopyTo(_rollStageOrigin, 0);
            }
            for (int c = 0; c < channels; c++)
            {
                int index = frame * channels + c;
                float x = block[index], echo = 0;
                if (_roll)
                {
                    int historyFrames = _rollHistory.Length / channels;
                    if (_rollFreezeAge < 0) _rollHistory[_rollWrite * channels + c] = x;
                    else
                    {
                        // Freeze the most recent slice, then halve its length twice.
                        // Short tapers at every slice edge prevent digital clicks.
                        int length = Math.Max(32, historyFrames / _rollDivisor);
                        int phase = (int)((Age + frame - _rollStageAge) % length);
                        int position = (_rollWrite + historyFrames - length + phase) % historyFrames;
                        float taper = (float)Math.Min(1, Math.Min(phase, length - 1 - phase) / (Rate * .004));
                        var frozen = _rollHistory[position * channels + c] * taper;
                        frozen = (float)(_rollStageOrigin[c] + Smooth((Age + frame - _rollStageAge) / (Rate * .005)) * (frozen - _rollStageOrigin[c]));
                        _rollLast[c] = frozen;
                        float blend = (float)(.93 * Smooth((sampleProgress - rollStart) / .035));
                        x += blend * (frozen - x);
                    }
                }
                if (_sweepEcho)
                {
                    _sweepFilter[c] = x + sweepPole * (_sweepFilter[c] - x);
                    x = _incoming ? _sweepFilter[c] : x - (float)build * _sweepFilter[c];
                }
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
                else if (delayActive)
                {
                    int slot = spaceBase + c;
                    // Cross-feed stereo repeats for movement without another decoder.
                    float delayed = channels == 2 ? (c == 0 ? spaceRight : spaceLeft) : _space[slot];
                    if (_sweepEcho)
                    {
                        _space[slot] = x * dry + .42f * delayed;
                        var arrival = Smooth(progress / Math.Max(.1, _switchAt));
                        var ageSeconds = (Age + frame) / (double)Rate;
                        var fadeSeconds = _fadeFrames / (double)Rate;
                        var tail = 1 - Smooth((ageSeconds - fadeSeconds) / .72);
                        echo = .42f * (float)(arrival * tail) * delayed;
                    }
                    else
                    {
                        _space[slot] = x + .27f * delayed;
                        var arrival = Math.Clamp((progress - .3) / .7, 0, 1);
                        echo = .18f * (float)(arrival * arrival * (3 - 2 * arrival)) * delayed;
                    }
                }
                block[index] = (x * dry + echo) * Gain;
            }
            if (_roll && _rollFreezeAge < 0) _rollWrite = (_rollWrite + 1) % (_rollHistory.Length / channels);
        }
    }

    private static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
}

