using System.Windows;
using System.Windows.Media.Animation;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private readonly HashSet<string> _keyPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _keyWorker = new(1, 1);

    private void RefreshTrackKey(string file)
    {
        if (_analysisCache.TryGet(file, out var cached) && cached.KeyScanned)
        {
            var text = cached.Key.Reliable ? $"KEY  {cached.Key.Display}" : "KEY  UNSURE";
            if (TrackKey.Text != text)
            {
                TrackKey.Text = text;
                TrackKey.BeginAnimation(OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(250)));
            }
            TrackKey.ToolTip = "Estimated musical key · Camelot notation · scanned locally";
            return;
        }
        TrackKey.Text = "KEY  ···";
        TrackKey.ToolTip = "Analyzing the track's harmony in the background";
        if (!_keyPending.Add(file)) return;
        _ = AnalyzePlayingKeyAsync(file);
    }

    private async Task AnalyzePlayingKeyAsync(string file)
    {
        await _keyWorker.WaitAsync();
        try
        {
            if (!IsLoaded || !string.Equals(file, _localTrack, StringComparison.OrdinalIgnoreCase)) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var analysis = await Task.Run(() => _analysisCache.AnalyzeKey(file, timeout.Token), timeout.Token)
                .WaitAsync(timeout.Token);
            await Task.Run(_analysisCache.Save);
            if (IsLoaded) ApplyTrackAnalysis(file, analysis);
        }
        catch
        {
            if (IsLoaded && string.Equals(file, _localTrack, StringComparison.OrdinalIgnoreCase))
            {
                TrackKey.Text = "KEY  —";
                TrackKey.ToolTip = "Key analysis unavailable; retry with the playlist scanner";
            }
        }
        finally { _keyPending.Remove(file); _keyWorker.Release(); }
    }
}
