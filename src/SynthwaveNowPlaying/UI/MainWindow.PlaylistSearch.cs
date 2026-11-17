using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private readonly List<PlaylistRow> _allPlaylistRows = [];
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private int _searchAnimationVersion;

    private void PlaylistSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchDelay is null) return;
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private void PlaylistSearchClear_Click(object sender, RoutedEventArgs e)
    {
        PlaylistSearch.Clear();
        PlaylistSearch.Focus();
    }

    private void FilterPlaylistRows()
    {
        _searchDelay.Stop();
        EndSort(true);
        var source = PlaylistSourcePicker.SelectedItem as PlaylistSource;
        if (source is null) return;
        var query = PlaylistSearch.Text.Trim();
        var matches = _allPlaylistRows.Where(row => row.Source == source &&
            (query.Length == 0 ||
             row.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             row.Artist.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             Path.GetFileNameWithoutExtension(row.File).Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();

        PlaylistCount.Text = query.Length == 0 ? $"{source.Files.Count} TRACKS" : $"{matches.Length} / {source.Files.Count}";
        if (_playlistRows.SequenceEqual(matches)) return;
        _playlistRows.Clear();
        foreach (var row in matches) _playlistRows.Add(row);
        if (matches.Length > 0) PlaylistItems.ScrollIntoView(matches[0]);
        var version = ++_searchAnimationVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (version != _searchAnimationVersion || !PlaylistPopup.IsOpen) return;
            AnimateSearchResults();
        }));
    }

    private void AnimateSearchResults()
    {
        var count = Math.Min(_playlistRows.Count, 10);
        for (var i = 0; i < count; i++)
        {
            if (PlaylistItems.ItemContainerGenerator.ContainerFromItem(_playlistRows[i]) is not ListBoxItem item) continue;
            item.BeginAnimation(OpacityProperty, null);
            var offset = new TranslateTransform(0, 7);
            item.RenderTransform = offset;
            var delay = TimeSpan.FromMilliseconds(i * 19);
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (_, _) =>
            {
                if (ReferenceEquals(item.RenderTransform, offset)) item.RenderTransform = Transform.Identity;
            };
            item.BeginAnimation(OpacityProperty, fade);
            offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }
}
