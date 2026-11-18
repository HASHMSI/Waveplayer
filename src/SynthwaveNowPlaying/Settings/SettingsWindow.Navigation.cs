using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace SynthwaveNowPlaying;

internal sealed partial class SettingsWindow
{
    private readonly IReadOnlyDictionary<string, BitmapSource?> _themePreviews;
    private readonly Dictionary<string, FrameworkElement> _sectionCards = new();
    private readonly Dictionary<string, Button> _sectionButtons = new();
    private string _activeSection = "";

    private UIElement BuildSectionLayout(Style scrollBarStyle)
    {
        var layout = new Grid { Margin = new Thickness(1, 1, 1, 0) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(174) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rail = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Brush("#FF121027"),
            BorderBrush = Brush("#FF39305A"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 14, 10, 12),
            Margin = new Thickness(0, 0, 12, 0)
        };
        var railStack = new StackPanel();
        railStack.Children.Add(new TextBlock
        {
            Text = "SETTINGS  /  SECTIONS", FontSize = 8, FontWeight = FontWeights.Bold,
            Foreground = Cyan, Margin = new Thickness(7, 0, 0, 14)
        });
        rail.Child = railStack;
        layout.Children.Add(rail);

        var pageHost = new Grid();
        AddSection("library", "01", "LIBRARY", "Music and playlists", BuildMusicCard(), pageHost, railStack);
        AddSection("appearance", "02", "NIGHT DRIVE", "Themes and backdrop", BuildAppearancePage(), pageHost, railStack);
        AddSection("spectrum", "03", "SPECTRUM LAB", "Live visualizer comparison", BuildSpectrumCard(_settings.Sensitivity), pageHost, railStack);
        AddSection("player", "04", "PLAYER", "Volume, EQ and Automix", BuildPlayerCard(_settings), pageHost, railStack);
        AddSection("edit", "05", "EDIT MODE", "Widget layout", BuildEditModeCard(), pageHost, railStack);
        AddSection("bpm", "06", "BPM SCAN", "Analyze this playlist", BuildBpmScanCard(scrollBarStyle), pageHost, railStack);

        railStack.Children.Add(new Border
        {
            Height = 1, Background = Brush("#FF493365"), Margin = new Thickness(7, 15, 7, 13)
        });
        railStack.Children.Add(new TextBlock
        {
            Text = "WavePlayer  •  LOCAL PLAYER", FontSize = 7,
            Foreground = Muted, Margin = new Thickness(7, 0, 0, 0)
        });
        railStack.Children.Add(new TextBlock
        {
            Text = "Create by MSIHASH", FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = Cyan, Margin = new Thickness(7, 8, 0, 0)
        });

        var pageFrame = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Brush("#A80D1025"),
            BorderBrush = Brush("#FF3C315E"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 16, 11, 14)
        };
        var scroll = new ScrollViewer
        {
            Content = pageHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 5, 0)
        };
        scroll.Resources.Add(typeof(ScrollBar), scrollBarStyle);
        pageFrame.Child = scroll;
        Grid.SetColumn(pageFrame, 1);
        layout.Children.Add(pageFrame);
        ShowSection("appearance", animate: false);
        return layout;
    }

    private void AddSection(string key, string number, string title, string subtitle,
        FrameworkElement card, Grid pageHost, StackPanel rail)
    {
        card.Visibility = Visibility.Collapsed;
        card.VerticalAlignment = VerticalAlignment.Top;
        card.Margin = new Thickness(0);
        pageHost.Children.Add(card);
        _sectionCards[key] = card;

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = $"{number}  /  {title}", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Ink });
        labels.Children.Add(new TextBlock { Text = subtitle, FontSize = 8, Foreground = Muted, Margin = new Thickness(0, 3, 0, 0) });
        var button = new Button
        {
            Content = labels,
            Template = ButtonTemplate(),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Height = 54,
            Padding = new Thickness(10, 5, 8, 5),
            Margin = new Thickness(0, 0, 0, 5),
            Foreground = Ink,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => ShowSection(key);
        button.MouseEnter += (_, _) => { if (_activeSection != key) button.Background = Brush("#FF2B2047"); };
        button.MouseLeave += (_, _) => { if (_activeSection != key) button.Background = Brush("#FF18152C"); };
        rail.Children.Add(button);
        _sectionButtons[key] = button;
    }

    private void ShowSection(string key, bool animate = true)
    {
        if (_activeSection == key) return;
        if (key != "appearance") foreach (var popup in _themeHoverPopups) popup.IsOpen = false;
        _activeSection = key;
        foreach (var (section, card) in _sectionCards)
        {
            card.BeginAnimation(OpacityProperty, null);
            card.Visibility = section == key ? Visibility.Visible : Visibility.Collapsed;
            if (section == key)
            {
                card.Opacity = animate ? 0 : 1;
                if (animate) card.BeginAnimation(OpacityProperty, Animation(1, 210));
            }
        }
        foreach (var (section, button) in _sectionButtons)
        {
            var selected = section == key;
            button.Background = Brush(selected ? "#FF392051" : "#FF18152C");
            button.BorderBrush = Brush(selected ? "#FFFF78D7" : "#FF332A4B");
        }
    }
}
