using System.Diagnostics;

namespace SynthwaveNowPlaying;

internal static class TimedBpmScan
{
    internal static readonly TimeSpan Minimum = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan Maximum = TimeSpan.FromMinutes(1);

    // WaitAsync also bounds a decoder that blocks inside a synchronous OS read.
    // Cancellation checks in the analyzer prevent a late result entering the cache.
    internal static async Task<TrackAnalysis> RunAsync(Func<CancellationToken, TrackAnalysis> analyze,
        Action<TimeSpan, bool> report, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(Maximum);
        var token = deadline.Token;
        var work = Task.Run(() => analyze(token), token);
        try
        {
            while (clock.Elapsed < Minimum || !work.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                report(clock.Elapsed, work.IsCompleted);
                if (clock.Elapsed >= Minimum)
                    await Task.WhenAny(work, Task.Delay(400, token));
                else
                    await Task.Delay(400, token);
            }
            token.ThrowIfCancellationRequested();
            return await work.WaitAsync(token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("No verified BPM within one minute.");
        }
    }
}

