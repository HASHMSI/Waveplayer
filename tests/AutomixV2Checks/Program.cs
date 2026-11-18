using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SynthwaveNowPlaying;

static void Ensure(bool valid, string message) { if (!valid) throw new Exception(message); }

static TransitionProfile Synthetic(bool gentle)
{
    var tailBins = Enumerable.Range(0, 80).Select(i => new CueBin(
        gentle ? .24f : .75f, 0, gentle ? .08f : .7f, gentle ? .5f : .08f)).ToArray();
    var introBins = Enumerable.Range(0, 80).Select(_ => new CueBin(.48f, 0, .13f, .2f)).ToArray();
    var section = new CueSection(150, tailBins, null);
    var intro = new CueSection(0, introBins, null);
    var features = new TransitionFeatures(intro, section, false, 170, -1);
    return new TransitionProfile(0, 10, .2, .15, default, default, 180,
        gentle ? 7 : 2.1, .2, null, features);
}
var next = Synthetic(true) with
{
    IntroSilence = .4, OutroSilence = 0, DurationSeconds = 190,
    Features = new TransitionFeatures(new CueSection(0,
        Enumerable.Repeat(new CueBin(.5f, 0, .14f, .2f), 80).ToArray(), null),
        new CueSection(170, Enumerable.Repeat(new CueBin(.2f, 0, .1f, .3f), 80).ToArray(), null), false, 190, -1)
};
var soft = TransitionPlanner.Plan(180, Synthetic(true), next, AutomixTiming.AdaptiveV2);
var hard = TransitionPlanner.Plan(180, Synthetic(false), next, AutomixTiming.AdaptiveV2);
Ensure(soft.FadeSeconds > hard.FadeSeconds, "Gentle outro should allow a longer mix");
Ensure(soft.Effect == EndingEffect.SpaceEcho, "Sustained outro should choose the new space echo");
Ensure(hard.Effect == EndingEffect.Clean, "Percussive outro should remain clean");
Ensure(soft.StartSeconds >= 0 && soft.StartSeconds + soft.FadeSeconds <= 180, "Mix exceeds outgoing track");
var classic = TransitionPlanner.Plan(180, Synthetic(true), next, AutomixTiming.Classic);
var original = TransitionPlanner.Plan(180, Synthetic(true), next, AutomixTiming.Adaptive);
Ensure(classic.Effect == EndingEffect.Clean && original.Effect != EndingEffect.SpaceEcho,
    "Older modes must not acquire the new effect");
var tempo = new BeatGrid(120, 0, .8);
var onBeat = TransitionPlanner.Plan(180, Synthetic(true) with { OutroBeat = tempo },
    next with { IntroBeat = tempo }, AutomixTiming.AdaptiveV2);
var offBeat = TransitionPlanner.Plan(180, Synthetic(true) with { OutroBeat = tempo },
    next with { IntroBeat = new BeatGrid(155, 0, .8) }, AutomixTiming.AdaptiveV2);
Ensure(onBeat.BeatAligned && !offBeat.BeatAligned, "Beat compatibility gate failed");
var loudIntro = next with
{
    IntroAttack = .8,
    Features = next.Features! with
    {
        Intro = new CueSection(0,
            Enumerable.Repeat(new CueBin(.72f, 0, .7f, .08f), 80).ToArray(), null)
    }
};
var drop = TransitionPlanner.Plan(180, Synthetic(false) with
    { OutroBeat = tempo, OutroBlendSeconds = 4 },
    loudIntro with { IntroBeat = tempo }, AutomixTiming.AdaptiveV2);
var noDrop = TransitionPlanner.Plan(180, Synthetic(false) with
    { OutroBeat = tempo, OutroBlendSeconds = 4 },
    loudIntro with { IntroBeat = new BeatGrid(155, 0, .8) }, AutomixTiming.AdaptiveV2);
Ensure(drop.Effect == EndingEffect.DropCut && noDrop.Effect != EndingEffect.DropCut,
    "Drop must require compatible reliable beats");
var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(
    new AppSettings { AutomixTiming = AutomixTiming.AdaptiveV2 }));
Ensure(restored?.AutomixTiming == AutomixTiming.AdaptiveV2, "V2 preference did not survive JSON");

// Render the new tail from two constant, stereo tracks. The stream must remain finite,
// stay below full scale, and end after the tail instead of cutting mid-delay.
var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
var source = new ConstantSource(format);
var bass = new BassSwapSampleProvider(source);
var deck = new AdaptiveDeck(bass, bass, 0);
deck.Arm(new TransitionPlan(0, 0, 3.2, false, EndingEffect.SpaceEcho), false, 0, .5, .8f, true);
var buffer = new float[4096];
double peak = 0; long samples = 0;
while (true)
{
    var read = deck.Read(buffer);
    if (read == 0) break;
    samples += read;
    for (int i = 0; i < read; i++)
    {
        Ensure(float.IsFinite(buffer[i]), "Non-finite output sample");
        peak = Math.Max(peak, Math.Abs(buffer[i]));
    }
    Ensure(samples < 44100L * 2 * 6, "Tail never stopped");
}
Ensure(peak < 1 && samples > 44100L * 2 * 3.2, "Tail level or length invalid");
var firstBass = new BassSwapSampleProvider(new ConstantSource(format));
var secondBass = new BassSwapSampleProvider(new ConstantSource(format)) { TargetBassGain = 0 };
var firstDeck = new AdaptiveDeck(firstBass, firstBass, 0);
var secondDeck = new AdaptiveDeck(secondBass, secondBass, 0);
var dropPlan = new TransitionPlan(0, 0, 3, true, EndingEffect.DropCut);
firstDeck.Arm(dropPlan, false, 0, .5, 1, true);
secondDeck.Arm(dropPlan, true, 0, .5, 1, true);
var dropMixer = new MixingSampleProvider(format);
dropMixer.AddMixerInput(firstDeck);
dropMixer.AddMixerInput(secondDeck);
var dropOutput = new PeakLimiterSampleProvider(dropMixer);
var window = new float[8820]; // 100 ms stereo
var dropLevels = new double[31];
double dropJump = 0, prior = 0;
for (int windowIndex = 0; windowIndex < dropLevels.Length; windowIndex++)
{
    var read = dropOutput.Read(window);
    Ensure(read == window.Length, "Drop render stopped unexpectedly");
    for (int j = 0; j < read; j += 2)
    {
        Ensure(float.IsFinite(window[j]), "Drop produced a non-finite sample");
        dropLevels[windowIndex] += Math.Abs(window[j]);
        if (windowIndex > 0 || j > 0) dropJump = Math.Max(dropJump, Math.Abs(window[j] - prior));
        prior = window[j];
    }
    dropLevels[windowIndex] /= read / 2;
}
Console.WriteLine($"Drop levels: before {dropLevels[12]:0.000}, cue {dropLevels[14]:0.000}, after {dropLevels[18]:0.000}, jump {dropJump:0.000}");
Ensure(dropLevels[14] < dropLevels[12] * .8 && dropLevels[18] > dropLevels[14] * 1.5 &&
    dropJump < .2, "Beat drop failed to duck and recover smoothly");
Console.WriteLine($"Synthetic plans: gentle {soft.FadeSeconds:0.00}s/{soft.Effect}, strong {hard.FadeSeconds:0.00}s/{hard.Effect}, beat drop {drop.FadeSeconds:0.00}s/{drop.Effect}; tail peak {peak:0.000}");

var cached = args.Length > 0 && args[0] == "--cached";
var files = (cached ? args.Skip(1) : args.AsEnumerable()).Where(File.Exists).ToArray();
var cache = cached ? new TrackAnalysisCache() : null;
var profiles = files.Select(file => (File: file, Profile: cached
    ? cache!.AnalyzeQuick(file).Profile : TransitionAnalyzer.Analyze(file))).ToArray();
foreach (var (file, profile) in profiles)
{
    Ensure(profile.DurationSeconds > 1, "Real track analysis failed: " + file);
    Console.WriteLine($"Analyzed {Path.GetFileName(file)}: {profile.DurationSeconds:0}s, outro silence {profile.OutroSilence:0.0}s, beats {profile.IntroBeat.Confidence:0.00}/{profile.OutroBeat.Confidence:0.00}");
}
for (int i = 0; i + 1 < profiles.Length; i++)
{
    var current = profiles[i].Profile; var incoming = profiles[i + 1].Profile;
    var plan = TransitionPlanner.Plan(current.DurationSeconds, current, incoming, AutomixTiming.AdaptiveV2);
    Ensure(double.IsFinite(plan.FadeSeconds) && plan.FadeSeconds is >= 0 and <= 12 &&
        plan.StartSeconds >= 0 && plan.StartSeconds + plan.FadeSeconds <= current.DurationSeconds + .01 &&
        plan.NextSeekSeconds >= 0 && plan.NextSeekSeconds < incoming.DurationSeconds,
        "Invalid real track plan");
    Console.WriteLine($"Mix {i + 1}: start {plan.StartSeconds:0.00}s, length {plan.FadeSeconds:0.00}s, effect {plan.Effect}, beat {plan.BeatAligned}");
}
if (profiles.Length >= 2)
{
    var oldProfile = profiles[0].Profile;
    var newProfile = profiles[1].Profile;
    var plan = TransitionPlanner.Plan(oldProfile.DurationSeconds, oldProfile, newProfile, AutomixTiming.AdaptiveV2);
    using var oldReader = new AudioFileReader(profiles[0].File) { CurrentTime = TimeSpan.FromSeconds(plan.StartSeconds) };
    using var newReader = new AudioFileReader(profiles[1].File) { CurrentTime = TimeSpan.FromSeconds(plan.NextSeekSeconds) };
    ISampleProvider Prepared(AudioFileReader reader, out BassSwapSampleProvider split)
    {
        split = new BassSwapSampleProvider(new LocalEqualizer(reader));
        ISampleProvider input = split;
        if (input.WaveFormat.Channels == 1) input = new MonoToStereoSampleProvider(input);
        if (input.WaveFormat.SampleRate != 44100) input = new WdlResamplingSampleProvider(input, 44100);
        return input;
    }
    var oldInput = Prepared(oldReader, out var oldBass);
    var newInput = Prepared(newReader, out var newBass);
    newBass.TargetBassGain = 0;
    var oldDeck = new AdaptiveDeck(oldInput, oldBass, plan.StartSeconds);
    var newDeck = new AdaptiveDeck(newInput, newBass, plan.NextSeekSeconds);
    newDeck.Prefetch();
    oldDeck.Arm(plan, false, 0, .5, 1, true);
    newDeck.Arm(plan, true, 0, .5, .9f, true);
    var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
    mixer.AddMixerInput(oldDeck);
    mixer.AddMixerInput(newDeck);
    var limited = new PeakLimiterSampleProvider(mixer);
    long total = 0;
    double peakMix = 0, maxJump = 0;
    var previous = new double[2];
    var output = new float[4096];
    var target = (long)((plan.FadeSeconds + .8) * 44100 * 2);
    while (total < target)
    {
        var read = limited.Read(output);
        Ensure(read > 0, "Real mix ended unexpectedly");
        total += read;
        for (int j = 0; j < read; j++)
        {
            Ensure(float.IsFinite(output[j]), "Real mix produced non-finite samples");
            peakMix = Math.Max(peakMix, Math.Abs(output[j]));
            var channel = j % 2;
            maxJump = Math.Max(maxJump, Math.Abs(output[j] - previous[channel]));
            previous[channel] = output[j];
        }
    }
    Console.WriteLine($"Rendered real pair: peak {peakMix:0.000}, max sample jump {maxJump:0.000}");
    Ensure(peakMix <= 1.001 && maxJump < 1.5, "Real mix clipped or developed a discontinuity");
}
Console.WriteLine("PASS: Adaptive v2 synthetic and real-track checks");

sealed class ConstantSource(WaveFormat format) : ISampleProvider
{
    public WaveFormat WaveFormat => format;
    public int Read(Span<float> buffer) { buffer.Fill(.3f); return buffer.Length; }
}
