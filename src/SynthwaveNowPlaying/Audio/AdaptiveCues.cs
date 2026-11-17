namespace SynthwaveNowPlaying;

internal static class AdaptiveCues
{
    public static TransitionPlan Choose(TransitionProfile current, TransitionProfile next, TransitionFeatures a,
        TransitionFeatures b, double end, double preferred, double limit)
    {
        var outgoing = current.OutroBeat;
        var incoming = next.IntroBeat;
        var intro = next.IntroSilence;
        var reliable = outgoing.IsReliable && incoming.IsReliable;
        if (reliable && HasPulse(a.Outro, end - Math.Min(preferred, 3), end) &&
            HasPulse(b.Intro, intro, intro + Math.Min(preferred, 3)))
        {
            // A quarter of a beat of slip is already audible with two kick drums.
            var drift = Math.Abs(outgoing.Bpm - incoming.Bpm) / 60;
            if (drift > .001) limit = Math.Min(limit, Math.Max(.6, .22 / drift));
        }
        preferred = Math.Min(preferred, limit);
        var best = new TransitionPlan(end - preferred, intro, preferred, false);
        double bestScore = double.PositiveInfinity;
        void Consider(double start, double seek, bool aligned)
        {
            double length = end - start;
            if (length < .3 || length > limit + .00001 || start < 0 || seek < intro - .001 ||
                seek > intro + 1.6 || (next.DurationSeconds - next.OutroSilence - seek - .1) * .4 < length - .001) return;
            if (seek > intro + .25)
                for (double t = intro; t < seek; t += .25)
                    if (b.Intro.At(t).Voice > .36) return;
            double conflict = 0, density = 0;
            for (double t = 0; t < length; t += .25)
            {
                var old = a.Outro.At(start + t);
                var fresh = b.Intro.At(seek + t);
                double both = Math.Sin(Math.PI * (t + .125) / Math.Max(.25, length));
                conflict += Math.Max(0, both) * Math.Min(old.Voice, fresh.Voice) * .25;
                density += old.Attack * fresh.Attack * .25;
            }
            double score = .55 * Math.Abs(length - preferred) / Math.Max(1, preferred)
                + 1.4 * conflict + .09 * density + .26 * (seek - intro);
            if (aligned)
            {
                score -= .28;
                if (IsBar(start, a.Outro.BarAnchor, outgoing)) score -= .13;
                if (IsBar(seek, b.Intro.BarAnchor, incoming)) score -= .13;
            }
            if (score >= bestScore) return;
            bestScore = score;
            best = new(start, seek, length, aligned);
        }

        foreach (double factor in new[] { 1d, 1.15, .83, .63, .42 })
        {
            double start = end - Math.Min(preferred * factor, limit);
            Consider(start, intro, false);
            if (!reliable) continue;
            var snapped = outgoing.Nearest(start);
            var first = incoming.FirstAtOrAfter(intro);
            for (int i = -1; i <= 1; i++)
                for (int j = 0; j <= 2; j++)
                    Consider(snapped + i * outgoing.PeriodSeconds, first + j * incoming.PeriodSeconds, true);
        }

        if (a.NaturalTail) return best with { Effect = EndingEffect.NaturalTail };
        var ending = a.Outro.At(Math.Max(best.StartSeconds, end - .5));
        if (ending.Voice > .42 || b.Intro.At(best.NextSeekSeconds + .25).Voice > .5) return best;
        if (outgoing.IsReliable && current.OutroBlendSeconds < 4 && a.LastHit >= best.StartSeconds &&
            a.LastHit >= end - outgoing.PeriodSeconds * 1.5 && a.LastHit <= end + .05 &&
            Math.Abs(a.LastHit - outgoing.Nearest(a.LastHit)) < .1 && best.FadeSeconds >= .8)
            return best with { Effect = EndingEffect.Echo, EchoHit = a.LastHit,
                EchoDelay = outgoing.PeriodSeconds / 2 };
        if (best.FadeSeconds >= 2.5 && ending.Tone > .12)
            return best with { Effect = EndingEffect.Filter };
        return best;
    }

    private static bool IsBar(double time, double? anchor, BeatGrid beat) =>
        anchor.HasValue && Math.Abs(Math.IEEERemainder(time - anchor.Value, 4 * beat.PeriodSeconds)) < .08;

    private static bool HasPulse(CueSection section, double from, double to)
    {
        int hits = 0;
        for (double t = from; t < to; t += CueSection.Step)
        {
            var bin = section.At(t);
            if (bin.Attack > .22 && bin.Level > .08 && ++hits >= 2) return true;
        }
        return false;
    }
}

