using System.Windows;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    // Playback order is separate from the visible playlist. Automix consumes
    // this same queue, so it analyzes and mixes the actual shuffled successor.
    private List<string> CreatePlaybackQueue(IEnumerable<string> files,bool reshuffle=false)
    {
        var source=files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(!_settings.ShuffleEnabled||source.Count<2)return source;
        var remaining=new HashSet<string>(source,StringComparer.OrdinalIgnoreCase);
        var result=reshuffle?new List<string>():_activeQueue.Where(remaining.Contains).ToList();
        foreach(var file in result)remaining.Remove(file);
        var additions=source.Where(remaining.Contains).ToList();
        for(int i=additions.Count-1;i>0;i--){int j=Random.Shared.Next(i+1);(additions[i],additions[j])=(additions[j],additions[i]);}
        result.AddRange(additions);return result;
    }
    private void Shuffle_Click(object sender,RoutedEventArgs e)
    {
        if(_radioActive)return;
        CancelDemoCrossfade();
        _currentLevel=1;
        ApplyPlaybackVolume();
        _settings.ShuffleEnabled=!_settings.ShuffleEnabled;
        if(_settings.ShuffleEnabled)
        {
            _activeQueue=CreatePlaybackQueue(_activeQueue,true);
            int current=_activeQueue.FindIndex(f=>string.Equals(f,_localTrack,StringComparison.OrdinalIgnoreCase));
            if(current>=0){var file=_activeQueue[current];_activeQueue.RemoveAt(current);_activeQueue.Insert(0,file);}
        }
        else
        {
            var source=_playlistSources.FirstOrDefault(p=>string.Equals(p.Id,_activeQueueSourceId,StringComparison.OrdinalIgnoreCase));
            if(source is not null)_activeQueue=source.Files.ToList();
        }
        _settings.Save();UpdateShuffleState();
        var index=_activeQueue.FindIndex(f=>string.Equals(f,_localTrack,StringComparison.OrdinalIgnoreCase));
        if(index>=0&&_settings.RemixDemoEnabled)WarmTransitionAnalysis(index);
    }
    private void UpdateShuffleState()
    {
        ShuffleButton.SetShuffleState(_settings.ShuffleEnabled);
        ShuffleButton.Visibility=_radioActive?Visibility.Collapsed:Visibility.Visible;
        _radioConcept?.Shuffle.SetShuffleState(_settings.ShuffleEnabled);
    }
}
