using System.IO;
using System.Windows.Threading;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".aiff", ".aif", ".wma", ".m4a", ".aac", ".flac" };
    private static readonly HashSet<string> PlaylistExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".m3u", ".m3u8", ".pls" };
    private readonly List<FileSystemWatcher> _musicWatchers = [];
    private readonly HashSet<string> _changedMusicFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _musicRefreshDelay = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private bool _musicRefreshRunning;
    private bool _musicWatchersNeedRestart;
    private int _musicRefreshVersion;

    private sealed record LibrarySnapshot(string[] Folders, string[] Tracks, (string Path, string[] Tracks)[] Playlists);

    private static LibrarySnapshot DiscoverMusicFolders(IReadOnlyList<string> musicFolders)
    {
        var folders = musicFolders.Where(Directory.Exists).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var files = WatchRoots(folders).SelectMany(folder => Directory.EnumerateFiles(folder, "*", new EnumerationOptions
            { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var tracks = files.Where(file => AudioExtensions.Contains(Path.GetExtension(file)))
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var playlists = files.Where(file => PlaylistExtensions.Contains(Path.GetExtension(file)))
            .OrderBy(file => file, StringComparer.CurrentCultureIgnoreCase)
            .Select(file => (Path: file, Tracks: ReadPlaylist(file, AudioExtensions).Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .Where(playlist => playlist.Tracks.Length > 0).ToArray();
        return new LibrarySnapshot(folders, tracks, playlists);
    }

    private void ConfigureMusicWatchers(IReadOnlyList<string> musicFolders)
    {
        foreach (var watcher in _musicWatchers) watcher.Dispose();
        _musicWatchers.Clear();
        foreach (var folder in WatchRoots(musicFolders.Where(Directory.Exists).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 32768
                };
                watcher.Created += MusicFileChanged;
                watcher.Changed += MusicFileChanged;
                watcher.Deleted += MusicFileChanged;
                watcher.Renamed += (_, e) => QueueMusicRefresh(e.FullPath);
                watcher.Error += (_, _) => QueueMusicRefresh(null);
                watcher.EnableRaisingEvents = true;
                _musicWatchers.Add(watcher);
            }
            catch (IOException) { /* A temporarily unavailable folder is scanned again when settings reopen. */ }
            catch (UnauthorizedAccessException) { }
        }
        _musicRefreshDelay.Tick -= MusicRefreshDelay_Tick;
        _musicRefreshDelay.Tick += MusicRefreshDelay_Tick;
    }

    private static IEnumerable<string> WatchRoots(string[] folders) => folders.Where(folder =>
        !folders.Any(other => !other.Equals(folder, StringComparison.OrdinalIgnoreCase) &&
            folder.StartsWith(other.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)));

    private void MusicFileChanged(object sender, FileSystemEventArgs e)
    {
        // Create/delete can refer to a whole folder moved into the library.
        if (e.ChangeType == WatcherChangeTypes.Changed && !AudioExtensions.Contains(Path.GetExtension(e.FullPath)) &&
            !PlaylistExtensions.Contains(Path.GetExtension(e.FullPath))) return;
        QueueMusicRefresh(e.FullPath);
    }

    private void QueueMusicRefresh(string? path)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (path is null) _musicWatchersNeedRestart = true;
            if (path is not null && AudioExtensions.Contains(Path.GetExtension(path))) _changedMusicFiles.Add(path);
            _musicRefreshVersion++;
            _musicRefreshDelay.Stop();
            _musicRefreshDelay.Start();
        }));
    }

    private async void MusicRefreshDelay_Tick(object? sender, EventArgs e)
    {
        _musicRefreshDelay.Stop();
        if (_musicRefreshRunning) return;
        _musicRefreshRunning = true;
        var version = _musicRefreshVersion;
        var folders = _settings.GetMusicFolders().ToArray();
        try
        {
            // Directory traversal stays off the render thread, including large libraries.
            var scan = await Task.Run(() => DiscoverMusicFolders(folders));
            if (version != _musicRefreshVersion || Dispatcher.HasShutdownStarted) return;
            foreach (var file in _changedMusicFiles)
            {
                _trackMetadataCache.Remove(file);
                _trackTitleCache.Remove(file);
            }
            _changedMusicFiles.Clear();
            ApplyMusicScan(scan);
            if (_musicWatchersNeedRestart)
            {
                _musicWatchersNeedRestart = false;
                ConfigureMusicWatchers(folders);
            }
        }
        catch (Exception ex) { ToolTip = $"Music library refresh failed: {ex.Message}"; }
        finally
        {
            _musicRefreshRunning = false;
            if (version != _musicRefreshVersion && !Dispatcher.HasShutdownStarted) _musicRefreshDelay.Start();
        }
    }

    private void StopMusicWatchers()
    {
        _musicRefreshDelay.Stop();
        foreach (var watcher in _musicWatchers) watcher.Dispose();
        _musicWatchers.Clear();
    }
}
