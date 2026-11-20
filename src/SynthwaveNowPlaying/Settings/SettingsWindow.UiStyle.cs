using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WavePlayer.ThemePreview;

namespace SynthwaveNowPlaying;

internal sealed partial class SettingsWindow
{
    private string _uiStyleChoice = "NeonDrive";

    private Border BuildUiStyleCard()
    {
        _uiStyleChoice = _settings.UiStyle;
        var body = new StackPanel();
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var current = new TextBlock
        {
            Text = UiStyleLabel(_uiStyleChoice), Foreground = Ink, FontSize = 12,
            FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(current);
        var change = ActionButton("CHANGE UI STYLE", primary: true);
        change.Click += (_, _) =>
        {
            var chooser = new ThemeWindow(_uiStyleChoice == "Modern80s") { Owner = this };
            if (chooser.ShowDialog() != true) return;
            _uiStyleChoice = chooser.SelectedStyle;
            current.Text = UiStyleLabel(_uiStyleChoice);
        };
        Grid.SetColumn(change, 1);
        row.Children.Add(change);
        body.Children.Add(row);
        body.Children.Add(new TextBlock
        {
            Text = "Choose the original neon player or the newer Modern 80s layout. Your music and playback settings stay the same.",
            Foreground = Muted, FontSize = 9, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        var card = Card("00  /  INTERFACE", "The shape of your player", body);
        card.Margin = new Thickness(0, 0, 0, 15);
        return card;
    }

    private static string UiStyleLabel(string style) =>
        style == "Modern80s" ? "80s / MODERN" : "80s / NEON DRIVE";
}
