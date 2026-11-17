using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SynthwaveNowPlaying;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _crossfadeTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Dictionary<string, TransitionProfile> _transitionProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _transitionPending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _remixFailedFiles = new(StringComparer.OrdinalIgnoreCase);
    private AudioFileReader? _incomingReader;
    private LocalEqualizer? _incomingEqualizer;
    private BassSwapSampleProvider? _currentBassSplit;
    private BassSwapSampleProvider? _incomingBassSplit;
    private ISampleProvider? _incomingMixInput;
    private string? _incomingTrack;
    private DateTime _fadeStartedUtc;
    private TimeSpan _fadeElapsedBeforePause;
    private double _fadeDurationSeconds;
    private double _incomingLevel = 1;
    private double _currentLevel = 1;
    private double _bassSwitchProgress = .5;
    private bool _bassSwitchOnBeat;
    private RenderGate? _renderGate;
    private bool _adaptiveArmed;

    private void RemixDemoButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.RemixDemoEnabled = !_settings.RemixDemoEnabled;
        if (!_settings.RemixDemoEnabled && AdaptiveWaiting) CancelDemoCrossfade();
        _settings.Save();
        UpdateRemixDemoButton();
        if (!_settings.RemixDemoEnabled && _incomingReader is null) _crossfadeTimer.Stop();
        if (_settings.RemixDemoEnabled && _localTrack is not null)
        {
            var index = _activeQueue.FindIndex(file => string.Equals(file, _localTrack, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) WarmTransitionAnalysis(index);
        }
    }

    private void UpdateRemixDemoButton()
    {
        RemixMode.Text = _settings.RemixDemoEnabled ? "ON" : "DEMO";
        RemixDemoButton.ToolTip = $"Automix {(_settings.RemixDemoEnabled ? "on" : "off")} · {_settings.AutomixTiming} timing";
        RemixDemoButton.Tag = _settings.RemixDemoEnabled ? "Active" : null;
    }

    // All local tracks enter the same 44.1 kHz stereo output. Resampling runs only when needed.
    private static ISampleProvider PrepareMixInput(ISampleProvider equalizer)
    {
        ISampleProvider input = equalizer;
        if (input.WaveFormat.Channels == 1) input = new MonoToStereoSampleProvider(input);
        else if (input.WaveFormat.Channels != 2) throw new NotSupportedException("Only mono and stereo tracks can be mixed.");
        if (input.WaveFormat.SampleRate != 44100) input = new WdlResamplingSampleProvider(input, 44100);
        return input;
    }

    private void WarmTransitionAnalysis(int currentIndex)
    {
        if (_activeQueue.Count == 0) return;
        WarmTransitionAnalysis(_activeQueue[currentIndex]);
        if (_settings.RemixDemoEnabled) WarmTransitionAnalysis(_activeQueue[(currentIndex + 1) % _activeQueue.Count]);
    }

    private void WarmTransitionAnalysis(string file)
    {
        if (_analysisCache.TryGet(file, out var cached) && cached.Profile.DurationSeconds > 0)
        {
            ApplyTrackAnalysis(file, cached);
            return;
        }
        if (!_transitionPending.Add(file)) return;
        _ = Task.Run(() =>
        {
            try
            {
                var analysis = _analysisCache.AnalyzeQuick(file);
                _analysisCache.Save();
                Dispatcher.BeginInvoke(() => { ApplyTrackAnalysis(file, analysis); _transitionPending.Remove(file); });
            }
            catch
            {
                try { Dispatcher.BeginInvoke(() => _transitionPending.Remove(file)); } catch { }
            }
        });
    }

    private void ApplyTrackAnalysis(string file, TrackAnalysis analysis)
    {
        _transitionProfiles[file] = analysis.Profile;
    }

    private void MaybeStartDemoCrossfade(double remainingSeconds, double totalSeconds)
    {
        if (!_settings.RemixDemoEnabled || _incomingReader is not null || _waveOut?.PlaybackState != PlaybackState.Playing ||
            _localMixer is null || _localTrack is null || _activeQueue.Count < 2 || totalSeconds < 1) return;

        var current = _activeQueue.FindIndex(file => string.Equals(file, _localTrack, StringComparison.OrdinalIgnoreCase));
        if (current < 0) return;
        if (remainingSeconds < .5) return;
        var timing = _settings.AutomixTiming;
        var currentProfile = _transitionProfiles.GetValueOrDefault(_localTrack).ForTiming(timing);
        bool adaptive = timing == AutomixTiming.Adaptive;
        if (remainingSeconds > Math.Max(currentProfile.OutroSilence, 0) + 15) return;
        var deck = (AdaptiveDeck)_currentMixInput!;
        // Prepare the decoder ahead of the cue. The render thread owns its exact onset.
        double lead = adaptive ? 1.25 : 0;

        for (var step = 1; step < _activeQueue.Count; step++)
        {
            var file = _activeQueue[(current + step) % _activeQueue.Count];
            if (_remixFailedFiles.Contains(file)) continue;
            var nextProfile = _transitionProfiles.GetValueOrDefault(file).ForTiming(timing);
            var plan = TransitionPlanner.Plan(totalSeconds, currentProfile, nextProfile, timing);
            if (totalSeconds - remainingSeconds + lead < plan.StartSeconds) return;
            AudioFileReader? reader = null;
            ISampleProvider? input = null;
            var added = false;
            try
            {
                reader = new AudioFileReader(file) { Volume = adaptive ? 1 : 0 };
                // The next clip may be short or its analysis may still be in flight.
                // Preserve its playlist position and leave enough music after the handoff.
                if (nextProfile.DurationSeconds <= 0)
                {
                    nextProfile = nextProfile with { DurationSeconds = reader.TotalTime.TotalSeconds, IntroAttack = .5 };
                    _transitionProfiles[file] = nextProfile;
                    plan = TransitionPlanner.Plan(totalSeconds, currentProfile, nextProfile, timing);
                }
                if (totalSeconds - remainingSeconds + lead < plan.StartSeconds || plan.FadeSeconds < .3)
                {
                    reader.Dispose();
                    return;
                }
                if (plan.NextSeekSeconds > 0) reader.CurrentTime = TimeSpan.FromSeconds(plan.NextSeekSeconds);
                var equalizer = new LocalEqualizer(reader) { Enabled = _settings.EqualizerEnabled };
                for (var band = 0; band < Math.Min(6, _settings.Bands.Length); band++)
                    equalizer.SetGain(band, (float)_settings.Bands[band]);
                var bassSplit = new BassSwapSampleProvider(equalizer) { TargetBassGain = 0 };
                var nextDeck = new AdaptiveDeck(PrepareMixInput(bassSplit), bassSplit, reader.CurrentTime.TotalSeconds);
                if (adaptive) nextDeck.Prefetch();
                input = nextDeck;
                _incomingLevel = currentProfile.OutroRms > .001 && nextProfile.IntroRms > .001
                    ? Math.Clamp(_currentLevel * currentProfile.OutroRms / nextProfile.IntroRms, .8, 1.2)
                    : _currentLevel;
                // Complete the blend while the outgoing song is still audible.
                _fadeDurationSeconds = Math.Max(.25, Math.Min(plan.FadeSeconds, remainingSeconds - currentProfile.OutroSilence - .1));
                _bassSwitchOnBeat = plan.BeatAligned && currentProfile.OutroBeat.IsReliable;
                _bassSwitchProgress = .5;
                lock (_renderGate!)
                {
                    if (adaptive)
                    {
                        double rendered = (double)deck.Frame / deck.Rate;
                        double start = Math.Max(rendered, plan.StartSeconds);
                        if (_bassSwitchOnBeat && rendered > plan.StartSeconds + .025)
                        {
                            double beat = currentProfile.OutroBeat.FirstAtOrAfter(rendered);
                            if (beat < plan.StartSeconds + plan.FadeSeconds - .6) start = beat;
                            else _bassSwitchOnBeat = false;
                        }
                        var length = Math.Max(.25, plan.StartSeconds + plan.FadeSeconds - start);
                        var effective = plan with { StartSeconds = start, FadeSeconds = length, BeatAligned = _bassSwitchOnBeat };
                        if (effective.Effect == EndingEffect.Echo && effective.EchoHit < start)
                            effective = effective with { Effect = EndingEffect.Clean };
                        var wait = Math.Max(0, (long)Math.Round(start * deck.Rate) - deck.Frame);
                        double swap = _bassSwitchOnBeat
                            ? Math.Clamp((currentProfile.OutroBeat.Nearest(start + length * .5) - start) / length, .25, .75) : .5;
                        _fadeDurationSeconds = length;
                        _audioReader!.Volume = 1;
                        reader.Volume = 1;
                        deck.Arm(effective, false, wait, swap, (float)(_settings.Volume * _currentLevel));
                        nextDeck.Arm(effective, true, wait, swap, (float)(_settings.Volume * _incomingLevel));
                        _adaptiveArmed = true;
                    }
                    _localMixer.AddMixerInput(input);
                    added = true;
                }
                _incomingReader = reader;
                _incomingEqualizer = equalizer;
                _incomingBassSplit = bassSplit;
                _incomingMixInput = input;
                _incomingTrack = file;
                if (!adaptive && _bassSwitchOnBeat)
                {
                    var actualStart = totalSeconds - remainingSeconds;
                    var beat = currentProfile.OutroBeat.Nearest(actualStart + _fadeDurationSeconds * .5);
                    var fraction = (beat - actualStart) / _fadeDurationSeconds;
                    if (fraction is >= .3 and <= .7) _bassSwitchProgress = fraction;
                    else _bassSwitchOnBeat = false;
                }
                _fadeElapsedBeforePause = TimeSpan.Zero;
                _fadeStartedUtc = DateTime.UtcNow;
                _crossfadeTimer.Start();
                return;
            }
            catch
            {
                lock (_renderGate!)
                {
                    if (added && input is not null) _localMixer.RemoveMixerInput(input);
                    if (adaptive) { deck.Release(); _adaptiveArmed = false; _audioReader!.Volume = (float)(_settings.Volume * _currentLevel); }
                }
                reader?.Dispose();
                _remixFailedFiles.Add(file);
            }
        }
    }

    private double FadeProgress => Math.Clamp((_fadeElapsedBeforePause.TotalSeconds +
        (_crossfadeTimer.IsEnabled ? (DateTime.UtcNow - _fadeStartedUtc).TotalSeconds : 0)) / _fadeDurationSeconds, 0, 1);

    private void UpdateDemoCrossfade()
    {
        if (_incomingReader is null)
        {
            _crossfadeTimer.Stop();
            return;
        }
        if (_adaptiveArmed)
        {
            bool done;
            lock (_renderGate!) done = ((AdaptiveDeck)_currentMixInput!).Finished;
            if (done) FinishDemoCrossfade();
            return;
        }
        ApplyPlaybackVolume();
        if (FadeProgress >= 1) FinishDemoCrossfade();
    }

    private void ApplyPlaybackVolume()
    {
        var volume = (float)Math.Clamp(_settings.Volume, 0, 1);
        if (_adaptiveArmed)
        {
            lock (_renderGate!)
            {
                ((AdaptiveDeck)_currentMixInput!).Gain = volume * (float)_currentLevel;
                ((AdaptiveDeck)_incomingMixInput!).Gain = volume * (float)_incomingLevel;
            }
            return;
        }
        if (_incomingReader is null)
        {
            if (_audioReader is not null) _audioReader.Volume = volume * (float)_currentLevel;
            if (_currentBassSplit is not null) _currentBassSplit.TargetBassGain = 1;
            return;
        }
        var progress = FadeProgress;
        if (_audioReader is not null) _audioReader.Volume = volume * (float)(_currentLevel * Math.Cos(progress * Math.PI / 2));
        _incomingReader.Volume = volume * (float)(Math.Sin(progress * Math.PI / 2) * _incomingLevel);
        var halfWidth = _bassSwitchOnBeat ? .025 : Math.Min(.12, .35 / _fadeDurationSeconds);
        static double Smooth(double value) { value = Math.Clamp(value, 0, 1); return value * value * (3 - 2 * value); }
        double outgoingBass, incomingBass;
        if (_bassSwitchOnBeat)
        {
            // Clear the old kick just before the beat, then introduce the new
            // kick just after it. Both low bands stay apart at the handoff.
            outgoingBass = 1 - Smooth((progress - (_bassSwitchProgress - halfWidth)) / halfWidth);
            incomingBass = Smooth((progress - _bassSwitchProgress) / halfWidth);
        }
        else
        {
            incomingBass = Smooth((progress - (_bassSwitchProgress - halfWidth)) / (halfWidth * 2));
            outgoingBass = 1 - incomingBass;
        }
        if (_currentBassSplit is not null) _currentBassSplit.TargetBassGain = (float)outgoingBass;
        if (_incomingBassSplit is not null) _incomingBassSplit.TargetBassGain = (float)incomingBass;
    }

    private void ToggleDemoCrossfadePause()
    {
        if (_waveOut is null || _incomingReader is null) return;
        if (_waveOut.PlaybackState == PlaybackState.Playing)
        {
            _fadeElapsedBeforePause += DateTime.UtcNow - _fadeStartedUtc;
            _crossfadeTimer.Stop();
            _waveOut.Pause();
        }
        else
        {
            _waveOut.Play();
            _fadeStartedUtc = DateTime.UtcNow;
            _crossfadeTimer.Start();
        }
    }

    private void FinishDemoCrossfade()
    {
        if (_incomingReader is null || _incomingTrack is null) return;
        _crossfadeTimer.Stop();
        lock (_renderGate!)
        {
            if (_currentMixInput is not null) _localMixer?.RemoveMixerInput(_currentMixInput);
            if (_adaptiveArmed)
            {
                _incomingReader.Volume = (float)(_settings.Volume * _incomingLevel);
                ((AdaptiveDeck)_incomingMixInput!).Release();
                _adaptiveArmed = false;
            }
        }
        _audioReader?.Dispose();
        _audioReader = _incomingReader;
        _equalizer = _incomingEqualizer;
        _currentBassSplit = _incomingBassSplit;
        if (_currentBassSplit is not null) _currentBassSplit.TargetBassGain = 1;
        _currentMixInput = _incomingMixInput;
        _currentLevel = _incomingLevel;
        _localTrack = _incomingTrack;
        _remixFailedFiles.Clear();
        _incomingReader = null;
        _incomingEqualizer = null;
        _incomingBassSplit = null;
        _incomingMixInput = null;
        _incomingTrack = null;
        ApplyPlaybackVolume();

        var file = _localTrack;
        var tags = _trackMetadataCache.TryGetValue(file, out var cached) ? cached : ReadTrackMetadata(file);
        _trackMetadataCache[file] = tags;
        TrackTitle.Text = tags.Title;
        ArtistName.Text = tags.Artist;
        AlbumName.Text = string.IsNullOrWhiteSpace(tags.Album) ? Path.GetDirectoryName(file) ?? "" : tags.Album;
        SetAlbumArt(tags.Artwork);
        FallbackCover.Visibility = tags.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
        SourceStatus.Text = "LOCAL · PLAYING";
        foreach (var row in _allPlaylistRows) row.IsCurrent = string.Equals(row.File, file, StringComparison.OrdinalIgnoreCase);
        var index = _activeQueue.FindIndex(item => string.Equals(item, file, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) WarmTransitionAnalysis(index);
        UpdateLocalProgress();
    }

    private bool AdaptiveWaiting
    {
        get
        {
            if (!_adaptiveArmed) return false;
            lock (_renderGate!) return ((AdaptiveDeck)_incomingMixInput!).Age < 0;
        }
    }

    private void CancelDemoCrossfade()
    {
        _crossfadeTimer.Stop();
        if (_incomingReader is null) return;
        lock (_renderGate!)
        {
            if (_incomingMixInput is not null) _localMixer?.RemoveMixerInput(_incomingMixInput);
            if (_adaptiveArmed)
            {
                ((AdaptiveDeck)_currentMixInput!).Release();
                _audioReader!.Volume = (float)(_settings.Volume * _currentLevel);
                _adaptiveArmed = false;
            }
        }
        _incomingReader.Dispose();
        _incomingReader = null;
        _incomingEqualizer = null;
        _incomingBassSplit = null;
        _incomingMixInput = null;
        _incomingTrack = null;
        if (_currentBassSplit is not null) _currentBassSplit.TargetBassGain = 1;
        ApplyPlaybackVolume();
    }
}
