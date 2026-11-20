using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;

namespace SynthwaveNowPlaying;

public partial class MainWindow : Window
{
    private string? _activeQueueSourceId;
    private bool _playlistEditAnimating;

    // Playlist metadata loads in batches so opening the panel does not block the UI.
    private void ScanMusicFolders(IReadOnlyList<string> musicFolders)
    {
        try
        {
            _musicRefreshVersion++;
            _musicRefreshDelay.Stop();
            _changedMusicFiles.Clear();
            ApplyMusicScan(DiscoverMusicFolders(musicFolders));
            ConfigureMusicWatchers(musicFolders);
        }
        catch (Exception ex)
        {
            SourceStatus.Text = "FOLDER SCAN FAILED";
            ToolTip = ex.Message;
        }
    }

    private void ApplyMusicScan(LibrarySnapshot scan)
    {
        var selectedId = (PlaylistSourcePicker.SelectedItem as PlaylistSource)?.Id;
        _loadingLibrary = true;
        try
        {
            _playlistMetadataCts?.Cancel();
            _playlistMetadataCts?.Dispose();
            _playlistMetadataCts = null;
            EndSort(false);
            _library.Clear();
            _library.AddRange(scan.Tracks);
            var currentFiles = scan.Tracks.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in _trackMetadataCache.Keys.Where(file => !currentFiles.Contains(file)).ToArray()) _trackMetadataCache.Remove(file);
            foreach (var file in _trackTitleCache.Keys.Where(file => !currentFiles.Contains(file)).ToArray()) _trackTitleCache.Remove(file);
            _playlistSources.Clear();
            _playlistRows.Clear();
            _allPlaylistRows.Clear();
            if (scan.Folders.Length > 0)
                _playlistSources.Add(CreatePlaylistSource($"all:{scan.Folders[0]}", "ALL TRACKS", _library));
            foreach (var playlist in scan.Playlists)
                _playlistSources.Add(CreatePlaylistSource($"playlist:{playlist.Path}", Path.GetFileNameWithoutExtension(playlist.Path), playlist.Tracks));
            var selected = _playlistSources.FirstOrDefault(source => source.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                ?? _playlistSources.FirstOrDefault();
            PlaylistSourcePicker.SelectedItem = selected;
            PlaylistSourcePicker.Visibility = _playlistSources.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            PlaylistSourceRow.Height = _playlistSources.Count > 1 ? new GridLength(27) : new GridLength(0);
            PlaylistPullButton.IsEnabled = true;
            if (_localTrack is null || _waveOut is null) { _activeQueue = []; _activeQueueSourceId = null; }
            else if (_activeQueueSourceId is not null)
            {
                var queueSource = _playlistSources.FirstOrDefault(source => source.Id.Equals(_activeQueueSourceId, StringComparison.OrdinalIgnoreCase));
                if (queueSource is not null) _activeQueue = queueSource.Files.ToList();
            }
            if (_localTrack is null || _waveOut is null)
                SourceStatus.Text = _library.Count == 0 ? "NO AUDIO FILES FOUND" : $"LIBRARY · {_library.Count}";
            PlaylistGenreSortButton.Tag = null;
            PlaylistArtistSortButton.Tag = null;
            if (selected is not null) BuildPlaylistRows(selected);
            else PlaylistCount.Text = "0 TRACKS";
        }
        finally { _loadingLibrary = false; }
    }

    private PlaylistSource CreatePlaylistSource(string id, string label, IEnumerable<string> files)
    {
        _settings.PlaylistEdits ??= new(StringComparer.OrdinalIgnoreCase);
        var discovered = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!_settings.PlaylistEdits.TryGetValue(id, out var edits)) return new PlaylistSource(id, label, discovered);
        var removed = new HashSet<string>(edits.Removed ?? [], StringComparer.OrdinalIgnoreCase);
        var ordered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in edits.Order ?? []) if (!ordered.ContainsKey(file)) ordered[file] = ordered.Count;
        var visible = discovered.Where(file => !removed.Contains(file))
            .OrderBy(file => ordered.TryGetValue(file, out var index) ? index : int.MaxValue).ToList();
        return new PlaylistSource(id, label, visible);
    }

    private void PlaylistSourcePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingLibrary) return;
        if (PlaylistSourcePicker.SelectedItem is PlaylistSource source) BuildPlaylistRows(source);
    }

    private void PlaylistArtwork_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement artwork && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            artwork.Clip = new RectangleGeometry(new Rect(e.NewSize), 5, 5);
    }

    private void PlaylistPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement panel && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            panel.Clip = new RectangleGeometry(new Rect(e.NewSize), 15, 15);
    }

    private void PlaylistContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement content && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            content.Clip = new RectangleGeometry(new Rect(e.NewSize), 14, 14);
    }

    private void BuildPlaylistRows(PlaylistSource source)
    {
        EndSort(false);
        // Browsing a different list only changes the displayed rows. Playback keeps
        // the queue selected when the listener clicked a track.
        _playlistRows.Clear();
        _allPlaylistRows.Clear();
        for (var index = 0; index < source.Files.Count; index++)
        {
            var file = source.Files[index];
            var row = new PlaylistRow(file, source, _trackTitleCache.TryGetValue(file, out var title) ? title : Path.GetFileNameWithoutExtension(file));
            if (_trackMetadataCache.TryGetValue(file, out var metadata)) row.Apply(metadata);
            if (_analysisCache.TryGet(file, out var analysis)) row.ApplyKey(analysis);
            row.IsCurrent = string.Equals(file, _localTrack, StringComparison.OrdinalIgnoreCase);
            _allPlaylistRows.Add(row);
        }
        FilterPlaylistRows();
        if (PlaylistPopup.IsOpen) _ = LoadPlaylistMetadataAsync();
    }

    private void PlaylistPullButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistPopup.IsOpen) ClosePlaylistAnimated();
        else PlaylistPopup.IsOpen = true;
    }

    private void PlaylistCollapseButton_Click(object sender, RoutedEventArgs e) => ClosePlaylistAnimated();

    private void ClosePlaylistAnimated()
    {
        if (!PlaylistPopup.IsOpen || _playlistClosing) return;
        _playlistClosing = true;
        var duration = TimeSpan.FromMilliseconds(175);
        var fade = new DoubleAnimation(PlaylistPanel.Opacity, 0, duration)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            if (_playlistClosing) PlaylistPopup.IsOpen = false;
        };
        PlaylistPanel.BeginAnimation(OpacityProperty, fade);
        _playlistPanelTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(_playlistPanelTransform.Y, 10, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
    }

    private void PlaylistTrackButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PlaylistRow row }) return;
        _activeQueue = row.Queue.ToList();
        _activeQueueSourceId = row.Source.Id;
        var index = _activeQueue.FindIndex(file => string.Equals(file, row.File, StringComparison.OrdinalIgnoreCase));
        PlayLibraryTrack(index);
        ClosePlaylistAnimated();
    }

    private void PlaylistRemove_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_playlistEditAnimating || sender is not Button { Tag: PlaylistRow row }) return;
        var container = PlaylistItems.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem;
        void Complete()
        {
            if (container is not null)
            {
                // Recycling can reuse this item container for another song.
                container.BeginAnimation(OpacityProperty, null);
                container.Opacity = 1;
                container.RenderTransform = Transform.Identity;
            }
            var source = row.Source;
            source.Files.RemoveAll(file => string.Equals(file, row.File, StringComparison.OrdinalIgnoreCase));
            _allPlaylistRows.Remove(row);
            _playlistRows.Remove(row);
            var edits = GetPlaylistEdits(source.Id);
            if (!edits.Removed.Contains(row.File, StringComparer.OrdinalIgnoreCase)) edits.Removed.Add(row.File);
            FilterPlaylistRows();
            source.RefreshName();
            SavePlaylistEdits(source);
            _playlistEditAnimating = false;
        }

        if (container is null) { Complete(); return; }
        _playlistEditAnimating = true;
        container.RenderTransform = new TranslateTransform();
        var duration = TimeSpan.FromMilliseconds(190);
        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Complete();
        container.BeginAnimation(OpacityProperty, fade);
        ((TranslateTransform)container.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, 18, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
    }

    private PlaylistEdits GetPlaylistEdits(string id)
    {
        _settings.PlaylistEdits ??= new(StringComparer.OrdinalIgnoreCase);
        if (!_settings.PlaylistEdits.TryGetValue(id, out var edits))
            _settings.PlaylistEdits[id] = edits = new PlaylistEdits();
        return edits;
    }

    private void SavePlaylistEdits(PlaylistSource source)
    {
        PlaylistGenreSortButton.Tag = null;
        PlaylistArtistSortButton.Tag = null;
        GetPlaylistEdits(source.Id).Order = source.Files.ToList();
        if (string.Equals(_activeQueueSourceId, source.Id, StringComparison.OrdinalIgnoreCase))
        {
            _activeQueue = source.Files.ToList();
            if (AdaptiveWaiting) CancelDemoCrossfade();
            var currentIndex = _activeQueue.FindIndex(file => string.Equals(file, _localTrack, StringComparison.OrdinalIgnoreCase));
            if (_settings.RemixDemoEnabled && currentIndex >= 0) WarmTransitionAnalysis(currentIndex);
        }
        try { _settings.Save(); }
        catch (Exception ex) { ToolTip = $"Playlist changes could not be saved: {ex.Message}"; }
    }

    private void PlaylistPopup_Opened(object? sender, EventArgs e)
    {
        _playlistClosing = false;
        PlaylistPanel.BeginAnimation(OpacityProperty, null);
        PlaylistPanel.Opacity = 1;
        _playlistPanelTransform.BeginAnimation(TranslateTransform.YProperty, null);
        _playlistPanelTransform.Y = 0;
        ResizePlaylistPanel();
        PlaylistBackground.Theme = _settings.Background;
        PlaylistBackground.ApplyAlbumPalette(_displayPalette.Primary, _displayPalette.Secondary, TimeSpan.Zero);
        PlaylistBackground.MotionEnabled = SpectrumHasSignal && _radioConcept is null;
        PlaylistBackground.InvalidateVisual();
        SetPullArrow("⌄");
        PlaylistPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _playlistPanelTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (!_radioActive) _ = LoadPlaylistMetadataAsync();
    }

    private void PlaylistPopup_Closed(object? sender, EventArgs e)
    {
        EndSort(false);
        _playlistClosing = false;
        _searchDelay.Stop();
        _playlistMetadataCts?.Cancel();
        _playlistMetadataCts?.Dispose();
        _playlistMetadataCts = null;
        PlaylistPanel.BeginAnimation(OpacityProperty, null);
        PlaylistPanel.Opacity = 1;
        _playlistPanelTransform.BeginAnimation(TranslateTransform.YProperty, null);
        _playlistPanelTransform.Y = 0;
        PlaylistBackground.MotionEnabled = false;
        SetPullArrow("⌃");
    }

    private void ResizePlaylistPanel()
    {
        if (!IsInitialized || ActualHeight <= 0 || ActualWidth <= 0) return;
        var scale = ActualHeight / 153d;
        var barHeight = 14 * scale;
        PlaylistBarRow.Height = new GridLength(barHeight);
        PlaylistPullButton.Height = barHeight;
        PlaylistPullButton.FontSize = 6.5 * scale;
        PlaylistPanel.Width = Math.Max(390, ActualWidth);
        PlaylistPanel.Height = Math.Max(220, (ActualHeight - barHeight) * (276d / 139d));
    }

    private void SetPullArrow(string text)
    {
        if (PlaylistPullButton.Template.FindName("PullArrow", PlaylistPullButton) is TextBlock arrow) arrow.Text = text;
    }

    private async Task LoadPlaylistMetadataAsync()
    {
        // A new scan or selection cancels stale work; each small batch returns to the UI thread.
        if (_allPlaylistRows.Count == 0) return;
        _playlistMetadataCts?.Cancel();
        _playlistMetadataCts?.Dispose();
        var cts = new CancellationTokenSource();
        _playlistMetadataCts = cts;
        var token = cts.Token;
        var pendingFiles = _allPlaylistRows.Select(row => row.File).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(file => !_trackMetadataCache.ContainsKey(file)).ToArray();
        if (pendingFiles.Length == 0) return;
        try
        {
            for (var offset = 0; offset < pendingFiles.Length; offset += 12)
            {
                var batch = pendingFiles.Skip(offset).Take(12).ToArray();
                var results = await Task.Run(() =>
                {
                    var read = new List<(string File, TrackMetadata Metadata)>(batch.Length);
                    foreach (var file in batch)
                    {
                        token.ThrowIfCancellationRequested();
                        read.Add((file, ReadTrackMetadata(file)));
                    }
                    return read;
                }, token);
                if (token.IsCancellationRequested || !PlaylistPopup.IsOpen) return;
                foreach (var (file, metadata) in results)
                {
                    _trackMetadataCache[file] = metadata;
                    _trackTitleCache[file] = metadata.Title;
                    foreach (var row in _allPlaylistRows.Where(row => string.Equals(row.File, file, StringComparison.OrdinalIgnoreCase))) row.Apply(metadata);
                    if (PlaylistSearch.Text.Length > 0) _searchDelay.Start();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ToolTip = $"Some track details could not be loaded: {ex.Message}"; }
    }

    private static TrackMetadata ReadTrackMetadata(string file)
    {
        try
        {
            using var tagged = TagLib.File.Create(file);
            var tag = tagged.Tag;
            var picture = tag.Pictures.FirstOrDefault();
            var imageBytes = picture?.Data.Data;
            var artwork = imageBytes is { Length: > 0 and <= 2_000_000 } ? ImageFromBytes(imageBytes, 160) : ReadFolderArtwork(file);
            return new TrackMetadata(
                string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(file) : tag.Title,
                string.IsNullOrWhiteSpace(tag.FirstPerformer) ? "LOCAL LIBRARY" : tag.FirstPerformer,
                tag.Album ?? "", tagged.Properties.Duration, artwork);
        }
        catch
        {
            return new TrackMetadata(Path.GetFileNameWithoutExtension(file), "LOCAL LIBRARY", "", TimeSpan.Zero, ReadFolderArtwork(file));
        }
    }

    private static BitmapImage? ReadFolderArtwork(string file)
    {
        var directory = Path.GetDirectoryName(file);
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var cover = new[] { "cover.jpg", "folder.jpg", "cover.png", "folder.png" }
            .Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
        if (cover is null) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 160;
            image.UriSource = new Uri(cover);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    private static IEnumerable<string> ReadPlaylist(string playlistFile, HashSet<string> supported)
    {
        string[]? lines = null;
        try { lines = File.ReadAllLines(playlistFile); } catch { }
        if (lines is null) yield break;
        var baseFolder = Path.GetDirectoryName(playlistFile) ?? "";
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var equal = line.IndexOf('=');
            if (equal > 0 && line[..equal].StartsWith("File", StringComparison.OrdinalIgnoreCase)) line = line[(equal + 1)..].Trim();
            if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile) line = uri.LocalPath;
            else if (!Path.IsPathRooted(line)) line = Path.GetFullPath(Path.Combine(baseFolder, line));
            if (supported.Contains(Path.GetExtension(line))) yield return line;
        }
    }

    private static BitmapImage ImageFromBytes(byte[] bytes, int decodeWidth = 0)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }

    private sealed class PlaylistSource(string id, string label, List<string> files) : INotifyPropertyChanged
    {
        public string Id { get; } = id;
        public List<string> Files { get; } = files;
        public string Name => $"{label}  ·  {Files.Count}";
        public event PropertyChangedEventHandler? PropertyChanged;
        public void RefreshName() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }
    private sealed record TrackMetadata(string Title, string Artist, string Album, TimeSpan Duration, BitmapSource? Artwork)
    {
        public string DurationText => Duration > TimeSpan.Zero ? FormatTime(Duration) : "—:—";
    }

    private sealed class PlaylistRow(string file, PlaylistSource source, string title) : INotifyPropertyChanged
    {
        private string _title = title;
        private string _artist = "LOCAL LIBRARY";
        private string _durationText = "—:—";
        private BitmapSource? _artwork;
        private bool _isCurrent;
        private bool _dragging;
        private string _keyText = "";
        public bool IsDragging { get => _dragging; set => Set(ref _dragging, value); }

        public string File { get; } = file;
        public PlaylistSource Source { get; } = source;
        public IReadOnlyList<string> Queue => Source.Files;
        public string Title { get => _title; private set => Set(ref _title, value); }
        public string Artist { get => _artist; private set => Set(ref _artist, value); }
        public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }
        public BitmapSource? Artwork { get => _artwork; private set => Set(ref _artwork, value); }
        public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
        public string KeyText { get => _keyText; private set => Set(ref _keyText, value); }
        public Visibility KeyVisibility => KeyText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        public string KeyDetails { get; private set; } = "";

        public void ApplyKey(TrackAnalysis analysis)
        {
            KeyText = analysis.KeyScanned ? analysis.Key.Display : "";
            KeyDetails = analysis.KeyScanned ? $"Estimated musical key: {analysis.Key.Display}" : "Key not scanned";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KeyVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KeyDetails)));
        }

        public void Apply(TrackMetadata metadata)
        {
            Title = metadata.Title;
            Artist = metadata.Artist;
            DurationText = metadata.DurationText;
            Artwork = metadata.Artwork;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
