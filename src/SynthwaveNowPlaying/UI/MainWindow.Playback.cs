using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SynthwaveNowPlaying;

public partial class MainWindow : Window
{
    private DateTime _lastPlaybackSaveUtc;

    private void SavePlaybackState(bool force = false)
    {
        if (!force && DateTime.UtcNow - _lastPlaybackSaveUtc < TimeSpan.FromSeconds(5)) return;
        _lastPlaybackSaveUtc = DateTime.UtcNow;
        _settings.LastTrack = _localTrack;
        _settings.LastQueueSourceId = _activeQueueSourceId;
        _settings.LastPositionSeconds = _audioReader?.CurrentTime.TotalSeconds ?? 0;
        _settings.Save();
    }

    private void RestorePlaybackState()
    {
        var file = _settings.LastTrack;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file) || !_library.Contains(file, StringComparer.OrdinalIgnoreCase)) return;
        var source = _playlistSources.FirstOrDefault(item =>
            string.Equals(item.Id, _settings.LastQueueSourceId, StringComparison.OrdinalIgnoreCase) &&
            item.Files.Contains(file, StringComparer.OrdinalIgnoreCase))
            ?? _playlistSources.FirstOrDefault(item => item.Files.Contains(file, StringComparer.OrdinalIgnoreCase));
        if (source is null) return;
        _activeQueue = source.Files.ToList();
        _activeQueueSourceId = source.Id;
        PlaylistSourcePicker.SelectedItem = source;
        var index = _activeQueue.FindIndex(item => string.Equals(item, file, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        TryPlayLibraryTrack(index, _settings.LastPositionSeconds);
    }

    // Only the local NAudio playback chain receives EQ changes.
    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_radioActive)
        {
            if (_radioOut is null) { _ = StartRadioModeAsync(); return; }
            if (_radioOut.PlaybackState == PlaybackState.Playing) _radioOut.Pause(); else _radioOut.Play();
            var playing = _radioOut.PlaybackState == PlaybackState.Playing;
            SetPlayPauseState(playing);
            SourceStatus.Text = playing ? "RADIO · ON AIR" : "RADIO · PAUSED";
            return;
        }
        if (_localTrack is not null && _waveOut is not null)
        {
            if (_incomingReader is not null) ToggleDemoCrossfadePause();
            else if (_waveOut.PlaybackState == PlaybackState.Playing) _waveOut.Pause();
            else _waveOut.Play();
            SetPlayPauseState(_waveOut.PlaybackState == PlaybackState.Playing);
            SourceStatus.Text = _waveOut.PlaybackState == PlaybackState.Playing ? "LOCAL · PLAYING" : "LOCAL · PAUSED";
            SavePlaybackState(force: true);
            return;
        }
        StartPlaylistPlayback(false);
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_radioActive) return;
        if (_localTrack is null) StartPlaylistPlayback(true);
        else SkipLocalTrack(forward: false);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_radioActive) return;
        if (_localTrack is null) StartPlaylistPlayback(false);
        else SkipLocalTrack(forward: true);
    }

    private void StartPlaylistPlayback(bool fromEnd)
    {
        if (PlaylistSourcePicker.SelectedItem is not PlaylistSource source || source.Files.Count == 0) return;
        _activeQueue = source.Files.ToList();
        _activeQueueSourceId = source.Id;
        PlayLibraryTrack(fromEnd ? _activeQueue.Count - 1 : 0);
    }

    private static string FormatTime(TimeSpan value) => $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    private void SkipLocalTrack(bool forward)
    {
        if (_activeQueue.Count == 0)
        {
            StopLocalPlayback();
            _localTrack = null;
            _localProgressTimer.Stop();
            SetPlayPauseState(false);
            SourceStatus.Text = "PLAYLIST EMPTY";
            return;
        }
        var currentIndex = _activeQueue.FindIndex(file => string.Equals(file, _localTrack, StringComparison.OrdinalIgnoreCase));
        var nextIndex = currentIndex < 0
            ? (forward ? 0 : _activeQueue.Count - 1)
            : (currentIndex + (forward ? 1 : -1) + _activeQueue.Count) % _activeQueue.Count;
        PlayLibraryTrack(nextIndex);
    }

    private void PlayLibraryTrack(int index)
    {
        if (_activeQueue.Count == 0) _activeQueue = _library.ToList();
        if (_activeQueue.Count == 0 || index < 0) return;
        index %= _activeQueue.Count;
        // Skip unreadable tracks so one bad file does not stall an entire playlist.
        for (var attempt = 0; attempt < _activeQueue.Count; attempt++)
            if (TryPlayLibraryTrack((index + attempt) % _activeQueue.Count)) return;
        _localTrack = null;
        SetPlayPauseState(false);
        SourceStatus.Text = "NO PLAYABLE TRACKS";
    }

    private bool TryPlayLibraryTrack(int index, double resumeSeconds = 0)
    {
        if (_radioActive) StopRadioMode(true);
        var file = _activeQueue[index];
        _localTrack = file;
        var tags = _trackMetadataCache.TryGetValue(file, out var cached) ? cached : ReadTrackMetadata(file);
        _trackMetadataCache[file] = tags;
        TrackTitle.Text = tags.Title;
        ArtistName.Text = tags.Artist;
        RefreshTrackKey(file);
        AlbumName.Text = string.IsNullOrWhiteSpace(tags.Album) ? Path.GetDirectoryName(file) ?? "" : tags.Album;
        SourceStatus.Text = "LOCAL · PLAYING";
        SetPlayPauseState(true);
        try
        {
            SetAlbumArt(tags.Artwork);
            FallbackCover.Visibility = tags.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
            StopLocalPlayback();
            _remixFailedFiles.Clear();
            _currentLevel = 1;
            _audioReader = new AudioFileReader(file) { Volume = (float)_settings.Volume };
            if (double.IsFinite(resumeSeconds) && resumeSeconds > 0)
                _audioReader.CurrentTime = TimeSpan.FromSeconds(Math.Min(resumeSeconds,
                    Math.Max(0, _audioReader.TotalTime.TotalSeconds - 2)));
            _equalizer = new LocalEqualizer(_audioReader) { Enabled = _settings.EqualizerEnabled };
            for (var band = 0; band < Math.Min(6, _settings.Bands.Length); band++) _equalizer.SetGain(band, (float)_settings.Bands[band]);
            _currentBassSplit = new BassSwapSampleProvider(_equalizer);
            _currentMixInput = new AdaptiveDeck(PrepareMixInput(_currentBassSplit), _currentBassSplit,
                _audioReader.CurrentTime.TotalSeconds);
            _localMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
            _localMixer.AddMixerInput(_currentMixInput);
            // Short reads feed the spectrum about once per display frame. Keep three buffers for playback headroom.
            _waveOut = new WaveOut { BufferMilliseconds = 16, NumberOfBuffers = _settings.StableAudioBuffering ? 8 : 3 };
            _renderGate = new RenderGate(_localMixer);
            var output = new PeakLimiterSampleProvider(_renderGate);
            _waveOut.Init(new SpectrumTap(output, _audioSamples, _archived, () => _classicActive, _analysisSignal, NotifyAudioSignal));
            _waveOut.PlaybackStopped += LocalPlaybackStopped;
            _waveOut.Play();
            SourceStatus.Text = "LOCAL · PLAYING";
            SetPlayPauseState(true);
            _localProgressTimer.Start();
            UpdateLocalProgress();
            SavePlaybackState(force: true);
            WarmTransitionAnalysis(index);
            foreach (var row in _allPlaylistRows) row.IsCurrent = string.Equals(row.File, file, StringComparison.OrdinalIgnoreCase);
            return true;
        }
        catch (Exception ex)
        {
            StopLocalPlayback();
            SourceStatus.Text = "UNSUPPORTED AUDIO FORMAT";
            ToolTip = ex.Message;
            SetPlayPauseState(false);
            return false;
        }
    }

    private void LocalPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Manual skips detach the old handler before Stop(). Ignore any callback
            // queued by an earlier player after the next track has already started.
            if (!ReferenceEquals(sender, _waveOut) || _localTrack is null) return;
            if (e.Exception is not null)
            {
                SourceStatus.Text = "PLAYBACK FAILED";
                ToolTip = e.Exception.Message;
                StopLocalPlayback();
                _localTrack = null;
                _localProgressTimer.Stop();
                SetPlayPauseState(false);
                return;
            }
            if (_incomingReader is not null) { FinishDemoCrossfade(); _waveOut?.Play(); }
            else SkipLocalTrack(forward: true);
        });
    }

    private void UpdateLocalProgress()
    {
        if (_localTrack is null) return;
        if (_audioReader is null) return;
        var position = TimeSpan.FromSeconds((double)_audioReader.Position / _audioReader.WaveFormat.AverageBytesPerSecond);
        ElapsedText.Text = FormatTime(position);
        RefreshLocalConcept();
        var duration = TimeSpan.FromSeconds((double)_audioReader.Length / _audioReader.WaveFormat.AverageBytesPerSecond);
        DurationText.Text = FormatTime(duration);
        if (!TrackProgress.IsMouseCaptureWithin) AnimateProgress(duration.TotalMilliseconds <= 0 ? 0 : position.TotalMilliseconds / duration.TotalMilliseconds * 1000);
        MaybeStartDemoCrossfade((duration - position).TotalSeconds, duration.TotalSeconds);
        SavePlaybackState();
    }

    private void EqButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.EqualizerEnabled)
        {
            _settings.EqualizerEnabled = true;
            if (_equalizer is not null) _equalizer.Enabled = true;
            if (_incomingEqualizer is not null) _incomingEqualizer.Enabled = true;
            for (var band = 0; band < Math.Min(6, _settings.Bands.Length); band++) SetSpectrumGain(band, _settings.Bands[band]);
            _settings.Save();
        }
        Spectrum.IsEditing = !Spectrum.IsEditing;
        _archived.SetEditing(Spectrum.IsEditing);
        EqButton.Tag = Spectrum.IsEditing ? "Active" : null;
        EqButton.ToolTip = Spectrum.IsEditing ? "Drag bands to adjust music played in this app" : "EQ for music played in this app";
        UpdateEqResetButton();
    }

    private void EqResetButton_Click(object sender, RoutedEventArgs e)
    {
        for (var band = 0; band < _settings.Bands.Length; band++)
        {
            _settings.Bands[band] = 0;
            SetSpectrumGain(band, 0);
            _equalizer?.SetGain(band, 0);
            _incomingEqualizer?.SetGain(band, 0);
        }
        UpdateEqResetButton();
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void UpdateEqResetButton() =>
        EqResetButton.Visibility = _settings.EqualizerEnabled && _settings.Bands.Any(gain => Math.Abs(gain) >= .05)
            ? Visibility.Visible : Visibility.Collapsed;

    private void Spectrum_BandGainChanged(int band, float gain)
    {
        if ((uint)band >= _settings.Bands.Length) return;
        SetSpectrumGain(band, gain);
        _settings.Bands[band] = gain;
        _equalizer?.SetGain(band, gain);
        _incomingEqualizer?.SetGain(band, gain);
        UpdateEqResetButton();
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void AnimateProgress(double value)
    {
        TrackProgress.BeginAnimation(Slider.ValueProperty, new DoubleAnimation(value, TimeSpan.FromMilliseconds(260))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void TrackProgress_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_audioReader is null || _waveOut is null || TrackProgress.ActualWidth <= 0) return;
        var ratio = Math.Clamp(e.GetPosition(TrackProgress).X / TrackProgress.ActualWidth, 0, 1);
        SeekLocal(ratio);
    }

    private void SeekLocal(double ratio)
    {
        if (_audioReader is null || _waveOut is null) return;
        CancelDemoCrossfade();
        var position = (long)(_audioReader.Length * ratio);
        position -= position % _audioReader.WaveFormat.BlockAlign;
        lock (_renderGate!)
        {
            _audioReader.Position = position;
            if (_currentMixInput is AdaptiveDeck deck)
            {
                deck.ClearPrefetch();
                deck.Frame = (long)Math.Round(_audioReader.CurrentTime.TotalSeconds * deck.Rate);
            }
        }
        if (_waveOut.PlaybackState != PlaybackState.Playing) _waveOut.Play();
        SavePlaybackState(force: true);
    }

    private void StopLocalPlayback()
    {
        CancelDemoCrossfade();
        if (_waveOut is not null) { _waveOut.PlaybackStopped -= LocalPlaybackStopped; _waveOut.Stop(); _waveOut.Dispose(); _waveOut = null; }
        _localMixer = null; _currentMixInput = null;
        _audioReader?.Dispose(); _audioReader = null; _equalizer = null; _currentBassSplit = null;
    }
}
