using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SynthwaveNowPlaying.UI;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private RadioConceptView? _radioConcept;
    private WrapPanel? _channelCards;
    private TextBox? _channelSearch;
    private bool _favoritesOnly;

    private void ShowRadioConcept(bool show)
    {
        if (_radioConcept is null)
        {
            _radioConcept = new RadioConceptView { Visibility = Visibility.Collapsed };
            Panel.SetZIndex(_radioConcept, 11);
            DesignSurface.Children.Add(_radioConcept);
            _radioConcept.Local += () => RadioToggleButton_Click(RadioToggleButton, new RoutedEventArgs());
            _radioConcept.Settings += () => SettingsButton_Click(SettingsControl, new RoutedEventArgs());
            _radioConcept.Pin += () => PinButton_Click(PinButton, new RoutedEventArgs());
            _radioConcept.Hide += () => CloseButton_Click(CloseControl, new RoutedEventArgs());
            _radioConcept.Play += () => PlayPauseButton_Click(PlayPauseButton, new RoutedEventArgs());
            _radioConcept.Previous += () => { if (_radioActive) StepRadioChannel(-1); else PreviousButton_Click(PreviousControl,new RoutedEventArgs()); };
            _radioConcept.Next += () => { if (_radioActive) StepRadioChannel(1); else NextButton_Click(NextControl,new RoutedEventArgs()); };
            _radioConcept.FavoriteRequested += () => ToggleRadioFavorite(_radioStation);
            _radioConcept.EqRequested += () => { EqButton_Click(EqButton,new RoutedEventArgs()); _radioConcept.EqReset.Visibility=Visibility.Visible; };
            _radioConcept.EqResetRequested += () => EqResetButton_Click(EqResetButton,new RoutedEventArgs());
            _radioConcept.RemixRequested += () => { RemixDemoButton_Click(RemixDemoButton,new RoutedEventArgs()); RefreshLocalConcept(); };
            BuildRadioDrawer();
        }
        if (!ReferenceEquals(SpectrumFrame.Parent, _radioConcept.SpectrumHost))
        {
            if (SpectrumFrame.Parent is Panel oldParent) oldParent.Children.Remove(SpectrumFrame);
            _radioConcept.SpectrumHost.Children.Add(SpectrumFrame);
        }
        if (!ReferenceEquals(AlbumArtFrame.Parent, _radioConcept.CoverHost))
        {
            if (AlbumArtFrame.Parent is Panel coverParent) coverParent.Children.Remove(AlbumArtFrame);
            _radioConcept.CoverHost.Children.Add(AlbumArtFrame);
            Grid.SetRow(AlbumArtFrame,0);
        }
        if (!ReferenceEquals(TrackProgress.Parent, _radioConcept.SeekSliderHost))
        {
            if (TrackProgress.Parent is Panel seekParent) seekParent.Children.Remove(TrackProgress);
            _radioConcept.SeekSliderHost.Children.Add(TrackProgress);
            TrackProgress.Style=(Style)_radioConcept.FindResource("ConceptSeek");
        }
        Grid.SetRow(SpectrumFrame,0);
        SpectrumFrame.BorderThickness=new Thickness(0);
        SpectrumFrame.Padding=new Thickness(0);
        SpectrumFrame.Background=Brushes.Transparent;
        RadioSpectrumTint.Visibility=RadioConsoleHeader.Visibility=Visibility.Collapsed;
        Spectrum.Margin=new Thickness(2,show?36:42,2,0);
        _archived.View.Margin=Spectrum.Margin;
        _radioConcept.SetMode(show);
        BasePlayerChrome.Visibility=Visibility.Collapsed;
        SynthBackground.Visibility=Visibility.Collapsed;
        SynthBackground.MotionEnabled=false;
        PlaylistBackground.MotionEnabled=false;
        _radioConcept.Visibility=Visibility.Visible;
        PlaylistPullButton.Background=new SolidColorBrush(Color.FromRgb(46,43,78));
        PlaylistPullButton.Foreground=new SolidColorBrush(Color.FromRgb(220,211,244));
        PlaylistPullButton.Content=show?"RADIO CHANNELS":"PLAYLIST";
        PlaylistBackground.Opacity=0;
        PlaylistPanel.Background=new LinearGradientBrush(Color.FromRgb(29,29,53),Color.FromRgb(17,23,40),90);
        PlaylistPanel.BorderBrush=new SolidColorBrush(Color.FromRgb(109,151,170));
        PlaylistPanel.Effect=null;
        if (show)
        {
            UpdateRadioConceptStation();
        }
        else
        {
            AlbumArtFrame.Visibility=TrackProgress.Visibility=Visibility.Visible;
            RefreshLocalConcept();
        }
    }

    private void RefreshLocalConcept()
    {
        if (_radioConcept is null || _radioActive) return;
        _radioConcept.Station.Text=TrackTitle.Text;
        _radioConcept.Metadata.Text=ArtistName.Text;
        _radioConcept.PlayGlyph.Text=_waveOut?.PlaybackState==NAudio.Wave.PlaybackState.Playing?"Ⅱ":"▶";
        _radioConcept.Elapsed.Text=ElapsedText.Text;
        _radioConcept.RemixLabel.Text=_settings.RemixDemoEnabled?"MIX ON":"REMIX";
        _radioConcept.EqReset.Visibility=_settings.EqualizerEnabled?Visibility.Visible:Visibility.Collapsed;
    }

    private void UpdateRadioConceptStation()
    {
        var index = Array.FindIndex(RadioStations, s => s.Id == _radioStation);
        _radioConcept?.Tune(RadioStations[Math.Max(0,index)].Name,index);
        _radioConcept?.SetFavorite(_settings.RadioFavorites.Contains(_radioStation));
        _radioConcept?.UpdateTrack("Connecting to Nightride FM…");
        RenderRadioCards();
    }

    private void StepRadioChannel(int step)
    {
        var index = Array.FindIndex(RadioStations,s => s.Id == _radioStation);
        SelectRadioChannel(RadioStations[(index+step+RadioStations.Length)%RadioStations.Length].Id);
    }

    private void SelectRadioChannel(string id)
    {
        RadioChannel_Click(new Button { Tag=id },new RoutedEventArgs());
    }

    private void ToggleRadioFavorite(string id)
    {
        if (!_settings.RadioFavorites.Remove(id)) _settings.RadioFavorites.Add(id);
        _settings.Save();
        _radioConcept?.SetFavorite(_settings.RadioFavorites.Contains(_radioStation));
        RenderRadioCards();
    }

    private void BuildRadioDrawer()
    {
        RadioChannelPanel.Children.Clear();
        RadioChannelPanel.RowDefinitions.Clear();
        RadioChannelPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        RadioChannelPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        RadioChannelPanel.RowDefinitions.Add(new RowDefinition());
        var heading = new DockPanel { Margin = new Thickness(3,0,3,5) };
        var close = new Button { Content="⌄", Width=23,Height=22,Style=(Style)FindResource("NeonButton"),Padding=new Thickness(0),ToolTip="Close channel list" };
        close.Click += (_,_) => ClosePlaylistAnimated(); DockPanel.SetDock(close,Dock.Right); heading.Children.Add(close);
        var favorites = new Button { Content="☆ FAVORITES",FontSize=7,Width=86,Height=22,Margin=new Thickness(0,0,7,0),Style=(Style)FindResource("NeonButton"),Padding=new Thickness(0) };
        favorites.Click += (_,_) => { _favoritesOnly=!_favoritesOnly; favorites.Content=_favoritesOnly?"★ FAVORITES":"☆ FAVORITES"; RenderRadioCards(); };
        DockPanel.SetDock(favorites,Dock.Right); heading.Children.Add(favorites);
        heading.Children.Add(new TextBlock { Text="CHANNELS",Foreground=Brushes.White,FontSize=13,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center });
        RadioChannelPanel.Children.Add(heading);
        var searchBorder=new Border { CornerRadius=new CornerRadius(7),Background=new SolidColorBrush(Color.FromArgb(135,39,38,67)),BorderBrush=new SolidColorBrush(Color.FromRgb(88,86,126)),BorderThickness=new Thickness(.7),Padding=new Thickness(10,4,10,4),Margin=new Thickness(2,1,2,5) };
        _channelSearch=new TextBox { FontSize=9,Foreground=Brushes.White,Background=Brushes.Transparent,BorderThickness=new Thickness(0),CaretBrush=Brushes.White,ToolTip="Search channels or mood" };
        System.Windows.Automation.AutomationProperties.SetName(_channelSearch,"Search radio channels");
        _channelSearch.TextChanged+=(_,_)=>RenderRadioCards();
        var searchGrid = new Grid();
        var placeholder=new TextBlock { Text="⌕  Search channels or mood…",FontSize=9,Foreground=new SolidColorBrush(Color.FromRgb(166,162,194)),IsHitTestVisible=false };
        _channelSearch.TextChanged+=(_,_)=>placeholder.Visibility=_channelSearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        searchGrid.Children.Add(placeholder); searchGrid.Children.Add(_channelSearch); searchBorder.Child=searchGrid;
        Grid.SetRow(searchBorder,1); RadioChannelPanel.Children.Add(searchBorder);
        _channelCards=new WrapPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(1,3,1,0) };
        var scroll=new ScrollViewer { Content=_channelCards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),FindResource("NeonScrollBar"));
        Grid.SetRow(scroll,2); RadioChannelPanel.Children.Add(scroll);
        RenderRadioCards();
    }

    private void RenderRadioCards()
    {
        if (_channelCards is null) return;
        _channelCards.Children.Clear();
        var query=_channelSearch?.Text.Trim()??"";
        var stations=RadioStations.Where(s=>(!_favoritesOnly||_settings.RadioFavorites.Contains(s.Id)) &&
            (s.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||s.Genre.Contains(query,StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(s=>_settings.RadioFavorites.Contains(s.Id));
        var count=0;
        foreach(var station in stations)
        {
            bool active=station.Id==_radioStation;
            var grid=new Grid { Margin=new Thickness(10,6,10,6) };
            grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
            grid.Children.Add(new TextBlock { Text=station.Name.ToUpperInvariant(),Foreground=Brushes.White,FontSize=10,FontWeight=FontWeights.Bold,Margin=new Thickness(0,2,25,0) });
            var mood=new TextBlock { Text=station.Genre,Foreground=new SolidColorBrush(Color.FromRgb(170,173,202)),FontSize=7,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,4,0,0) };
            Grid.SetRow(mood,1); grid.Children.Add(mood);
            var star=new Button { Content=_settings.RadioFavorites.Contains(station.Id)?"★":"☆",Width=23,Height=23,FontSize=15,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=new SolidColorBrush(Color.FromRgb(255,166,181)),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Cursor=Cursors.Hand,ToolTip="Favorite "+station.Name };
            star.Click+=(_,e)=>{e.Handled=true;ToggleRadioFavorite(station.Id);}; grid.Children.Add(star);
            var card=new Border { Width=174,Height=55,Margin=new Thickness(2,3,5,3),CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(active?1.1:.6),BorderBrush=new SolidColorBrush(active?Color.FromRgb(141,232,221):Color.FromRgb(87,83,125)),Background=new SolidColorBrush(active?Color.FromRgb(36,62,76):Color.FromRgb(29,29,53)),Child=grid,Cursor=Cursors.Hand,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new TranslateTransform() };
            card.MouseLeftButtonUp+=(_,e)=>{if(e.Handled)return;e.Handled=true;SelectRadioChannel(station.Id);};
            card.MouseEnter+=(_,_)=>card.BeginAnimation(OpacityProperty,new DoubleAnimation(.78,TimeSpan.FromMilliseconds(120)));
            card.MouseLeave+=(_,_)=>card.BeginAnimation(OpacityProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(190)));
            card.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(240)) { BeginTime=TimeSpan.FromMilliseconds(count*22) });
            ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(7,0,TimeSpan.FromMilliseconds(300)) { BeginTime=TimeSpan.FromMilliseconds(count*22),EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } });
            _channelCards.Children.Add(card); count++;
        }
        if(count==0)_channelCards.Children.Add(new TextBlock {Text="NO CHANNELS FOUND",Foreground=Brushes.LightSteelBlue,FontSize=10,Margin=new Thickness(10,20,0,0)});
    }
}
