using SynthwaveNowPlaying;
using System.Diagnostics;

// Generated tonal progressions are independent ground truth. No library songs
// or tags are used to set the expected answer; real recordings are reported separately.
var directory = Path.Combine(Path.GetTempPath(), "WavePlayerKeyChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
int passed = 0, total = 0;
var clock = Stopwatch.StartNew();
foreach (bool minor in new[] { false, true })
    foreach (int tonic in Enumerable.Range(0, 12))
        foreach (double cents in new[] { -27d, 0, 27d })
        {
            var file = Path.Combine(directory, "progression.wav");
            Write(file, tonic, minor, cents);
            var result = MusicalKeyAnalyzer.Analyze(file, CancellationToken.None);
            total++;
            if (result.Reliable && result.Tonic == tonic && result.Minor == minor) passed++;
            else Console.WriteLine($"FAIL expected tonic={tonic} minor={minor} cents={cents}: {result}");
        }
foreach (int mode in new[] { 1, 2, 3 })
{
    var file = Path.Combine(directory, "negative.wav");
    Write(file, 0, false, 0, mode);
    total++;
    if (!MusicalKeyAnalyzer.Analyze(file, CancellationToken.None).Reliable) passed++;
    else Console.WriteLine($"FAIL negative control {mode}");
}
var stereo = Path.Combine(directory, "stereo.wav");
Write(stereo, 9, true, 0, 4);
var stereoKey = MusicalKeyAnalyzer.Analyze(stereo, CancellationToken.None);
total++;
if (stereoKey.Reliable && stereoKey.Tonic == 9 && stereoKey.Minor) passed++;
else Console.WriteLine($"FAIL inverted stereo: {stereoKey}");
using (var cts = new CancellationTokenSource())
{
    cts.Cancel(); total++;
    try { MusicalKeyAnalyzer.Analyze(stereo, cts.Token); Console.WriteLine("FAIL cancellation"); }
    catch (OperationCanceledException) { passed++; }
}
Console.WriteLine($"{passed}/{total} checks passed in {clock.Elapsed.TotalSeconds:F2}s");
foreach (var file in args)
{
    clock.Restart();
    var result = MusicalKeyAnalyzer.Analyze(file, CancellationToken.None);
    Console.WriteLine($"{Path.GetFileName(file)}: {result.Display}; candidate={result.Name}; evidence={result.Confidence:F2}; {clock.Elapsed.TotalSeconds:F2}s");
}
// The directory is generated above, never a caller-supplied path.
if (!Path.GetFileName(directory).StartsWith("WavePlayerKeyChecks-", StringComparison.Ordinal) ||
    !string.Equals(Path.GetDirectoryName(Path.GetFullPath(directory))!.TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Unexpected fixture directory");
Directory.Delete(directory, recursive: true);
Environment.ExitCode = passed == total ? 0 : 1;

static void Write(string file, int tonic, bool minor, double cents, int mode = 0)
{
    const int rate = 22050, seconds = 16;
    int channels = mode == 4 ? 2 : 1;
    var rng = new Random(41);
    using var w = new BinaryWriter(File.Create(file));
    w.Write("RIFF"u8); w.Write(36 + rate * seconds * 2 * channels); w.Write("WAVEfmt "u8);
    w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
    w.Write(rate * 2 * channels); w.Write((short)(2 * channels)); w.Write((short)16);
    w.Write("data"u8); w.Write(rate * seconds * 2 * channels);
    int[][] chords = minor ? [[0, 3, 7], [5, 8, 12], [7, 11, 14], [0, 3, 7]]
        : [[0, 4, 7], [5, 9, 12], [7, 11, 14], [0, 4, 7]];
    for (int n = 0; n < rate * seconds; n++)
    {
        double time = (double)n / rate, sample = 0;
        int chord = (int)(time / 2) % 4;
        double envelope = Math.Min(1, time % 2 * 12) * Math.Min(1, (2 - time % 2) * 8);
        if (mode == 1) sample = (rng.NextDouble() * 2 - 1) * .16;
        else if (mode == 2) sample = .2 * Math.Sin(2 * Math.PI * 440 * time);
        else if (mode != 3)
        {
            foreach (int note in chords[chord])
            {
                double hz = 440 * Math.Pow(2, (48 + tonic + note - 69 + cents / 100) / 12d);
                for (int h = 1; h <= 4; h++) sample += .06 / h * Math.Sin(2 * Math.PI * hz * h * time + .3 * h) * envelope;
            }
            double bass = 440 * Math.Pow(2, (36 + tonic + chords[chord][0] - 69 + cents / 100) / 12d);
            sample += .1 * Math.Sin(2 * Math.PI * bass * time) * envelope;
            double beat = time % .5;
            sample += .1 * Math.Sin(2 * Math.PI * (55 * beat + 20 * (1 - Math.Exp(-beat * 20)))) * Math.Exp(-beat * 28);
        }
        short pcm = (short)(Math.Clamp(sample, -1, 1) * 32760);
        w.Write(pcm);
        if (channels == 2) w.Write((short)-pcm);
    }
}
