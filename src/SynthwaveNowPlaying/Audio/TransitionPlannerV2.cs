namespace SynthwaveNowPlaying;

/// <summary>Searches several safe overlap lengths and ranks audible collisions.</summary>
internal static class TransitionPlannerV2
{
    internal static TransitionPlan Plan(double duration, TransitionProfile current, TransitionProfile next)
    {
        var legacy = TransitionPlanner.Plan(duration, current, next, AutomixTiming.Adaptive);
        if (current.Features is not { } a || next.Features is not { } b) return legacy;

        var end = Math.Clamp(duration - current.OutroSilence - .1, 0, duration);
        if (a.NaturalTail) end = Math.Min(duration, Math.Max(end, a.TailEnd));
        var intro = Math.Max(0, next.IntroSilence);
        var availableNext = next.DurationSeconds > 0
            ? Math.Max(0, next.DurationSeconds - next.OutroSilence - intro - .1) : 14;
        var limit = Math.Min(12, Math.Min(Math.Max(0, end - current.IntroSilence) * .45, availableNext * .45));
        if (limit < .8) return legacy;

        var strongEnding = Average(a.Outro, Math.Max(0, end - 2), end, bin => bin.Attack);
        var quietEnding = Average(a.Outro, Math.Max(0, end - 2), end, bin => bin.Level);
        var entrance = Average(b.Intro, intro, intro + 2, bin => bin.Attack);
        var preferred = current.OutroBlendSeconds > 0 ? current.OutroBlendSeconds : 3.6;
        if (quietEnding < .25 && strongEnding < .32) preferred += 1.8;
        if (strongEnding > .55 || entrance > .58) preferred *= .7;
        preferred = Math.Clamp(preferred, .8, limit);

        var oldBeat = current.OutroBeat;
        var newBeat = next.IntroBeat;
        var equivalent = newBeat.Bpm;
        if (newBeat.Bpm > oldBeat.Bpm * 1.5) equivalent *= .5;
        else if (oldBeat.Bpm > newBeat.Bpm * 1.5) equivalent *= 2;
        bool compatible = oldBeat.IsReliable && newBeat.IsReliable &&
            Math.Abs(oldBeat.Bpm - equivalent) / oldBeat.Bpm <= .045;
        if (!compatible && strongEnding > .4 && entrance > .4) limit = Math.Min(limit, 4);
        preferred = Math.Min(preferred, limit);

        var best = legacy;
        double bestScore = double.PositiveInfinity;
        void Consider(double start, double seek, bool aligned)
        {
            var length = end - start;
            if (length < .45 || length > limit + .001 || start < current.IntroSilence ||
                seek < intro || seek > intro + 1.4 ||
                (next.DurationSeconds > 0 && length > (next.DurationSeconds - next.OutroSilence - seek - .1) * .45)) return;
            double voices = 0, attacks = 0, level = 0, weight = 0;
            for (double t = 0; t < length; t += CueSection.Step)
            {
                var old = a.Outro.At(start + t);
                var fresh = b.Intro.At(seek + t);
                var overlap = Math.Sin(Math.PI * (t + .125) / length);
                voices += overlap * Math.Min(old.Voice, fresh.Voice);
                attacks += overlap * old.Attack * fresh.Attack;
                level += overlap * Math.Min(old.Level, fresh.Level);
                weight += overlap;
            }
            var score = .48 * Math.Abs(length - preferred) / Math.Max(1, preferred)
                + 1.8 * voices / Math.Max(1, weight)
                + .32 * attacks / Math.Max(1, weight)
                + .18 * level / Math.Max(1, weight)
                + .15 * (seek - intro);
            if (aligned) score -= .15;
            if (score >= bestScore) return;
            bestScore = score;
            best = new(start, seek, length, aligned);
        }

        foreach (var factor in new[] { .6, .8, 1d, 1.2, 1.45 })
        {
            var start = end - Math.Min(limit, preferred * factor);
            Consider(start, intro, false);
            if (!compatible) continue;
            var beatStart = oldBeat.Nearest(start);
            var beatSeek = newBeat.FirstAtOrAfter(intro);
            for (var i = -1; i <= 1; i++)
                for (var j = 0; j <= 1; j++)
                    Consider(beatStart + i * oldBeat.PeriodSeconds,
                        beatSeek + j * newBeat.PeriodSeconds, true);
        }
        if (!double.IsFinite(bestScore)) return legacy;
        // V2 uses a deliberate filter/echo handoff on normal-length transitions,
        // even when a trustworthy beat grid is unavailable. Very short clips
        // keep the plain fade; no beat sync is claimed for an unaligned plan.
        if (best.FadeSeconds >= 1.5)
            best = best with { Effect = best.FadeSeconds >= 2 ? EndingEffect.NeonRoll : EndingEffect.SweepEcho,
                EchoDelay = oldBeat.IsReliable ? Math.Clamp(oldBeat.PeriodSeconds / 2, .14, .38) : .24 };

        var oldVoice = Average(a.Outro, Math.Max(best.StartSeconds, end - 2), end, bin => bin.Voice);
        var newVoice = Average(b.Intro, best.NextSeekSeconds,
            best.NextSeekSeconds + Math.Min(2, best.FadeSeconds), bin => bin.Voice);
        if (oldVoice > .35 || newVoice > .4) return best;
        // Reserve the dramatic cut for a real shared beat with distinct attacks.
        if (best.BeatAligned && best.FadeSeconds >= 1.2 &&
            strongEnding > .45 && entrance > .45 && oldVoice < .25 && newVoice < .25)
            return best with { Effect = EndingEffect.DropCut };
        if (best.Effect is EndingEffect.SweepEcho or EndingEffect.NeonRoll) return best;
        if (oldBeat.IsReliable && a.LastHit >= best.StartSeconds &&
            a.LastHit > end - oldBeat.PeriodSeconds * 1.4 &&
            Math.Abs(a.LastHit - oldBeat.Nearest(a.LastHit)) < .09 && best.FadeSeconds >= 1)
            return best with { Effect = EndingEffect.Echo, EchoHit = a.LastHit,
                EchoDelay = oldBeat.PeriodSeconds / 2 };
        var tone = Average(a.Outro, Math.Max(best.StartSeconds, end - 2), end, bin => bin.Tone);
        if (best.FadeSeconds >= 3 && tone > .17 && strongEnding < .4)
            return best with { Effect = EndingEffect.SpaceEcho };
        if (best.FadeSeconds >= 2.5 && tone > .12 && strongEnding < .46)
            return best with { Effect = EndingEffect.Filter };
        return best;
    }

    private static double Average(CueSection section, double from, double to, Func<CueBin, float> value)
    {
        double sum = 0;
        int count = 0;
        for (var t = from; t < to; t += CueSection.Step) { sum += value(section.At(t)); count++; }
        return count == 0 ? 0 : sum / count;
    }
}
