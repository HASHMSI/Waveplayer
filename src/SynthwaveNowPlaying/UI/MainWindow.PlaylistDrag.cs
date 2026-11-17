using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private PlaylistRow? _dragRow;
    private ScrollViewer? _dragScroll;
    private int _dragStart, _dragIndex;
    private double _dragStartY, _dragScrollStart, _grabOffset, _rowPitch;
    private long _lastDragTick;

    private void SortGrip_Down(object sender, MouseButtonEventArgs e)
    {
        if (_playlistEditAnimating || _dragRow != null || sender is not Button { Tag: PlaylistRow row }) return;
        if (PlaylistItems.ItemContainerGenerator.ContainerFromItem(row) is not ListBoxItem item) return;
        DependencyObject? parent = item;
        while (parent != null && parent is not ScrollViewer)
            parent = VisualTreeHelper.GetParent(parent);
        if (parent is not ScrollViewer scroll || !PlaylistItems.CaptureMouse()) return;
        e.Handled = true;
        _dragRow = row;
        _dragScroll = scroll;
        _dragStart = _dragIndex = _playlistRows.IndexOf(row);
        _dragStartY = e.GetPosition(PlaylistItems).Y;
        _dragScrollStart = scroll.VerticalOffset;
        _grabOffset = e.GetPosition(item).Y;
        _rowPitch = item.ActualHeight + item.Margin.Top + item.Margin.Bottom;
        DragPreview.Content = row;
        DragPreview.Width = item.ActualWidth;
        DragPreview.Visibility = Visibility.Visible;
        Canvas.SetLeft(DragPreview, item.TranslatePoint(new Point(), PlaylistItems).X);
        Canvas.SetTop(DragPreview, _dragStartY - _grabOffset);
        row.IsDragging = true;
        _lastDragTick = Environment.TickCount64;
        CompositionTarget.Rendering += MoveSortPreview;
    }

    private void MoveSortPreview(object? sender, EventArgs e)
    {
        if (_dragRow == null || _dragScroll == null) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed) { EndSort(true); return; }
        var y = Mouse.GetPosition(PlaylistItems).Y;
        var height = PlaylistItems.ActualHeight;
        var tick = Environment.TickCount64;
        var elapsed = Math.Clamp(tick - _lastDragTick, 0, 50);
        _lastDragTick = tick;
        var speed = y < 20 ? -Math.Clamp((20-y)/20, 0, 1) : Math.Clamp((y-height+20)/20, 0, 1);
        if (speed != 0) _dragScroll.ScrollToVerticalOffset(_dragScroll.VerticalOffset + speed * elapsed * .52);
        var top = Math.Clamp(y - _grabOffset, 0, Math.Max(0, height - DragPreview.ActualHeight));
        Canvas.SetTop(DragPreview, top);
        var delta = y - _dragStartY + _dragScroll.VerticalOffset - _dragScrollStart;
        var target = Math.Clamp(_dragStart + (int)Math.Round(delta / _rowPitch), 0, _playlistRows.Count - 1);
        if (target == _dragIndex) return;
        // Record visible row positions before WPF rearranges the containers.
        var previous = new Dictionary<PlaylistRow, double>();
        foreach (var candidate in _playlistRows)
        {
            if (candidate == _dragRow) continue;
            if (PlaylistItems.ItemContainerGenerator.ContainerFromItem(candidate) is not ListBoxItem container) continue;
            container.BeginAnimation(RenderTransformProperty, null);
            container.RenderTransform = Transform.Identity;
            previous[candidate] = container.TranslatePoint(new Point(), PlaylistItems).Y;
        }
        _playlistRows.Move(_dragIndex, target);
        _dragIndex = target;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_dragRow is null) return;
            foreach (var (candidate, from) in previous)
            {
                if (PlaylistItems.ItemContainerGenerator.ContainerFromItem(candidate) is not ListBoxItem container) continue;
                if (!ReferenceEquals(container.DataContext, candidate)) continue;
                var distance = from - container.TranslatePoint(new Point(), PlaylistItems).Y;
                if (Math.Abs(distance) < 1) continue;
                var motion = new TranslateTransform(0, distance);
                container.RenderTransform = motion;
                motion.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(distance, 0, TimeSpan.FromMilliseconds(185))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }));
    }

    private void SortGrip_Up(object sender, MouseButtonEventArgs e)
    {
        if (_dragRow == null) return;
        e.Handled = true;
        EndSort(true);
    }

    private void SortCaptureLost(object sender, MouseEventArgs e)
    {
        if (!PlaylistItems.IsMouseCaptured) EndSort(false);
    }

    private void EndSort(bool commit)
    {
        if (_dragRow is not PlaylistRow row) return;
        _dragRow = null;
        CompositionTarget.Rendering -= MoveSortPreview;
        row.IsDragging = false;
        DragPreview.Visibility = Visibility.Collapsed;
        DragPreview.Content = null;
        _dragScroll = null;
        if (PlaylistItems.IsMouseCaptured) PlaylistItems.ReleaseMouseCapture();
        if (_dragStart == _dragIndex) return;
        if (commit)
        {
            row.Source.Files.Remove(row.File);
            // Filtering changes visible indices. Insert relative to the next
            // visible song while preserving the positions of hidden songs.
            var next = _dragIndex + 1 < _playlistRows.Count ? _playlistRows[_dragIndex + 1] : null;
            if (next is not null)
                row.Source.Files.Insert(row.Source.Files.IndexOf(next.File), row.File);
            else if (_dragIndex > 0)
                row.Source.Files.Insert(row.Source.Files.IndexOf(_playlistRows[_dragIndex - 1].File) + 1, row.File);
            else row.Source.Files.Insert(0, row.File);
            _allPlaylistRows.Remove(row);
            _allPlaylistRows.Insert(row.Source.Files.IndexOf(row.File), row);
            SavePlaylistEdits(row.Source);
        }
        else _playlistRows.Move(_dragIndex, _dragStart);
    }
}
