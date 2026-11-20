using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private bool IsModernUi => _settings.UiStyle == "Modern80s";
    private bool _classicChromeCaptured;
    private Thickness _classicSpectrumBorder;
    private Thickness _classicSpectrumPadding;
    private Brush? _classicSpectrumBackground;
    private Style? _classicSeekStyle;
    private Brush? _classicPullForeground;
    private Brush? _classicPlaylistBackground;
    private Brush? _classicPlaylistBorder;
    private Effect? _classicPlaylistEffect;
    private UIElement[] _classicDrawerChildren = [];
    private RowDefinition[] _classicDrawerRows = [];

    // The two styles use the same audio, album cover, spectrum and seek control.
    // Keep their original parents so switching skins never restarts playback.
    private void CaptureClassicChrome()
    {
        if (_classicChromeCaptured) return;
        _classicChromeCaptured = true;
        _classicSpectrumBorder = SpectrumFrame.BorderThickness;
        _classicSpectrumPadding = SpectrumFrame.Padding;
        _classicSpectrumBackground = SpectrumFrame.Background;
        _classicSeekStyle = TrackProgress.Style;
        _classicPullForeground = PlaylistPullButton.Foreground;
        _classicPlaylistBackground = PlaylistPanel.Background;
        _classicPlaylistBorder = PlaylistPanel.BorderBrush;
        _classicPlaylistEffect = PlaylistPanel.Effect;
        _classicDrawerChildren = RadioChannelPanel.Children.Cast<UIElement>().ToArray();
        _classicDrawerRows = RadioChannelPanel.RowDefinitions.Cast<RowDefinition>().ToArray();
    }

    private void ApplyUiStyle()
    {
        if (IsModernUi) ShowRadioConcept(_radioActive);
        else RestoreClassicChrome();
    }

    private void RestoreClassicChrome()
    {
        if (_radioConcept is null) { BasePlayerChrome.Visibility = Visibility.Visible; return; }
        if (!ReferenceEquals(SpectrumFrame.Parent, SpectrumLayoutGrid))
        {
            if (SpectrumFrame.Parent is Panel spectrumParent) spectrumParent.Children.Remove(SpectrumFrame);
            SpectrumLayoutGrid.Children.Insert(0, SpectrumFrame);
        }
        Grid.SetRow(SpectrumFrame, 0);
        SpectrumFrame.BorderThickness = _classicSpectrumBorder;
        SpectrumFrame.Padding = _classicSpectrumPadding;
        SpectrumFrame.Background = _classicSpectrumBackground;

        if (!ReferenceEquals(AlbumArtFrame.Parent, MediaContentGrid))
        {
            if (AlbumArtFrame.Parent is Panel coverParent) coverParent.Children.Remove(AlbumArtFrame);
            MediaContentGrid.Children.Insert(0, AlbumArtFrame);
        }
        Grid.SetRow(AlbumArtFrame, 1);
        Grid.SetColumn(AlbumArtFrame, 0);

        if (!ReferenceEquals(TrackProgress.Parent, PlayerFooter))
        {
            if (TrackProgress.Parent is Panel seekParent) seekParent.Children.Remove(TrackProgress);
            PlayerFooter.Children.Add(TrackProgress);
        }
        TrackProgress.Style = _classicSeekStyle;

        _radioConcept.Visibility = Visibility.Collapsed;
        BasePlayerChrome.Visibility = Visibility.Visible;
        SynthBackground.Visibility = Visibility.Visible;
        SynthBackground.MotionEnabled = SpectrumHasSignal;
        PlaylistBackground.MotionEnabled = PlaylistPopup.IsOpen && SpectrumHasSignal;
        PlaylistBackground.Opacity = .85;
        PlaylistPullButton.SetResourceReference(Control.BackgroundProperty, "AlbumPrimaryBrush");
        PlaylistPullButton.Foreground = _classicPullForeground;
        PlaylistPanel.Background = _classicPlaylistBackground;
        PlaylistPanel.BorderBrush = _classicPlaylistBorder;
        PlaylistPanel.Effect = _classicPlaylistEffect;

        if (!_modernDrawerReady) return;
        RadioChannelPanel.Children.Clear();
        RadioChannelPanel.RowDefinitions.Clear();
        foreach (var row in _classicDrawerRows) RadioChannelPanel.RowDefinitions.Add(row);
        foreach (var child in _classicDrawerChildren) RadioChannelPanel.Children.Add(child);
        _modernDrawerReady = false;
    }
}
