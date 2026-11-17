using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private readonly Dictionary<string, FrameworkElement> _editTargets = new();
    private readonly Dictionary<string, UiPlacement> _editPositions = new();
    private Dictionary<string, UiPlacement> _editSnapshot = new();
    private FrameworkElement? _selectedEditTarget;
    private bool _editMode;
    private bool _editDragging;
    private bool _editResizing;
    private Point _editLastPoint;

    private void InitializeEditMode()
    {
        AddEditable("brand", BrandBlock);
        AddEditable("status", SourceStatus);
        AddEditable("settings", SettingsControl);
        AddEditable("eq", EqButton);
        AddEditable("pin", PinButton);
        AddEditable("close", CloseControl);
        AddEditable("cover", AlbumArtFrame);
        AddEditable("visualizer", SpectrumFrame);
        AddEditable("trackInfo", TrackInfoBlock);
        AddEditable("previous", PreviousControl);
        AddEditable("playPause", PlayPauseButton);
        AddEditable("next", NextControl);
        AddEditable("album", AlbumName);
        AddEditable("seek", TrackProgress);
        AddEditable("elapsed", ElapsedText);
        AddEditable("duration", DurationText);

        foreach (var (key, position) in _settings.WidgetLayout ?? new Dictionary<string, UiPlacement>())
            if (_editTargets.ContainsKey(key) && IsValid(position)) SetPlacement(key, position.Copy());

        DesignSurface.PreviewMouseLeftButtonDown += EditSurface_MouseDown;
        DesignSurface.PreviewMouseMove += EditSurface_MouseMove;
        DesignSurface.PreviewMouseLeftButtonUp += EditSurface_MouseUp;
        DesignSurface.LostMouseCapture += (_, _) => { _editDragging = false; _editResizing = false; };
    }

    private void AddEditable(string key, FrameworkElement element)
    {
        // Render transforms keep the original XAML layout intact and allow layout reset.
        _editTargets[key] = element;
        _editPositions[key] = new UiPlacement();
        element.RenderTransformOrigin = new Point(.5, .5);
        element.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection { new ScaleTransform(1, 1), new TranslateTransform() }
        };
    }

    private static bool IsValid(UiPlacement position) =>
        double.IsFinite(position.X) && double.IsFinite(position.Y) && double.IsFinite(position.Scale) &&
        Math.Abs(position.X) <= 500 && Math.Abs(position.Y) <= 200 && position.Scale is >= .5 and <= 3;

    private void SetPlacement(string key, UiPlacement position)
    {
        _editPositions[key] = position;
        if (_editTargets[key].RenderTransform is not TransformGroup transform) return;
        ((ScaleTransform)transform.Children[0]).ScaleX = position.Scale;
        ((ScaleTransform)transform.Children[0]).ScaleY = position.Scale;
        ((TranslateTransform)transform.Children[1]).X = position.X;
        ((TranslateTransform)transform.Children[1]).Y = position.Y;
        UpdateEditSelection();
    }

    private void EnterEditMode()
    {
        if (_editMode) return;
        FinishCoverVolumeDrag();
        AlbumVolume.StopAnimation();
        AlbumArtFrame.Cursor = Cursors.SizeAll;
        PlaylistPopup.IsOpen = false;
        _editSnapshot = _editPositions.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
        _editMode = true;
        PlaylistPullButton.IsEnabled = false;
        EditOverlay.Visibility = Visibility.Visible;
        EditToolbar.IsOpen = true;
        SelectEditTarget(AlbumArtFrame);
    }

    private void ExitEditMode()
    {
        AlbumArtFrame.Cursor = Cursors.SizeNS;
        _editMode = false;
        _editDragging = false;
        _editResizing = false;
        DesignSurface.ReleaseMouseCapture();
        _selectedEditTarget = null;
        EditSelection.Visibility = Visibility.Collapsed;
        EditResizeHandle.Visibility = Visibility.Collapsed;
        EditOverlay.Visibility = Visibility.Collapsed;
        EditToolbar.IsOpen = false;
        PlaylistPullButton.IsEnabled = true;
    }

    private void EditSurface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode) return;
        if (IsWithin(e.OriginalSource as DependencyObject, EditResizeHandle))
        {
            if (_selectedEditTarget is null) return;
            _editResizing = true;
        }
        else
        {
            var target = FindEditTarget(e.OriginalSource as DependencyObject);
            if (target is null) return;
            SelectEditTarget(target);
            _editDragging = true;
        }
        _editLastPoint = e.GetPosition(DesignSurface);
        DesignSurface.CaptureMouse();
        e.Handled = true;
    }

    private void EditSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_editMode || _selectedEditTarget is null || (!_editDragging && !_editResizing)) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EditSurface_MouseUp(sender, e); return; }
        var point = e.GetPosition(DesignSurface);
        var delta = point - _editLastPoint;
        _editLastPoint = point;
        var key = _editTargets.First(pair => ReferenceEquals(pair.Value, _selectedEditTarget)).Key;
        var placement = _editPositions[key].Copy();
        if (_editResizing)
            placement.Scale = Math.Clamp(placement.Scale + (delta.X + delta.Y) / 90, .5, 3);
        else
        {
            placement.X = Math.Clamp(placement.X + delta.X, -350, 350);
            placement.Y = Math.Clamp(placement.Y + delta.Y, -110, 110);
        }
        SetPlacement(key, placement);
        e.Handled = true;
    }

    private void EditSurface_MouseUp(object sender, MouseEventArgs e)
    {
        if (!_editMode || (!_editDragging && !_editResizing)) return;
        _editDragging = false;
        _editResizing = false;
        DesignSurface.ReleaseMouseCapture();
        e.Handled = true;
    }

    private FrameworkElement? FindEditTarget(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, DesignSurface))
        {
            if (source is FrameworkElement element && _editTargets.Values.Contains(element)) return element;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject target)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, target)) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void SelectEditTarget(FrameworkElement target)
    {
        _selectedEditTarget = target;
        EditSelection.Visibility = Visibility.Visible;
        EditResizeHandle.Visibility = Visibility.Visible;
        UpdateEditSelection();
    }

    private void UpdateEditSelection()
    {
        if (!_editMode || _selectedEditTarget is null || !DesignSurface.IsLoaded) return;
        try
        {
            var topLeft = _selectedEditTarget.TransformToAncestor(DesignSurface).Transform(new Point());
            var bottomRight = _selectedEditTarget.TransformToAncestor(DesignSurface)
                .Transform(new Point(_selectedEditTarget.ActualWidth, _selectedEditTarget.ActualHeight));
            var width = Math.Abs(bottomRight.X - topLeft.X);
            var height = Math.Abs(bottomRight.Y - topLeft.Y);
            if (width < 2 || height < 2) return;
            EditSelection.Width = width + 4;
            EditSelection.Height = height + 4;
            Canvas.SetLeft(EditSelection, Math.Min(topLeft.X, bottomRight.X) - 2);
            Canvas.SetTop(EditSelection, Math.Min(topLeft.Y, bottomRight.Y) - 2);
            Canvas.SetLeft(EditResizeHandle, Math.Max(topLeft.X, bottomRight.X) - 4);
            Canvas.SetTop(EditResizeHandle, Math.Max(topLeft.Y, bottomRight.Y) - 4);
        }
        catch (InvalidOperationException) { }
    }

    private void EditSave_Click(object sender, RoutedEventArgs e)
    {
        _settings.WidgetLayout = _editPositions.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
        _settings.Save();
        ExitEditMode();
    }

    private void EditCancel_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (key, position) in _editSnapshot) SetPlacement(key, position.Copy());
        ExitEditMode();
    }

    private void EditReset_Click(object sender, RoutedEventArgs e)
    {
        foreach (var key in _editTargets.Keys) SetPlacement(key, new UiPlacement());
        UpdateEditSelection();
    }
}
