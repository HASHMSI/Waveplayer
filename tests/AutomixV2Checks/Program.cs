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
Ensure(soft.Effect == EndingEffect.SweepEcho, "Unaligned gentle outro should use a subtle sweep");
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
var labOut = Synthetic(true) with
{
    OutroBeat = tempo,
    Features = Synthetic(true).Features! with
    {
        Outro = Synthetic(true).Features!.Outro with { BarAnchor = 0 }
    }
};
var labIn = next with
{
    IntroBeat = tempo,
    Features = next.Features! with { Intro = next.Features!.Intro with { BarAnchor = 0 } }
};
var labAligned = TransitionPlanner.Plan(180, labOut, labIn, AutomixTiming.AdaptiveV2);
var labMismatch = TransitionPlanner.Plan(180, labOut,
    labIn with { IntroBeat = new BeatGrid(150, 0, .8) }, AutomixTiming.AdaptiveV2);
var labUncertain = TransitionPlanner.Plan(180, Synthetic(true), next, AutomixTiming.AdaptiveV2);
Ensure(labAligned.BeatAligned && labAligned.FadeSeconds >= 1.4 &&
    labAligned.StartSeconds + labAligned.FadeSeconds <= 180,
    "Adaptive v2 failed to align a reliable same-tempo pair");
Ensure(!labMismatch.BeatAligned && labMismatch.FadeSeconds <= 4.01 &&
    !labUncertain.BeatAligned && labUncertain.FadeSeconds <= 4.51,
    "Adaptive v2 must choose a conservative fallback for uncertain grids");
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
var labRestored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(
    new AppSettings { AutomixTiming = AutomixTiming.AutoDjLab }));
Ensure(labRestored?.AutomixTiming == AutomixTiming.AutoDjLab, "Lab preference did not survive JSON");
Ensure(TransitionPlanner.Plan(180, labOut, labIn, AutomixTiming.AutoDjLab) == labAligned,
    "Old lab settings must use the merged Adaptive v2 planner");

// Render the new tail from two constant, stereo tracks. The stream must remain finite,
// stay below full scale, and end after the tail instead of cutting mid-delay.
var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
foreach (var effect in Enum.GetValues<EndingEffect>())
{
    // Production arms 1.25 s early. Before the cue, the old stream must remain
    // unchanged and the incoming decoder must not consume its first sample.
    var beforeBass = new BassSwapSampleProvider(new ConstantSource(format));
    var afterBass = new BassSwapSampleProvider(new ConstantSource(format));
    var before = new AdaptiveDeck(beforeBass, beforeBass, 0);
    var after = new AdaptiveDeck(afterBass, afterBass, 0);
    const int wait = 55125;
    var scheduled = new TransitionPlan(0, 0, 2, false, effect, EchoHit: .5, EchoDelay: .24);
    before.Arm(scheduled, false, wait, .5, .8f, true);
    after.Arm(scheduled, true, wait, .5, .8f, true);
    var beforePacket = new float[2000];
    var afterPacket = new float[2000];
    int advanced = 0;
    while (advanced < wait)
    {
        int count = Math.Min(beforePacket.Length, (wait - advanced) * 2);
        Ensure(before.Read(beforePacket.AsSpan(0, count)) == count &&
            after.Read(afterPacket.AsSpan(0, count)) == count, "Prewarm stream stopped");
        for (int i = 0; i < count; i++)
            Ensure(Math.Abs(beforePacket[i] - .24f) < .00001 && afterPacket[i] == 0,
                "Prewarm changed the old track or leaked the new track: " + effect);
        advanced += count / 2;
    }
    Ensure(before.Age == 0 && after.Age == 0 && after.Frame == 0,
        "Prewarm consumed incoming audio or drifted off the scheduled cue");
    Ensure(before.Read(beforePacket) == beforePacket.Length && after.Read(afterPacket) == afterPacket.Length,
        "Scheduled cue stopped playback: " + effect);
    foreach (var sample in beforePacket.Concat(afterPacket)) Ensure(float.IsFinite(sample), "Cue output invalid");
}
Console.WriteLine("PASS: 1.25s decoder prewarm and scheduled cue for every ending effect");
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
// Compare the same tones through the plain fade and the new production effect.
// A non-trivial RMS difference proves the effect does more than change metadata.
float[] RenderEffect(EndingEffect effect)
{
    var split = new BassSwapSampleProvider(new ToneSource(format));
    var toneDeck = new AdaptiveDeck(split, split, 0);
    toneDeck.Arm(new TransitionPlan(0, 0, 4, false, effect, EchoDelay: .24), false, 0, .5, 1, true);
    var samplesOut = new List<float>();
    var packet = new float[1024];
    while (samplesOut.Count < 44100 * 2 * 5)
    {
        var read = toneDeck.Read(packet);
        if (read == 0) break;
        for (int i = 0; i < read; i++) { Ensure(float.IsFinite(packet[i]), "Sweep sample invalid"); samplesOut.Add(packet[i]); }
    }
    return samplesOut.ToArray();
}
var cleanPcm = RenderEffect(EndingEffect.Clean);
var sweepPcm = RenderEffect(EndingEffect.SweepEcho);
var rollPcm = RenderEffect(EndingEffect.NeonRoll);
double difference = Math.Sqrt(Enumerable.Range(0, cleanPcm.Length)
    .Average(i => Math.Pow(cleanPcm[i] - sweepPcm[i], 2)));
Ensure(difference > .035 && sweepPcm.Max(x => Math.Abs(x)) < 1 &&
    sweepPcm.TakeLast(200).Max(x => Math.Abs(x)) < .0001,
    "Sweep isn't distinct, exceeded full scale, or cut off its tail");
Console.WriteLine($"Sweep vs plain fade RMS difference: {difference:0.000}");
double rollDifference = Math.Sqrt(Enumerable.Range(0, cleanPcm.Length)
    .Average(i => Math.Pow(cleanPcm[i] - rollPcm[i], 2)));
Ensure(rollDifference > .035 && rollPcm.Max(x => Math.Abs(x)) < 1 &&
    rollPcm.TakeLast(200).Max(x => Math.Abs(x)) < .0001, "Roll output or decay invalid");
Console.WriteLine($"Roll vs plain fade RMS difference: {rollDifference:0.000}");
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
// Every ordered pair exercises a different outro/intro combination. Keep the
// planner within both playable regions even when one beat map is missing.
for (int i = 0; i < profiles.Length; i++)
    for (int j = 0; j < profiles.Length; j++)
    {
        if (i == j) continue;
        var outgoing = profiles[i].Profile;
        var incoming = profiles[j].Profile;
        var candidate = TransitionPlanner.Plan(outgoing.DurationSeconds, outgoing, incoming, AutomixTiming.AdaptiveV2);
        Ensure(double.IsFinite(candidate.StartSeconds) && double.IsFinite(candidate.NextSeekSeconds) &&
            double.IsFinite(candidate.FadeSeconds) && candidate.StartSeconds >= 0 &&
            candidate.FadeSeconds >= 0 && candidate.StartSeconds + candidate.FadeSeconds <= outgoing.DurationSeconds + .01 &&
            candidate.NextSeekSeconds >= 0 && candidate.NextSeekSeconds + candidate.FadeSeconds < incoming.DurationSeconds,
            $"Invalid Adaptive v2 plan for pair {i + 1} -> {j + 1}");
        Ensure(candidate.Effect != EndingEffect.NeonRoll || candidate.BeatAligned,
            $"Beat roll selected without alignment for pair {i + 1} -> {j + 1}");
    }
Console.WriteLine($"PASS: {profiles.Length * (profiles.Length - 1)} ordered real-track plans");
for (int i = 0; i + 1 < profiles.Length; i++)
{
    var current = profiles[i].Profile;
    var incoming = profiles[i + 1].Profile;
    var plan = TransitionPlanner.Plan(current.DurationSeconds, current, incoming, AutomixTiming.AdaptiveV2);
    Ensure(double.IsFinite(plan.FadeSeconds) && plan.FadeSeconds >= 0 && plan.FadeSeconds <= 11.5 &&
        plan.StartSeconds >= 0 && plan.StartSeconds + plan.FadeSeconds <= current.DurationSeconds + .01 &&
        plan.NextSeekSeconds >= 0 && plan.NextSeekSeconds < incoming.DurationSeconds,
        "Invalid merged Adaptive v2 real-track plan");
    Console.WriteLine($"Merged mix {i + 1}: {plan.FadeSeconds:0.00}s, aligned {plan.BeatAligned}, effect {plan.Effect}");
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
if (Environment.GetEnvironmentVariable("WavePlayer_FIND_DROP") == "1")
{
    int found = 0;
    for (int i = 0; i < profiles.Length && found < 5; i++)
        for (int j = 0; j < profiles.Length; j++)
        {
            if (i == j) continue;
            var plan = TransitionPlanner.Plan(profiles[i].Profile.DurationSeconds,
                profiles[i].Profile, profiles[j].Profile, AutomixTiming.AdaptiveV2);
            if (plan.Effect != EndingEffect.DropCut) continue;
            Console.WriteLine($"Drop pair: {profiles[i].File} => {profiles[j].File} ({plan.FadeSeconds:0.00}s)");
            found++;
            if (found >= 5) break;
        }
    Console.WriteLine($"Drop pairs found: {found}");
}
if (profiles.Length >= 2)
{
    var oldProfile = profiles[0].Profile;
    var newProfile = profiles[1].Profile;
    var renderTiming = AutomixTiming.AdaptiveV2;
    var plan = TransitionPlanner.Plan(oldProfile.DurationSeconds, oldProfile, newProfile, renderTiming);
    const double prewarmSeconds = 1.25;
    var scheduledStart = Math.Max(0, plan.StartSeconds - prewarmSeconds);
    var waitFrames = (long)Math.Round((plan.StartSeconds - scheduledStart) * 44100);
    using var oldReader = new AudioFileReader(profiles[0].File) { CurrentTime = TimeSpan.FromSeconds(scheduledStart) };
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
    var oldDeck = new AdaptiveDeck(oldInput, oldBass, scheduledStart);
    var newDeck = new AdaptiveDeck(newInput, newBass, plan.NextSeekSeconds);
    newDeck.Prefetch();
    oldDeck.Arm(plan, false, waitFrames, .5, 1, true);
    newDeck.Arm(plan, true, waitFrames, .5, .9f, true);
    var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
    mixer.AddMixerInput(oldDeck);
    mixer.AddMixerInput(newDeck);
    var limited = new PeakLimiterSampleProvider(mixer);
    long total = 0;
    double peakMix = 0, maxJump = 0;
    var previous = new double[2];
    var output = new float[4096];
    var previewPath = Environment.GetEnvironmentVariable("WavePlayer_AUTOMIX_PREVIEW");
    using var previewWriter = string.IsNullOrWhiteSpace(previewPath) ? null
        : new WaveFileWriter(previewPath, WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
    var target = (long)((plan.FadeSeconds + prewarmSeconds + .8) * 44100 * 2);
    while (total < target)
    {
        var read = limited.Read(output);
        Ensure(read > 0, "Real mix ended unexpectedly");
        previewWriter?.WriteSamples(output, 0, read);
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
    Console.WriteLine($"Rendered {renderTiming} real pair: peak {peakMix:0.000}, max sample jump {maxJump:0.000}");
    Ensure(peakMix <= 1.001 && maxJump < 1.5, "Real mix clipped or developed a discontinuity");
}
Console.WriteLine("PASS: Adaptive v2 synthetic and real-track checks");

sealed class ConstantSource(WaveFormat format) : ISampleProvider
{
    public WaveFormat WaveFormat => format;
    public int Read(Span<float> buffer) { buffer.Fill(.3f); return buffer.Length; }
}

sealed class ToneSource(WaveFormat format) : ISampleProvider
{
    long frame;
    public WaveFormat WaveFormat => format;
    public int Read(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i += 2, frame++)
        {
            var t = frame / (double)format.SampleRate;
            var value = (float)(.2 * Math.Sin(2 * Math.PI * 100 * t) + .2 * Math.Sin(2 * Math.PI * 1500 * t));
            buffer[i] = buffer[i + 1] = value;
        }
        return buffer.Length;
    }
}
