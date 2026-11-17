namespace SynthwaveNowPlaying;

/// <summary>Measures the shape of the music using levels relative to its own recording.</summary>
internal static class TransitionDynamics
{
    // Ignore near-digital silence when taking the reference. Padding with silence
    // must not change the chosen duration for the exact same musical ending.
    internal static double Reference(float[] levels)
    {
        var audible = levels.Where(level => level > .00003f).Order().ToArray();
        return audible.Length == 0 ? 0 : audible[(audible.Length - 1) * 8 / 10];
    }

    internal static (double Seconds, double BlendSeconds) MeasureOutro(float[] frames, double step)
    {
        const int framesPerBlock = 20;
        var levels = new float[(frames.Length + framesPerBlock - 1) / framesPerBlock];
        for (int i = 0; i < levels.Length; i++)
        {
            var block = frames.AsSpan(i * framesPerBlock, Math.Min(framesPerBlock, frames.Length - i * framesPerBlock));
            double sum = 0;
            foreach (var level in block) sum += level * level;
            levels[i] = (float)Math.Sqrt(sum / block.Length);
        }
        var reference = Reference(levels);
        if (reference == 0) return default;

        // Keep the body of a sustained, quiet coda. Only trim its tail below
        // the local -13 dB threshold, with the global reference as a noise floor.
        var audibleFloor = Math.Max(.00003, reference * .032);
        var last = Array.FindLastIndex(levels, level => level >= audibleFloor);
        if (last < 0) return default;
        var blockSeconds = step * framesPerBlock;
        var localStart = Math.Max(0, last - (int)(10 / blockSeconds));
        var localReference = Reference(levels[localStart..(last + 1)]);
        var endThreshold = Math.Max(audibleFloor, localReference * .224);
        // A single residual click after silence is a poor musical cue.
        while (last > 0 && (levels[last] < endThreshold || levels[last - 1] < endThreshold * .6))
            last--;
        if (last == 0 && levels[0] < endThreshold) return default;

        var endSeconds = Math.Min(frames.Length * step, (last + 1) * blockSeconds);
        var lastSecond = Mean(levels, Math.Max(0, last - 2), last + 1);
        var bodyLevel = Reference(levels[..(last + 1)]);
        var dropDb = 20 * Math.Log10(Math.Max(bodyLevel, .00003) / Math.Max(lastSecond, .00003));
        var softness = Math.Clamp((dropDb - 2) / 10, 0, 1);
        // A lone quiet beat before a hard stop should not imply a long fade.
        int quietBlocks = 0;
        for (var i = last; i >= 0 && levels[i] < bodyLevel * .76; i--) quietBlocks++;
        var quietSeconds = quietBlocks * blockSeconds;
        var blend = 2.1 + Math.Min(9, quietSeconds * 1.35) * softness;
        return (endSeconds, blend);
    }

    internal static double MeasureIntro(float[] levels, double step, double firstActive)
    {
        var reference = Reference(levels);
        var first = (int)(firstActive / step);
        if (reference == 0 || first >= levels.Length) return .5;
        var early = Mean(levels, first, Math.Min(levels.Length, first + (int)(1.6 / step)));
        return Math.Clamp(early / reference, 0, 1);
    }

    private static double Mean(float[] levels, int start, int end)
    {
        double sum = 0;
        for (var i = start; i < end; i++) sum += levels[i];
        return sum / Math.Max(1, end - start);
    }
}
