using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SynthwaveNowPlaying;

public partial class MainWindow : Window
{
    private readonly AudioSampleBuffer _audioSamples = new();
    private readonly SpectrumAnalyzer _analyzer;
    private readonly ArchivedVisualizer _archived;
    private volatile bool _classicActive;
    private int _classicDecayFrames;
    private bool SpectrumHasSignal => _classicActive ? _archived.HasRecentSignal() : _audioSamples.HasRecentSignal;
    private readonly AutoResetEvent _analysisSignal = new(false);
    private readonly object _spectrumLock = new();
    private readonly float[] _latestSpectrum = new float[56];
    private readonly float[] _displaySpectrum = new float[56];
    private readonly float[] _silenceSpectrum = new float[56];
    private Thread? _spectrumThread;
    private volatile bool _spectrumThreadRunning;
    private long _spectrumVersion;
    private long _displayedSpectrumVersion;
    private int _visualizationActive;
    private bool _loadingLibrary;
    private bool _playlistClosing;
    private bool _hasRenderedSpectrum;
    private bool _pinned = true;
    private bool _dockToBottomRight = true;
    private bool _windowReady;
    private readonly List<string> _library = [];
    private readonly Dictionary<string, string> _trackTitleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackMetadata> _trackMetadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<PlaylistSource> _playlistSources = [];
    private readonly ObservableCollection<PlaylistRow> _playlistRows = [];
    private CancellationTokenSource? _playlistMetadataCts;
    private readonly TranslateTransform _playlistPanelTransform = new();
    private List<string> _activeQueue = [];
    private string? _localTrack;
    private AlbumPalette _activePalette;
    private AlbumPalette _displayPalette = new(Color.FromRgb(255, 62, 189), Color.FromRgb(61, 220, 255));
    private AlbumPalette _fadeStartPalette;
    private DateTime _paletteFadeStarted;
    private readonly DispatcherTimer _paletteTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private bool _hasActivePalette;
    private readonly DispatcherTimer _localProgressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _settingsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _sizeSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly TrackAnalysisCache _analysisCache = new();
    private AudioFileReader? _audioReader;
    private WaveOut? _waveOut;
    private LocalEqualizer? _equalizer;
    private MixingSampleProvider? _localMixer;
    private ISampleProvider? _currentMixInput;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWidgetSize();
        DockToDesktopCorner();
        PlaylistPanel.RenderTransformOrigin = new Point(.5, .5);
        PlaylistPanel.RenderTransform = _playlistPanelTransform;
        InitializeEditMode();
        PlaylistItems.ItemsSource = _playlistRows;
        _searchDelay.Tick += (_, _) => FilterPlaylistRows();
        PlaylistSourcePicker.ItemsSource = _playlistSources;
        _analyzer = new SpectrumAnalyzer(_audioSamples);
        _archived = new ArchivedVisualizer(Spectrum_BandGainChanged);
        SpectrumLayers.Children.Add(_archived.View);
        Spectrum.BandGainChanged += Spectrum_BandGainChanged;
        _localProgressTimer.Tick += (_, _) => UpdateLocalProgress();
        _crossfadeTimer.Tick += (_, _) => UpdateDemoCrossfade();
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            _settings.Save();
        };
        _sizeSaveTimer.Tick += (_, _) =>
        {
            _sizeSaveTimer.Stop();
            SaveWidgetSize();
        };
        _paletteTimer.Tick += (_, _) => UpdatePaletteFrame();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        SourceInitialized += MainWindow_SourceInitialized;
        SizeChanged += (_, _) => { ResizePlaylistPanel(); UpdateEditSelection(); WidgetSizeChanged(); };
        _settings.Volume = double.IsFinite(_settings.Volume) ? Math.Clamp(_settings.Volume, 0, 1) : 1;
        AlbumVolume.SetIdleVolume(_settings.Volume);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _windowReady = true;
        DockToDesktopCorner();
        ApplySettings();
        ScanMusicFolders(_settings.GetMusicFolders());
        try
        {
            _spectrumThreadRunning = true;
            _spectrumThread = new Thread(SpectrumAnalysisLoop)
            {
                IsBackground = true,
                Name = "Waveplayer spectrum analysis",
                Priority = ThreadPriority.AboveNormal
            };
            _spectrumThread.Start();
            SourceStatus.Text = "LOCAL SPECTRUM · READY";
        }
        catch (Exception ex)
        {
            SourceStatus.Text = "SPECTRUM UNAVAILABLE";
            ToolTip = $"Spectrum analysis could not start: {ex.Message}";
        }
    }

    private void NotifyAudioSignal()
    {
        if (Interlocked.Exchange(ref _visualizationActive, 1) != 0 || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (!IsLoaded || _spectrumThreadRunning == false) return;
            SynthBackground.MotionEnabled = true;
            if (PlaylistPopup.IsOpen) PlaylistBackground.MotionEnabled = true;
            CompositionTarget.Rendering += OnCompositionRendering;
        }));
    }

    private void SpectrumAnalysisLoop()
    {
        // The capture callback only writes samples. FFT work stays off the WPF render thread.
        while (_spectrumThreadRunning)
        {
            _analysisSignal.WaitOne();
            if (!_spectrumThreadRunning) break;
            var classic = _classicActive;
            if (!(classic ? _archived.HasRecentSignal() : _audioSamples.HasRecentSignal)) continue;
            var values = classic ? _archived.Compute() : _analyzer.Compute();
            lock (_spectrumLock)
            {
                if (classic != _classicActive) continue;
                Array.Copy(values, _latestSpectrum, _latestSpectrum.Length);
                _spectrumVersion++;
            }
        }
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        // Consume only the newest completed spectrum and stop rendering after silence settles.
        var frameTime = ((RenderingEventArgs)e).RenderingTime;
        var active = SpectrumHasSignal;
        SynthBackground.MotionEnabled = active;
        if (PlaylistPopup.IsOpen) PlaylistBackground.MotionEnabled = active;
        if (active)
        {
            lock (_spectrumLock)
            {
                if (_displayedSpectrumVersion != _spectrumVersion)
                {
                    Array.Copy(_latestSpectrum, _displaySpectrum, _latestSpectrum.Length);
                    _displayedSpectrumVersion = _spectrumVersion;
                }
            }
            _classicDecayFrames = 0;
            if (_classicActive) _archived.SetSpectrum(_displaySpectrum);
            else Spectrum.SetSpectrum(_displaySpectrum, frameTime);
            _hasRenderedSpectrum = true;
            return;
        }

        // The original renderer has a fixed baseline. On large widgets it can
        // remain above its own idle threshold even after every bar has settled.
        if (_hasRenderedSpectrum && (_classicActive
                ? !_archived.IsSettled() && _classicDecayFrames++ < 90
                : !Spectrum.IsSettled))
        {
            if (_classicActive) _archived.SetSpectrum(_silenceSpectrum);
            else Spectrum.SetSpectrum(_silenceSpectrum, frameTime);
            return;
        }

        CompositionTarget.Rendering -= OnCompositionRendering;
        _hasRenderedSpectrum = false;
        Interlocked.Exchange(ref _visualizationActive, 0);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(KeepWidgetAspectRatio);
    }

    private static readonly double WidgetAspectRatio = 410d / 153d;
    private const int WmSizing = 0x0214;

    private void RestoreWidgetSize()
    {
        var workArea = SystemParameters.WorkArea;
        var maximumWidth = Math.Min(workArea.Width, workArea.Height * WidgetAspectRatio);
        var desiredWidth = _settings.WidgetWidth is double saved && double.IsFinite(saved) && saved >= MinWidth
            ? saved : 410d;
        Width = Math.Clamp(desiredWidth, MinWidth, Math.Max(MinWidth, maximumWidth));
        Height = Math.Max(MinHeight, Width / WidgetAspectRatio);
    }

    private void DockToDesktopCorner()
    {
        if (!_dockToBottomRight) return;
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        Left = workArea.Right - width;
        Top = workArea.Bottom - height;
    }

    private void WidgetSizeChanged()
    {
        // Debounce resize events so dragging the grip does not write settings every frame.
        if (!_windowReady || WindowState != WindowState.Normal) return;
        if (_pinned && _dockToBottomRight) DockToDesktopCorner();
        _sizeSaveTimer.Stop();
        _sizeSaveTimer.Start();
    }

    private void SaveWidgetSize()
    {
        if (!_windowReady || !double.IsFinite(ActualWidth) || ActualWidth < MinWidth) return;
        _settings.WidgetWidth = ActualWidth;
        _settings.Save();
    }

    private IntPtr KeepWidgetAspectRatio(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmSizing || lParam == IntPtr.Zero) return IntPtr.Zero;
        var sizing = Marshal.PtrToStructure<NativeRect>(lParam);
        var grip = wParam.ToInt32();
        var width = Math.Max(390, sizing.Right - sizing.Left);
        var height = Math.Max(146, sizing.Bottom - sizing.Top);

        if (grip is 3 or 6) // top or bottom edge: derive width from dragged height
        {
            width = (int)Math.Round(height * WidgetAspectRatio);
            sizing.Right = sizing.Left + width;
        }
        else
        {
            height = (int)Math.Round(width / WidgetAspectRatio);
            switch (grip)
            {
                case 4: // top-left, preserve the opposite corner
                    sizing.Top = sizing.Bottom - height;
                    break;
                case 5: // top-right
                    sizing.Top = sizing.Bottom - height;
                    break;
                default: // side or bottom edge/corner
                    sizing.Bottom = sizing.Top + height;
                    break;
            }
        }
        Marshal.StructureToPtr(sizing, lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        if (!_pinned) _dockToBottomRight = false;
        Topmost = _pinned;
        PinIcon.Opacity = _pinned ? 1 : .42;
        PinButton.Tag = _pinned ? "Active" : null;
        PinButton.ToolTip = _pinned ? "Pinned · click to unlock movement" : "Unpinned · drag to move";
        SourceStatus.ToolTip = _pinned ? "Pinned in place" : "Window can be moved";
    }

    private void SetPlayPauseState(bool playing)
    {
        PlayIcon.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        PlayPauseButton.Tag = playing ? "Active" : null;
        PlayPauseButton.ToolTip = playing ? "Pause" : "Play";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedPlaylist = PlaylistSourcePicker.SelectedItem as PlaylistSource ?? _playlistSources.FirstOrDefault();
        var scanTracks = selectedPlaylist?.Files.Select(file => new ScanTrack(file,
            _trackTitleCache.TryGetValue(file, out var title) ? title : Path.GetFileNameWithoutExtension(file))).ToArray() ?? [];
        var dialog = new SettingsWindow(_settings, (Style)FindResource("NeonScrollBar"), CaptureThemePreviews(),
            selectedPlaylist?.Name ?? "CURRENT PLAYLIST", scanTracks, _analysisCache, ApplyTrackAnalysis, ReadSpectrumPreview) { Owner = this };
        dialog.VisualizerPreviewChanged += SelectVisualizer;
        if (dialog.ShowDialog() == true) { _settings.Save(); ApplySettings(); ScanMusicFolders(_settings.GetMusicFolders()); }
        else SelectVisualizer(_settings.ClassicVisualizer);
        if (dialog.EditModeRequested) EnterEditMode();
    }

    private bool ReadSpectrumPreview(float[] destination)
    {
        if (_waveOut?.PlaybackState != PlaybackState.Playing) return false;
        if (SpectrumHasSignal)
        {
            lock (_spectrumLock) _latestSpectrum.CopyTo(destination, 0);
        }
        else Array.Clear(destination);
        return true;
    }

    private void SelectVisualizer(bool classic)
    {
        Spectrum.Visibility = classic ? Visibility.Collapsed : Visibility.Visible;
        _archived.View.Visibility = classic ? Visibility.Visible : Visibility.Collapsed;
        lock (_spectrumLock)
        {
            if (classic == _classicActive) return;
            _classicActive = classic;
            Array.Clear(_latestSpectrum);
            Array.Clear(_displaySpectrum);
            _spectrumVersion++;
        }
        _classicDecayFrames = 0;
        _hasRenderedSpectrum = true;
        if (_spectrumThreadRunning) NotifyAudioSignal();
    }

    private void SetSpectrumGain(int band, double gain)
    {
        Spectrum.SetBandGain(band, gain);
        _archived.SetBandGain(band, gain);
    }

    private void ApplySettings()
    {
        if (_settings.AutomixTiming == AutomixTiming.Classic && AdaptiveWaiting) CancelDemoCrossfade();
        SynthBackground.Theme = _settings.Background;
        PlaylistBackground.Theme = _settings.Background;
        SynthBackground.InvalidateVisual();
        if (_settings.Background == "Custom" || AlbumArtImage.Source is not BitmapSource artwork)
            ApplyUiPalette(ThemePalette());
        else
            ApplyUiPalette(ExtractAlbumPalette(artwork));
        try
        {
            if (!string.IsNullOrWhiteSpace(_settings.BackgroundImage) && File.Exists(_settings.BackgroundImage))
            {
                CustomBackgroundImage.Source = new BitmapImage(new Uri(_settings.BackgroundImage));
                CustomBackgroundImage.Visibility = Visibility.Visible;
            }
            else { CustomBackgroundImage.Source = null; CustomBackgroundImage.Visibility = Visibility.Collapsed; }
        }
        catch { CustomBackgroundImage.Source = null; CustomBackgroundImage.Visibility = Visibility.Collapsed; }
        SelectVisualizer(_settings.ClassicVisualizer);
        Spectrum.Sensitivity = Math.Clamp(_settings.Sensitivity, .4, 2.5);
        _archived.SetSensitivity(Spectrum.Sensitivity);
        Spectrum.IsEditing = false;
        _archived.SetEditing(false);
        EqButton.Content = "EQ";
        EqButton.Tag = null;
        for (var band = 0; band < Math.Min(6, _settings.Bands.Length); band++) SetSpectrumGain(band, _settings.EqualizerEnabled ? _settings.Bands[band] : 0);
        UpdateEqResetButton();
        if (_equalizer is not null) _equalizer.Enabled = _settings.EqualizerEnabled;
        if (_incomingEqualizer is not null) _incomingEqualizer.Enabled = _settings.EqualizerEnabled;
        ApplyPlaybackVolume();
        AlbumVolume.SetIdleVolume(_settings.Volume);
        UpdateRemixDemoButton();
    }

    private void Shell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editMode || _pinned) return;
        if (e.ButtonState == MouseButtonState.Pressed && !IsInteractiveControl(e.OriginalSource as DependencyObject))
        {
            var start = new Point(Left, Top);
            try { DragMove(); } catch (InvalidOperationException) { }
            if (Math.Abs(Left - start.X) + Math.Abs(Top - start.Y) > 1) _dockToBottomRight = false;
        }
    }

    private static bool IsInteractiveControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or Slider or Thumb) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _sizeSaveTimer.Stop();
        SaveWidgetSize();
        _paletteTimer.Stop();
        _localProgressTimer.Stop();
        _settingsSaveTimer.Stop();
        _playlistMetadataCts?.Cancel();
        _playlistMetadataCts?.Dispose();
        _settings.Save();
        CompositionTarget.Rendering -= OnCompositionRendering;
        AlbumVolume.StopAnimation();
        SynthBackground.MotionEnabled = false;
        StopLocalPlayback();
        _spectrumThreadRunning = false;
        _analysisSignal.Set();
        _spectrumThread?.Join(500);
        _analysisSignal.Dispose();
    }
}
