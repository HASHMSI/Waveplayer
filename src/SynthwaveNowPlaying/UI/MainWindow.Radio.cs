using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private const string RadioStreamUrl = "https://stream.nightride.fm/chillsynth.mp3";
    private static readonly HttpClient RadioClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private CancellationTokenSource? _radioCancel;
    private MediaFoundationReader? _radioReader;
    private WaveOut? _radioOut;
    private bool _radioActive;
    private int _radioGeneration;
    private int _radioLayoutGeneration;

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
            ApplyUiPalette(new AlbumPalette(Color.FromRgb(82, 244, 185), Color.FromRgb(73, 207, 249)));
            TrackTitle.Text = "Chillsynth FM";
            ArtistName.Text = "Nightride FM · live radio";
        }
        _radioCancel?.Cancel();
        _radioCancel?.Dispose();
        var generation = ++_radioGeneration;
        _radioCancel = new CancellationTokenSource();
        SourceStatus.Text = "RADIO · CONNECTING";
        SetPlayPauseState(false);
        _ = FollowRadioMetadataAsync(generation, _radioCancel.Token);
        MediaFoundationReader? reader = null;
        try
        {
            // Media Foundation resolves the live MP3 URL off the UI thread.
            reader = await Task.Run(() => new MediaFoundationReader(RadioStreamUrl));
            if (!_radioActive || generation != _radioGeneration) { reader.Dispose(); return; }
            var source = reader.ToSampleProvider();
            var volume = new VolumeSampleProvider(source) { Volume = (float)_settings.Volume };
            var output = new WaveOut { BufferMilliseconds = 80, NumberOfBuffers = 5 };
            output.Init(new SpectrumTap(volume, _audioSamples, _archived, () => _classicActive,
                _analysisSignal, NotifyAudioSignal));
            _radioReader = reader;
            _radioOut = output;
            reader = null;
            output.Play();
            SourceStatus.Text = "RADIO · ON AIR";
            SetPlayPauseState(true);
        }
        catch (Exception ex)
        {
            reader?.Dispose();
            if (generation != _radioGeneration) return;
            _radioOut?.Dispose();
            _radioOut = null;
            _radioReader?.Dispose();
            _radioReader = null;
            SourceStatus.Text = "RADIO UNAVAILABLE";
            ToolTip = ex.Message;
            SetPlayPauseState(false);
        }
    }

    private async Task FollowRadioMetadataAsync(int generation, CancellationToken token)
    {
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
                        if (entry.GetProperty("station").GetString() != "chillsynth") continue;
                        var title = entry.GetProperty("title").GetString();
                        var artist = entry.GetProperty("artist").GetString();
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (!_radioActive || generation != _radioGeneration) return;
                            TrackTitle.Text = title;
                            ArtistName.Text = string.IsNullOrWhiteSpace(artist) ? "Nightride FM" : artist;
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
        _radioActive = false;
        _radioGeneration++;
        _radioCancel?.Cancel();
        _radioCancel?.Dispose();
        _radioCancel = null;
        _radioOut?.Stop();
        _radioOut?.Dispose();
        _radioOut = null;
        _radioReader?.Dispose();
        _radioReader = null;
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

    private void SetRadioLayout(bool radio)
    {
        var generation = ++_radioLayoutGeneration;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.Completed += (_, _) =>
        {
            if (generation != _radioLayoutGeneration || !IsLoaded) return;
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
        RadioSpectrumBadge.Visibility = radio ? Visibility.Visible : Visibility.Collapsed;
        EqControls.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        RemixDemoButton.Visibility = radio ? Visibility.Collapsed : Visibility.Visible;
        RadioToggleButton.Tag = radio ? "Active" : null;
        RadioToggleButton.ToolTip = radio ? "Return to local music" : "Play Chillsynth FM · Nightride FM";
        BrandTagline.Text = radio ? "  •  ON AIR" : "  •  STEREO FM 84";
    }
}
