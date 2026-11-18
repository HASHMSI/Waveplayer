using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using NAudio.Wave;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private TrayIcon? _tray;
    private bool _allowExit;
    private volatile bool _widgetVisible = true;

    internal void InitializeTray()
    {
        var menu = new ContextMenu { MinWidth = 240, HasDropShadow = true, StaysOpen = false };
        menu.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
              <Border Background="#FF0B0B10" BorderBrush="#FF604165" BorderThickness="1" CornerRadius="12" Padding="7">
                <ItemsPresenter/>
              </Border>
            </ControlTemplate>
            """);
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(ForegroundProperty, Brushes.WhiteSmoke));
        style.Setters.Add(new Setter(FontSizeProperty, 12d));
        style.Setters.Add(new Setter(Control.TemplateProperty, XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="MenuItem">
              <Border x:Name="Frame" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" CornerRadius="6" Padding="11,8" Background="Transparent">
                <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="25"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                  <ContentPresenter ContentSource="Icon" VerticalAlignment="Center"/>
                  <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center"/>
                </Grid>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Frame" Property="Background" Value="#FF302039"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".55"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """)));
        menu.Resources.Add(typeof(MenuItem), style);
        MenuItem Item(string title, string icon, Action action)
        {
            var item = new MenuItem { Header = title, Icon = new TextBlock { Text = icon, Foreground = Brushes.HotPink } };
            item.Click += (_, _) => action(); menu.Items.Add(item); return item;
        }
        var heading = new MenuItem { Header = "WavePlayer", IsEnabled = false, Foreground = Brushes.HotPink };
        menu.Items.Add(heading);
        var show = Item("Hide widget", "◈", () => { if (IsVisible) Hide(); else RestoreFromTray(); });
        var play = Item("Play", "▶", () => PlayPauseButton_Click(this, new RoutedEventArgs()));
        Item("Previous track", "⏮", () => PreviousButton_Click(this, new RoutedEventArgs()));
        Item("Next track", "⏭", () => NextButton_Click(this, new RoutedEventArgs()));
        var remix = Item("Automix", "≈", () => RemixDemoButton_Click(this, new RoutedEventArgs()));
        var overlay = Item("Automix popup · On", "◉", () =>
        {
            _settings.AutomixOverlayEnabled = !_settings.AutomixOverlayEnabled;
            _settings.Save();
            if (!_settings.AutomixOverlayEnabled) DismissMixOverlay();
        });
        var preview = Item("Preview Automix popup", "▷", PreviewMixOverlay);
        var pin = Item("Pin widget", "◇", () => PinButton_Click(this, new RoutedEventArgs()));
        Item("Settings", "⚙", () => { RestoreFromTray(); if (IsEnabled) SettingsButton_Click(this, new RoutedEventArgs()); });
        menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(60, 43, 65)), Height = 1, Margin = new Thickness(10, 5, 10, 5) });
        Item("Exit WavePlayer", "×", ExitApplication);
        try
        {
            _tray = new TrayIcon(menu, () =>
            {
                heading.Header = _localTrack is null ? "WavePlayer" : "WavePlayer · " + TrackTitle.Text[..Math.Min(35, TrackTitle.Text.Length)];
                show.Header = IsVisible ? "Hide widget" : "Show widget";
                play.Header = _waveOut?.PlaybackState == PlaybackState.Playing ? "Pause" : "Play";
                remix.Header = "Automix · " + (_settings.RemixDemoEnabled ? "On" : "Off");
                overlay.Header = "Automix popup · " + (_settings.AutomixOverlayEnabled ? "On" : "Off");
                pin.Header = _pinned ? "Unpin widget" : "Pin widget";
                foreach (var item in menu.Items.OfType<MenuItem>()) item.IsEnabled = IsEnabled;
                heading.IsEnabled = false;
                preview.IsEnabled = IsEnabled && _incomingReader is null;
            }, RestoreFromTray);
            Closing += TrayClosing;
            Closed += (_, _) => _tray?.Dispose();
            Application.Current.SessionEnding += (_, _) => _allowExit = true;
            CloseControl.ToolTip = "Hide to notification area · use tray menu to exit";
            IsVisibleChanged += (_, _) =>
            {
                _widgetVisible = IsVisible;
                if (!IsVisible)
                {
                    PlaylistPopup.IsOpen = false;
                    CompositionTarget.Rendering -= OnCompositionRendering;
                    Interlocked.Exchange(ref _visualizationActive, 0);
                    SynthBackground.MotionEnabled = false;
                }
                else if (_spectrumThreadRunning && SpectrumHasSignal) NotifyAudioSignal();
            };
        }
        catch { _tray?.Dispose(); _tray = null; }
    }

    private void TrayClosing(object? sender, CancelEventArgs e)
    {
        if (_allowExit || _tray?.Registered != true) return;
        e.Cancel = true; Hide();
    }

    internal void ExitApplication()
    {
        DismissMixOverlay(immediately: true);
        _allowExit = true; _tray?.Dispose(); Close(); Application.Current?.Shutdown();
    }

    internal void RestoreFromTray()
    {
        Show(); WindowState = WindowState.Normal; Activate();
        foreach (Window owned in OwnedWindows) if (owned.IsVisible) owned.Activate();
    }
}
