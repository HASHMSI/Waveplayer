namespace SynthwaveNowPlaying;

internal enum AutomixTiming { Adaptive, Classic, AdaptiveV2 }

internal enum EndingEffect { Clean, Filter, Echo, NaturalTail, SpaceEcho, DropCut, SweepEcho }
internal readonly record struct TransitionPlan(double StartSeconds, double NextSeekSeconds, double FadeSeconds, bool BeatAligned,
    EndingEffect Effect = EndingEffect.Clean, double EchoHit = 0, double EchoDelay = 0);

/// <summary>Fits the overlap inside both songs' playable music, then optionally snaps it to a beat.</summary>
internal static class TransitionPlanner
{
    public static TransitionPlan Plan(double durationSeconds, TransitionProfile current, TransitionProfile next,
        AutomixTiming timing = AutomixTiming.Adaptive)
    {
        if (timing == AutomixTiming.Classic) return PlanClassic(durationSeconds, current, next);
        if (timing == AutomixTiming.AdaptiveV2) return TransitionPlannerV2.Plan(durationSeconds, current, next);
        var activeEnd = Math.Max(0, durationSeconds - current.OutroSilence - .1);
        if (current.Features is { NaturalTail: true } tail)
            activeEnd = Math.Min(durationSeconds, Math.Max(activeEnd, tail.TailEnd));
        var target = current.OutroBlendSeconds > 0 ? current.OutroBlendSeconds : 3.5;
        // A forceful entrance exposes competing attacks during a long overlap.
        var attack = next.DurationSeconds > 0 ? next.IntroAttack : .5;
        target *= 1.12 - .28 * attack;
        var nextSeek = next.IntroSilence;
        var maxFade = Math.Min(12, Math.Max(0, activeEnd - current.IntroSilence) * .40);
        if (next.DurationSeconds > 0)
            maxFade = Math.Min(maxFade, Math.Max(0, next.DurationSeconds - next.OutroSilence - nextSeek - .1) * .4);
        var fade = Math.Min(target, maxFade);
        if (current.Features is { } a && next.Features is { } b && fade >= .3)
            return AdaptiveCues.Choose(current, next, a, b, activeEnd, fade, maxFade);
        var fallback = new TransitionPlan(activeEnd - fade, nextSeek, fade, false);
        if (!current.OutroBeat.IsReliable || !next.IntroBeat.IsReliable) return fallback;
        var tempoDifference = Math.Abs(current.OutroBeat.Bpm - next.IntroBeat.Bpm) / current.OutroBeat.Bpm;
        if (tempoDifference > .06) return fallback;

        var nextBeat = next.IntroBeat.FirstAtOrAfter(next.IntroSilence);
        var seek = Math.Max(nextSeek, nextBeat - .02);
        if (nextBeat < nextSeek || nextBeat > nextSeek + 1.3) return fallback;
        if (next.DurationSeconds > 0)
            maxFade = Math.Min(maxFade, (next.DurationSeconds - next.OutroSilence - seek - .1) * .4);
        var earliestBeat = current.OutroBeat.FirstAtOrAfter(activeEnd - maxFade);
        var start = Math.Max(earliestBeat, current.OutroBeat.Nearest(activeEnd - fade));
        var alignedFade = activeEnd - start;
        if (alignedFade < .6)
            return fallback;

        return new TransitionPlan(start, seek, alignedFade, true);
    }

    // Classic targets the original 4% of track length (3–5 s). Keep the
    // original beat snap tolerance and protect short clips in either deck.
    private static TransitionPlan PlanClassic(double duration, TransitionProfile current, TransitionProfile next)
    {
        var end = Math.Max(0, duration - current.OutroSilence);
        var seek = next.IntroSilence;
        var target = Math.Min(Math.Clamp(duration * .04, 3, 5), end);
        if (next.DurationSeconds > 0)
            target = Math.Min(target, Math.Max(0, next.DurationSeconds - next.OutroSilence - seek) * .65);
        var start = end - target;
        var fallback = new TransitionPlan(start, seek, Math.Max(0, target - .1), false);
        if (!current.OutroBeat.IsReliable || !next.IntroBeat.IsReliable ||
            Math.Abs(current.OutroBeat.Bpm - next.IntroBeat.Bpm) > current.OutroBeat.Bpm * .06)
            return fallback;

        var beatStart = current.OutroBeat.Nearest(start);
        var nextBeat = next.IntroBeat.FirstAtOrAfter(seek);
        var overlap = end - beatStart;
        if (beatStart < 0 || Math.Abs(beatStart - start) > .45 || overlap is < 2 or > 5.6 ||
            nextBeat < seek || nextBeat > seek + 1.3 ||
            (next.DurationSeconds > 0 && overlap > (next.DurationSeconds - next.OutroSilence - nextBeat) * .65))
            return fallback;
        return new TransitionPlan(beatStart, Math.Max(0, nextBeat - .02), overlap - .1, true);
    }
}
