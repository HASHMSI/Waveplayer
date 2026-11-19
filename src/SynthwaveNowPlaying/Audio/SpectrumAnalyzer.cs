using System.Numerics;

namespace SynthwaveNowPlaying;

internal sealed class SpectrumAnalyzer
{
    // Reuse FFT arrays and twiddle factors; only the capture worker calls Compute().
    // 4096 samples resolve bass far better than 1024 at the player's 44.1 kHz
    // output rate (about 10.8 Hz per bin instead of 43 Hz).
    private const int Size = 4096;
    private const int Bands = 56;
    private readonly AudioSampleBuffer _samples;
    private readonly Complex[] _fft = new Complex[Size];
    private readonly float[] _input = new float[Size];
    private readonly float[] _result = new float[Bands];
    private readonly double[] _window = new double[Size];
    private readonly int[] _bitReversed = new int[Size];
    private readonly Complex[][] _twiddles;
    private readonly int[] _bandStarts = new int[Bands];
    private readonly int[] _bandEnds = new int[Bands];

    public SpectrumAnalyzer(AudioSampleBuffer samples)
    {
        _samples = samples;
        var bits = (int)Math.Log2(Size);
        for (var i = 0; i < Size; i++)
        {
            _window[i] = .5 - .5 * Math.Cos(2 * Math.PI * i / (Size - 1));
            _bitReversed[i] = ReverseBits(i, bits);
        }

        _twiddles = new Complex[bits][];
        for (int stage = 0, length = 2; length <= Size; stage++, length <<= 1)
        {
            var half = length >> 1;
            var values = _twiddles[stage] = new Complex[half];
            for (var i = 0; i < half; i++)
            {
                var angle = -2 * Math.PI * i / length;
                values[i] = new Complex(Math.Cos(angle), Math.Sin(angle));
            }
        }

        for (var band = 0; band < Bands; band++)
        {
            // Log-spaced bins give bass and treble similar visual detail.
            var start = Math.Max(1, (int)Math.Pow(Size / 2d, band / (double)Bands));
            var end = Math.Max(start + 1, (int)Math.Pow(Size / 2d, (band + 1) / (double)Bands));
            _bandStarts[band] = start;
            _bandEnds[band] = Math.Min(end, Size / 2);
        }
    }

    public float[] Compute()
    {
        _samples.CopyLatest(_input);
        for (var i = 0; i < Size; i++)
            _fft[i] = new Complex(_input[_bitReversed[i]] * _window[_bitReversed[i]], 0);

        var stage = 0;
        for (var length = 2; length <= Size; length <<= 1, stage++)
        {
            var twiddles = _twiddles[stage];
            var half = length >> 1;
            for (var start = 0; start < Size; start += length)
            {
                for (var offset = 0; offset < half; offset++)
                {
                    var index = start + offset;
                    var even = _fft[index];
                    var odd = twiddles[offset] * _fft[index + half];
                    _fft[index] = even + odd;
                    _fft[index + half] = even - odd;
                }
            }
        }

        for (var band = 0; band < Bands; band++)
        {
            var start = _bandStarts[band];
            var end = _bandEnds[band];
            var energy = 0d;
            for (var bin = start; bin < end; bin++)
            {
                var value = _fft[bin];
                energy += value.Real * value.Real + value.Imaginary * value.Imaginary;
            }
            var average = Math.Sqrt(energy / Math.Max(1, end - start)) / (Size / 2d);
            // Square-root compression keeps quieter upper bands visible without
            // letting strong bass pin every neighboring bar at full height.
            _result[band] = (float)Math.Clamp(Math.Pow(average * 8, .58), 0, 1);
        }
        return _result;
    }

    private static int ReverseBits(int value, int bits)
    {
        var reversed = 0;
        for (var i = 0; i < bits; i++)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>= 1;
        }
        return reversed;
    }
}
