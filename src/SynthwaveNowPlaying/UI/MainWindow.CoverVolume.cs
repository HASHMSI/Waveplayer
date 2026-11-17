using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;

namespace SynthwaveNowPlaying;

public partial class MainWindow : Window
{
    private void AlbumArtContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var content = (Grid)sender;
        double radius = Math.Max(0, AlbumArtFrame.CornerRadius.TopLeft - AlbumArtFrame.BorderThickness.Left);
        radius = Math.Min(radius, Math.Min(e.NewSize.Width, e.NewSize.Height) / 2);
        content.Clip = new RectangleGeometry(new Rect(e.NewSize), radius, radius);
    }

    private bool _coverVolumeDragging;
    private double _lastCoverY;
    private double _coverVerticalMotion;

    private void AlbumArtFrame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editMode) return;
        e.Handled = true; // Prevent the shell's window-drag handler from taking this gesture.
        if (!AlbumArtFrame.CaptureMouse()) return;
        _coverVolumeDragging = true;
        _lastCoverY = e.GetPosition(AlbumArtFrame).Y;
        _coverVerticalMotion = 0;
    }

    private void AlbumArtFrame_MouseMove(object sender, MouseEventArgs e)
    {
        if (_editMode || !_coverVolumeDragging) return;
        if (e.LeftButton != MouseButtonState.Pressed) { FinishCoverVolumeDrag(); return; }
        var y = e.GetPosition(AlbumArtFrame).Y;
        _coverVerticalMotion += y - _lastCoverY;
        _lastCoverY = y;
        e.Handled = true;
        if (AlbumArtFrame.ActualHeight < 1 || Math.Abs(_coverVerticalMotion) < .8) return;

        // Local cover coordinates preserve sensitivity when Edit Mode scales the cover.
        var next = Math.Clamp(_settings.Volume - _coverVerticalMotion / AlbumArtFrame.ActualHeight, 0, 1);
        _coverVerticalMotion = 0;
        if (Math.Abs(next - _settings.Volume) < .001)
        {
            AlbumVolume.ShowVolume(_settings.Volume);
            return;
        }
        _settings.Volume = next;
        ApplyPlaybackVolume();
        AlbumVolume.ShowVolume(next);
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void AlbumArtFrame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_coverVolumeDragging) return;
        e.Handled = true;
        FinishCoverVolumeDrag();
    }

    private void AlbumArtFrame_LostMouseCapture(object sender, MouseEventArgs e) => FinishCoverVolumeDrag();

    private void FinishCoverVolumeDrag()
    {
        _coverVolumeDragging = false;
        _coverVerticalMotion = 0;
        if (AlbumArtFrame.IsMouseCaptured) AlbumArtFrame.ReleaseMouseCapture();
    }
}
