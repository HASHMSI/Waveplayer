using System.IO;
using System.Windows;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private bool _groupingPlaylist;

    private async void PlaylistGenreSortButton_Click(object sender, RoutedEventArgs e) => await GroupPlaylistAsync(byArtist: false);
    private async void PlaylistArtistSortButton_Click(object sender, RoutedEventArgs e) => await GroupPlaylistAsync(byArtist: true);

    private async Task GroupPlaylistAsync(bool byArtist)
    {
        if (_groupingPlaylist || PlaylistSourcePicker.SelectedItem is not PlaylistSource source) return;
        EndSort(false);
        var original = source.Files.ToArray();
        if (original.Length < 2) return;
        _groupingPlaylist = true;
        PlaylistGenreSortButton.IsEnabled = false;
        PlaylistArtistSortButton.IsEnabled = false;
        PlaylistCount.Text = "GROUPING…";
        try
        {
            // Read tags off the UI thread. Missing tags form a group at the end.
            var keys = await Task.Run(() => original.Select(file => ReadGroupKey(file, byArtist)).ToArray());
            if (!ReferenceEquals(PlaylistSourcePicker.SelectedItem, source) || !source.Files.SequenceEqual(original)) return;
            var order = original.Select((file, index) => (File: file, Index: index, Key: keys[index]))
                .OrderBy(item => item.Key.Length == 0 ? 1 : 0)
                .ThenBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Index)
                .Select(item => item.File).ToArray();
            source.Files.Clear();
            source.Files.AddRange(order);
            var rows = _allPlaylistRows.ToDictionary(row => row.File, StringComparer.OrdinalIgnoreCase);
            _allPlaylistRows.Clear();
            foreach (var file in order) if (rows.TryGetValue(file, out var row)) _allPlaylistRows.Add(row);
            SavePlaylistEdits(source);
            FilterPlaylistRows();
            PlaylistGenreSortButton.Tag = byArtist ? null : "Active";
            PlaylistArtistSortButton.Tag = byArtist ? "Active" : null;
        }
        catch (Exception ex) { ToolTip = $"Could not group playlist: {ex.Message}"; }
        finally
        {
            _groupingPlaylist = false;
            PlaylistGenreSortButton.IsEnabled = true;
            PlaylistArtistSortButton.IsEnabled = true;
            if (PlaylistCount.Text == "GROUPING…") FilterPlaylistRows();
        }
    }

    private static string ReadGroupKey(string file, bool byArtist)
    {
        try
        {
            using var tagged = TagLib.File.Create(file);
            return (byArtist ? tagged.Tag.FirstPerformer : tagged.Tag.FirstGenre)?.Trim() ?? "";
        }
        catch (IOException) { return ""; }
        catch (Exception) { return ""; }
    }
}
