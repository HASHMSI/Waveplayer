using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private sealed record RadioStation(string Id, string Name, string Genre);
    private static readonly RadioStation[] RadioStations =
    [
        new("nightride", "Nightride", "Synthwave · retro night drive"),
        new("chillsynth", "Chillsynth", "Chillwave · soft analog glow"),
        new("darksynth", "Darksynth", "Dark synth · neon afterhours"),
        new("horrorsynth", "Horrorsynth", "Horror synth · midnight"),
        new("spacesynth", "Spacesynth", "Space synth · cosmic drift"),
        new("datawave", "Datawave", "Datawave · digital nostalgia"),
        new("ebsm", "EBSM", "Electronic body synth music"),
        new("rektory", "Rektory", "Industrial · electronic"),
        new("rekt", "Rekt", "Heavy synth · high energy")
    ];
    private string _radioStation = "chillsynth";
    private static readonly HttpClient RadioClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private CancellationTokenSource? _radioCancel;
    private MediaFoundationReader? _radioReader;
    private WaveOut? _radioOut;
    private VolumeSampleProvider? _radioVolume;
    private bool _radioActive;
    private int _radioGeneration;
    private int _radioLayoutGeneration;

    private bool _radioVolumeDragging;
    private double _lastRadioY;
    private double _radioVerticalMotion;

    private void RadioChannel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var station = RadioStations.FirstOrDefault(candidate => candidate.Id == id);
        if (station is null || station.Id == _radioStation) { ClosePlaylistAnimated(); return; }
        _radioStation = station.Id;
        UpdateRadioConceptStation();
        RadioStationTitle.Text = station.Name.ToUpperInvariant() + " FM";
        RadioLiveLabel.Text = $"●  LIVE  ·  {station.Name.ToUpperInvariant()} FM  ·  NIGHTRIDE.FM";
        ClosePlaylistAnimated();
        if (!_radioActive) return;
        StopRadioAudio();
        TrackTitle.Text = station.Name + " FM";
        ArtistName.Text = "Nightride FM · live radio";
        _ = StartRadioModeAsync();
    }

    private void SpectrumFrame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_radioActive || _editMode) return;
        e.Handled = true;
        if (!SpectrumFrame.CaptureMouse()) return;
        _radioVolumeDragging = true;
        _lastRadioY = e.GetPosition(SpectrumFrame).Y;
        _radioVerticalMotion = 0;
    }

    private void SpectrumFrame_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_radioVolumeDragging) return;
        if (e.LeftButton != MouseButtonState.Pressed) { FinishRadioVolumeDrag(); return; }
        var y = e.GetPosition(SpectrumFrame).Y;
        _radioVerticalMotion += y - _lastRadioY;
        _lastRadioY = y;
        e.Handled = true;
        if (SpectrumFrame.ActualHeight < 1 || Math.Abs(_radioVerticalMotion) < .8) return;
        var next = Math.Clamp(_settings.Volume - _radioVerticalMotion / SpectrumFrame.ActualHeight, 0, 1);
        _radioVerticalMotion = 0;
        _settings.Volume = next;
        if (_radioVolume is not null) _radioVolume.Volume = (float)next;
        RadioVolumeOverlay.ShowVolume(next);
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void SpectrumFrame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_radioVolumeDragging) return;
        e.Handled = true;
        FinishRadioVolumeDrag();
    }

    private void SpectrumFrame_LostMouseCapture(object sender, MouseEventArgs e) => FinishRadioVolumeDrag();

    private void FinishRadioVolumeDrag()
    {
        _radioVolumeDragging = false;
        _radioVerticalMotion = 0;
        if (SpectrumFrame.IsMouseCaptured) SpectrumFrame.ReleaseMouseCapture();
    }

    private void RadioToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_radioActive)
        {
            StopRadioMode(true);
            RestorePlaybackState();
            return;
        }
        _ = StartRadioModeAsync();
    }

    private async Task StartRadioModeAsync()
    {
        if (_radioActive && _radioOut is not null) return;
        if (!_radioActive)
        {
            SavePlaybackState(force: true);
            StopLocalPlayback();
            _localProgressTimer.Stop();
            _localTrack = null;
            _radioActive = true;
            SetRadioLayout(true);
            ApplyUiPalette(new AlbumPalette(Color.FromRgb(113, 216, 244), Color.FromRgb(218, 132, 235)));
            TrackTitle.Text = RadioStations.First(station => station.Id == _radioStation).Name + " FM";
            ArtistName.Text = "Nightride FM · live radio";
        }
        _radioCancel?.Cancel();
        _radioCancel?.Dispose();
        var generation = ++_radioGeneration;
        _radioCancel = new CancellationTokenSource();
        SourceStatus.Text = "RADIO · CONNECTING";
        _radioConcept?.SetStatus("TUNING", "CONNECTING");
        SetPlayPauseState(false);
        _ = FollowRadioMetadataAsync(generation, _radioCancel.Token);
        MediaFoundationReader? reader = null;
        try
        {
            // Media Foundation resolves the live MP3 URL off the UI thread.
            var stationId = _radioStation;
            reader = await Task.Run(() => new MediaFoundationReader($"https://stream.nightride.fm/{stationId}.mp3"));
            if (!_radioActive || generation != _radioGeneration) { reader.Dispose(); return; }
            var source = reader.ToSampleProvider();
            var volume = new VolumeSampleProvider(source) { Volume = (float)_settings.Volume };
            var output = new WaveOut { BufferMilliseconds = 80, NumberOfBuffers = 5 };
            output.Init(new SpectrumTap(volume, _audioSamples, _archived, () => _classicActive,
                _analysisSignal, NotifyAudioSignal));
            _radioReader = reader;
            _radioOut = output;
            _radioVolume = volume;
            reader = null;
            output.Play();
            SourceStatus.Text = "RADIO · ON AIR";
            _radioConcept?.SetStatus("LIVE", "LIVE STREAM");
            SetPlayPauseState(true);
        }
        catch (Exception ex)
        {
            reader?.Dispose();
            if (generation != _radioGeneration) return;
            _radioOut?.Dispose();
            _radioOut = null;
            _radioVolume = null;
            _radioReader?.Dispose();
            _radioReader = null;
            SourceStatus.Text = "RADIO UNAVAILABLE";
            _radioConcept?.SetStatus("OFFLINE", "TAP PLAY TO RETRY");
            ToolTip = ex.Message;
            SetPlayPauseState(false);
        }
    }

    private async Task FollowRadioMetadataAsync(int generation, CancellationToken token)
    {
        var stationId = _radioStation;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var response = await RadioClient.GetAsync("https://nightride.fm/meta",
                    HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var lines = new StreamReader(stream);
                while (await lines.ReadLineAsync(token) is { } line)
                {
                    if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                    using var data = JsonDocument.Parse(line.AsSpan(6).ToString());
                    foreach (var entry in data.RootElement.EnumerateArray())
                    {
                        if (entry.GetProperty("station").GetString() != stationId) continue;
                        var title = entry.GetProperty("title").GetString();
                        var artist = entry.TryGetProperty("artist", out var artistValue) ? artistValue.GetString() : null;
                        if (string.IsNullOrWhiteSpace(artist) && title?.IndexOf(" - ", StringComparison.Ordinal) is int split && split > 0)
                        {
                            artist = title[..split];
                            title = title[(split + 3)..];
                        }
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (!_radioActive || generation != _radioGeneration) return;
                            TrackTitle.Text = title;
                            ArtistName.Text = string.IsNullOrWhiteSpace(artist) ? "Nightride FM" : artist;
                            _radioConcept?.UpdateTrack(string.IsNullOrWhiteSpace(artist) ? title : $"{artist} — {title}");
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch { /* A dropped event stream reconnects without interrupting audio. */ }
            try { await Task.Delay(TimeSpan.FromSeconds(3), token); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void StopRadioMode(bool restoreLayout)
    {
        if (!_radioActive && _radioOut is null) return;
        FinishRadioVolumeDrag();
        RadioVolumeOverlay.StopAnimation();
        _radioActive = false;
        StopRadioAudio();
        if (restoreLayout)
        {
            SetRadioLayout(false);
            ApplyUiPalette(ThemePalette());
            SourceStatus.Text = "LOCAL LIBRARY";
            TrackTitle.Text = "Choose a track";
            ArtistName.Text = "Local music";
            SetPlayPauseState(false);
        }
    }

    private void StopRadioAudio()
    {
        _radioGeneration++;
        _radioCancel?.Cancel();
        _radioCancel?.Dispose();
        _radioCancel = null;
        _radioOut?.Stop();
        _radioOut?.Dispose();
        _radioOut = null;
        _radioVolume = null;
        _radioReader?.Dispose();
        _radioReader = null;
    }

    private void SetRadioLayout(bool radio)
    {
        var generation = ++_radioLayoutGeneration;
        if (_radioConcept is not null)
        {
            ClosePlaylistAnimated();
            _radioConcept.TransitionMode(() =>
            {
                if (generation == _radioLayoutGeneration && IsLoaded) ApplyRadioLayout(radio);
            });
            return;
        }
        bool applied = false;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.Completed += (_, _) =>
        {
            if (applied || generation != _radioLayoutGeneration || !IsLoaded) return;
            applied = true;
            ApplyRadioLayout(radio);
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(340))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            MediaContentGrid.BeginAnimation(OpacityProperty, fadeIn);
            PlayerFooter.BeginAnimation(OpacityProperty, fadeIn);
            RadioSpectrumScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(.94, 1, TimeSpan.FromMilliseconds(370))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            RadioSpectrumScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(.88, 1, TimeSpan.FromMilliseconds(370))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };
        MediaContentGrid.BeginAnimation(OpacityProperty, fadeOut);
        PlayerFooter.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ApplyRadioLayout(bool radio)
    {
        CoverColumn.Width = new GridLength(radio ? 0 : 86);
        AlbumArtFrame.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        SpectrumLayoutGrid.Margin = radio ? new Thickness(0) : new Thickness(6, 0, 0, 0);
        TrackDetailsRow.Height = new GridLength(radio ? 30 : 36);
        TrackKey.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        PreviousControl.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        NextControl.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        AlbumName.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        ElapsedText.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        DurationText.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        TrackProgress.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        RadioLiveLabel.Visibility = radio ? Visibility.Visible : Visibility.Collapsed;
        RadioConsoleHeader.Visibility = radio ? Visibility.Visible : Visibility.Collapsed;
        RadioSpectrumTint.Visibility = radio ? Visibility.Visible : Visibility.Collapsed;
        PlaylistPullButton.Content = radio ? "CLICK TO PULL RADIO LIST" : "CLICK TO PULL SONG LIST";
        PlaylistPullButton.ToolTip = radio ? "Open radio channels" : "Open the music library";
        PlaylistTracksPanel.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        RadioChannelPanel.Visibility = radio ? Visibility.Visible : Visibility.Collapsed;
        Spectrum.Margin = radio ? new Thickness(0, 22, 0, 0) : new Thickness(0);
        _archived.View.Margin = Spectrum.Margin;
        EqControls.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        RemixDemoButton.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        RadioToggleButton.Tag = radio ? "Active" : null;
        RadioToggleButton.ToolTip = radio ? "Return to local music" : "Play Chillsynth FM · Nightride FM";
        BrandTagline.Text = radio ? "  •  ON AIR" : "  •  STEREO FM 84";
        ShowRadioConcept(radio);
    }
}
