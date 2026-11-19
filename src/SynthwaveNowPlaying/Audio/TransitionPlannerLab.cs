namespace SynthwaveNowPlaying;

/// <summary>
/// Experimental, independent WPF/NAudio adaptation of confidence-tiered,
/// bar-aware transitions. No code or runtime dependency from the AGPL web
/// reference is included in WavePlayer.
/// </summary>
internal static class TransitionPlannerLab
{
    public static TransitionPlan Refine(double duration, TransitionProfile current, TransitionProfile next,
        TransitionPlan baseline)
    {
        var end = Math.Clamp(duration - current.OutroSilence - .1, 0, duration);
        if (current.Features is { NaturalTail: true } tail)
            end = Math.Min(duration, Math.Max(end, tail.TailEnd));
        var intro = Math.Max(0, next.IntroSilence);
        var old = current.OutroBeat;
        var fresh = next.IntroBeat;
        var confidence = Math.Min(old.Confidence, fresh.Confidence);

        // Poor grids should never turn two uncertain kick patterns into a long,
        // precisely wrong beat lock. Keep an audible but conservative fallback.
        if (!old.IsReliable || !fresh.IsReliable || confidence < .48 ||
            current.Features is not { } a || next.Features is not { } b)
            return SafeFallback(baseline, end, intro, next, 4.5);

        var equivalent = fresh.Bpm;
        if (equivalent > old.Bpm * 1.5) equivalent *= .5;
        else if (old.Bpm > equivalent * 1.5) equivalent *= 2;
        var mismatch = Math.Abs(old.Bpm - equivalent) / old.Bpm;
        // This prototype has no pitch-preserving time stretch. Refuse a bar
        // lock if phase would audibly drift during the overlap.
        if (mismatch > .012) return SafeFallback(baseline, end, intro, next, 4);

        double limit = Math.Min(11.5, Math.Min(end - current.IntroSilence,
            next.DurationSeconds > 0 ? next.DurationSeconds - next.OutroSilence - intro - .2 : 11.5));
        if (limit < 1) return SafeFallback(baseline, end, intro, next, 2);

        var oldAnchor = a.Outro.BarAnchor ?? old.AnchorSeconds;
        var nextAnchor = b.Intro.BarAnchor ?? fresh.AnchorSeconds;
        var oldBar = 4 * old.PeriodSeconds;
        var nextBar = 4 * fresh.PeriodSeconds;
        var seek = FirstOnGrid(nextAnchor, nextBar, intro);
        if (seek > intro + nextBar + .01 ||
            (next.DurationSeconds > 0 && seek + 1.5 >= next.DurationSeconds - next.OutroSilence))
            return SafeFallback(baseline, end, intro, next, 4);

        TransitionPlan? best = null;
        var bestScore = double.PositiveInfinity;
        // 1, 2 or 4 bars are useful at widget scale; longer DJ blends require
        // precise phrase maps and time stretching, which this lab does not have.
        foreach (var bars in new[] { 1, 2, 4 })
        {
            if (confidence < .58 && bars > 1) continue;
            if (confidence < .72 && bars > 2) continue;
            var target = bars * oldBar;
            if (target > limit) continue;
            var start = LastOnGrid(oldAnchor, oldBar, end - target);
            var length = end - start;
            if (start < current.IntroSilence || length > limit || length < 1.4) continue;

            double conflict = 0, attacks = 0, count = 0;
            for (var t = 0d; t < length; t += CueSection.Step)
            {
                var outBin = a.Outro.At(start + t);
                var inBin = b.Intro.At(seek + t);
                conflict += Math.Min(outBin.Voice, inBin.Voice);
                attacks += outBin.Attack * inBin.Attack;
                count++;
            }
            var desired = Math.Clamp(current.OutroBlendSeconds, oldBar, limit);
            var score = Math.Abs(length - desired) / Math.Max(1, desired)
                + 1.5 * conflict / Math.Max(1, count)
                + .35 * attacks / Math.Max(1, count)
                + .2 * mismatch * length / old.PeriodSeconds;
            if (score >= bestScore) continue;
            bestScore = score;
            // The V2 planner chooses an effect after timing is settled.
            best = new TransitionPlan(start, seek, length, true);
        }
        return best ?? SafeFallback(baseline, end, intro, next, 4);
    }

    private static TransitionPlan SafeFallback(TransitionPlan baseline, double end, double intro,
        TransitionProfile next, double maxLength)
    {
        var available = Math.Max(0, end - .1);
        if (next.DurationSeconds > 0)
            available = Math.Min(available, Math.Max(0, next.DurationSeconds - next.OutroSilence - intro - .2));
        if (available < .35) return new TransitionPlan(Math.Max(0, end), intro, 0, false, EndingEffect.Clean);
        var length = Math.Clamp(Math.Min(baseline.FadeSeconds, maxLength), .35, available);
        return new TransitionPlan(Math.Max(0, end - length), intro, length, false, EndingEffect.Clean);
    }

    private static double FirstOnGrid(double anchor, double period, double atOrAfter) =>
        anchor + Math.Ceiling((atOrAfter - anchor) / period) * period;

    private static double LastOnGrid(double anchor, double period, double atOrBefore) =>
        anchor + Math.Floor((atOrBefore - anchor) / period) * period;
}
