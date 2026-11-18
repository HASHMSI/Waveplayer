using Microsoft.Win32;
using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SynthwaveNowPlaying;

internal sealed partial class SettingsWindow : Window
{
    // Cards present library, appearance, spectrum, player, and edit-mode settings.
    public event Action<bool>? VisualizerPreviewChanged;
    private SpectrumLab _spectrumLab = null!;
    private RadioButton _classicTiming = null!;
    private Border BuildMusicCard()
    {
        var body = new StackPanel();
        _folderRows.Margin = new Thickness(0, 0, 0, 5);
        body.Children.Add(_folderRows);
        foreach (var folder in _settings.GetMusicFolders()) AddMusicFolderRow(folder);
        var add = ActionButton("+  ADD MUSIC FOLDER", primary: true);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += BrowseFolder;
        body.Children.Add(add);
        body.Children.Add(new TextBlock
        {
            Text = "Folders are scanned together, including their subfolders and playlists.",
            Foreground = Muted, FontSize = 9, Margin = new Thickness(1, 10, 0, 0)
        });
        return Card("01  /  LIBRARY", "Your music locations", body);
    }

    private Border BuildAppearanceCard()
    {
        var body = new StackPanel();
        var themes = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 16) };
        _themePreviews.TryGetValue("Neon Grid", out var neonPreview);
        _themePreviews.TryGetValue("Violet Dusk", out var violetPreview);
        _themePreviews.TryGetValue("Ocean Drive", out var oceanPreview);
        _neonTheme = ThemeChoice("NEON GRID", "MAGENTA / CYAN", Color.FromRgb(255, 62, 189), Color.FromRgb(61, 220, 255), _settings.Background == "Neon Grid", neonPreview);
        _violetTheme = ThemeChoice("VIOLET DUSK", "PLUM / AMBER", Color.FromRgb(218, 91, 239), Color.FromRgb(255, 177, 100), _settings.Background == "Violet Dusk", violetPreview);
        _oceanTheme = ThemeChoice("OCEAN DRIVE", "CYAN / BLUE", Color.FromRgb(76, 214, 255), Color.FromRgb(103, 126, 255), _settings.Background == "Ocean Drive", oceanPreview);
        themes.Children.Add(_neonTheme);
        themes.Children.Add(_violetTheme);
        themes.Children.Add(_oceanTheme);
        body.Children.Add(themes);

        body.Children.Add(new TextBlock
        {
            Text = "LIVE WIDGET SNAPSHOT  /  TILTED 3D PREVIEW",
            FontSize = 8, Foreground = Muted, Margin = new Thickness(2, 0, 0, 16)
        });

        var imageRow = new Grid();
        imageRow.ColumnDefinitions.Add(new ColumnDefinition());
        imageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _image.Text = _settings.BackgroundImage ?? "No custom background image";
        StyleBox(_image);
        _image.VerticalContentAlignment = VerticalAlignment.Center;
        _image.ToolTip = "Optional image layered over the animated synthwave background";
        imageRow.Children.Add(_image);
        var imageButton = ActionButton("Choose image");
        imageButton.Margin = new Thickness(8, 0, 0, 0);
        imageButton.Click += BrowseImage;
        Grid.SetColumn(imageButton, 1);
        imageRow.Children.Add(imageButton);
        body.Children.Add(imageRow);
        return Card("02  /  NIGHT DRIVE", "Choose the atmosphere of your player", body);
    }

    private Border BuildSpectrumCard(double sensitivity)
    {
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var caption = new TextBlock { Text = "Visualizer sensitivity", Foreground = Muted, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(caption);
        _sensitivityValue.Text = $"{(double.IsFinite(sensitivity) ? Math.Clamp(sensitivity, .4, 2.5) : 1):0.00}×";
        _sensitivityValue.Foreground = Cyan;
        _sensitivityValue.FontSize = 10;
        _sensitivityValue.HorizontalAlignment = HorizontalAlignment.Right;
        body.Children.Add(_sensitivityValue);

        _sensitivity = SliderControl(.4, 2.5, double.IsFinite(sensitivity) ? Math.Clamp(sensitivity, .4, 2.5) : 1);
        _spectrumLab = new SpectrumLab(_readSpectrumPreview, _settings.ClassicVisualizer, _sensitivity.Value);
        _spectrumLab.SelectionChanged += classic => VisualizerPreviewChanged?.Invoke(classic);
        _sensitivity.ValueChanged += (_, _) =>
        {
            _sensitivityValue.Text = $"{_sensitivity.Value:0.00}×";
            _spectrumLab.SetSensitivity(_sensitivity.Value);
        };
        Grid.SetRow(_sensitivity, 1);
        body.Children.Add(_sensitivity);
        var card = new StackPanel();
        card.Children.Add(_spectrumLab);
        card.Children.Add(body);
        return Card("03  /  SPECTRUM LAB", "Choose a visualizer", card);
    }

    private Border BuildPlayerCard(AppSettings settings)
    {
        var body = new StackPanel();
        var volumeHeader = new Grid();
        volumeHeader.ColumnDefinitions.Add(new ColumnDefinition());
        volumeHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        volumeHeader.Children.Add(new TextBlock { Text = "Local player volume", Foreground = Muted, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        _volumeValue.Text = $"{Math.Round(Math.Clamp(double.IsFinite(settings.Volume) ? settings.Volume : 1, 0, 1) * 100):0}%";
        _volumeValue.Foreground = Accent;
        _volumeValue.FontSize = 10;
        _volumeValue.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_volumeValue, 1);
        volumeHeader.Children.Add(_volumeValue);
        body.Children.Add(volumeHeader);

        _volume = SliderControl(0, 1, Math.Clamp(double.IsFinite(settings.Volume) ? settings.Volume : 1, 0, 1));
        _volume.Margin = new Thickness(0, 2, 0, 8);
        _volume.ValueChanged += (_, _) => _volumeValue.Text = $"{Math.Round(_volume.Value * 100):0}%";
        body.Children.Add(_volume);

        var eqCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("#FF49365F"),
            Background = Brush("#FF171426"),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 3, 0, 3)
        };
        var eqLayout = new Grid();
        eqLayout.ColumnDefinitions.Add(new ColumnDefinition());
        eqLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var eqText = new StackPanel();
        eqText.Children.Add(new TextBlock { Text = "CHANGE EQ", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Ink });
        eqText.Children.Add(new TextBlock { Text = "Drag the live bands to shape in-app tracks", FontSize = 8, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0) });
        eqLayout.Children.Add(eqText);
        _eq = new CheckBox { IsChecked = settings.EqualizerEnabled, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = "The EQ only changes songs played by this app" };
        Grid.SetColumn(_eq, 1);
        eqLayout.Children.Add(_eq);
        eqCard.Child = eqLayout;
        body.Children.Add(eqCard);
        body.Children.Add(new TextBlock
        {
            Text = "LOCAL TRACKS ONLY  ·  WINDOWS AUDIO IS UNTOUCHED",
            FontSize = 7,
            Foreground = Brush("#FF8E83A6"),
            Margin = new Thickness(1, 5, 0, 0)
        });
        body.Children.Add(new TextBlock
        {
            Text = "Automix timing", Foreground = Ink, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(1, 15, 0, 6)
        });
        var timing = new UniformGrid { Columns = 2 };
        bool classic = settings.AutomixTiming == AutomixTiming.Classic;
        timing.Children.Add(ChoiceOption("Automix timing", "Adaptive", "Follows the outro and intro", !classic));
        _classicTiming = ChoiceOption("Automix timing", "Classic", "Short blends · about 3–5 seconds", classic);
        timing.Children.Add(_classicTiming);
        body.Children.Add(timing);
        body.Children.Add(new TextBlock { Text = "Audio buffering", Foreground = Ink, FontSize = 11,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(1, 15, 0, 6) });
        var buffering = new UniformGrid { Columns = 2 };
        buffering.Children.Add(ChoiceOption("Audio buffering", "16 ms · Original", "Existing playback · 3 buffers", !settings.StableAudioBuffering));
        _stableAudio = ChoiceOption("Audio buffering", "16 ms · Stable", "8 buffers · more headroom under load", settings.StableAudioBuffering);
        buffering.Children.Add(_stableAudio);
        body.Children.Add(buffering);
        body.Children.Add(new TextBlock { Text = "Applies to the next track. Both modes keep 16 ms chunks; Stable adds output buffering, not a slower visualizer mode.",
            Foreground = Muted, FontSize = 8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 7, 1, 0) });
        body.Children.Add(new TextBlock { Text = "AUTOMIX POPUP  /  SIZE", Foreground = Cyan, FontSize = 9,
            FontWeight = FontWeights.Bold, Margin = new Thickness(1, 17, 0, 7) });
        var overlayRow = new Grid();
        overlayRow.ColumnDefinitions.Add(new ColumnDefinition());
        overlayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        overlayRow.Children.Add(new TextBlock { Text = "Overlay size", Foreground = Ink, FontSize = 10 });
        _overlayScaleValue.Foreground = Accent;
        _overlayScaleValue.FontSize = 10;
        _overlayScaleValue.Text = $"{Math.Round(Math.Clamp(double.IsFinite(settings.AutomixOverlayScale) ? settings.AutomixOverlayScale : 1, .65, 1.8) * 100):0}%";
        Grid.SetColumn(_overlayScaleValue, 1);
        overlayRow.Children.Add(_overlayScaleValue);
        body.Children.Add(overlayRow);
        _overlayScale = SliderControl(.65, 1.8, double.IsFinite(settings.AutomixOverlayScale) ? Math.Clamp(settings.AutomixOverlayScale, .65, 1.8) : 1);
        _overlayScale.Margin = new Thickness(0, 4, 0, 7);
        _overlayScale.ValueChanged += (_, _) =>
        {
            _overlayScaleValue.Text = $"{Math.Round(_overlayScale.Value * 100):0}%";
            _resizeAutomixOverlayPreview(_overlayScale.Value);
        };
        body.Children.Add(_overlayScale);
        var preview = ActionButton("▷  PREVIEW POPUP", primary: true);
        preview.HorizontalAlignment = HorizontalAlignment.Left;
        preview.Click += (_, _) => _previewAutomixOverlay(_overlayScale.Value);
        body.Children.Add(preview);
        body.Children.Add(new TextBlock { Text = "Preview appears in the screen corner. Drag the slider while it is visible to compare sizes.",
            Foreground = Muted, FontSize = 8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 7, 1, 0) });
        return Card("04  /  PLAYER", "Playback settings", body);
    }

    private static RadioButton ChoiceOption(string group, string title, string description, bool selected)
    {
        var labels = new StackPanel();
        labels.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ink });
        labels.Children.Add(new TextBlock { Text = description, FontSize = 9, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        var frame = new Border
        {
            CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 5, 0), Padding = new Thickness(9, 7, 9, 7), Child = labels
        };
        var option = new RadioButton
        {
            Template = RadioButtonTemplate(), GroupName = group, Content = frame,
            Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        System.Windows.Automation.AutomationProperties.SetName(option, title + " " + group);
        void Repaint()
        {
            frame.Background = Brush(option.IsChecked == true ? "#FF392051" : "#FF171426");
            frame.BorderBrush = option.IsChecked == true ? Accent : Brush("#FF49365F");
        }
        option.Checked += (_, _) => Repaint();
        option.Unchecked += (_, _) => Repaint();
        option.IsChecked = selected;
        Repaint();
        return option;
    }

    private UIElement BuildFooter()
    {
        var row = new Grid { Margin = new Thickness(2, 8, 1, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var note = new TextBlock
        {
            Text = "SAVED LOCALLY ON THIS PC",
            FontSize = 7,
            Foreground = Brush("#FF9589AD"),
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(note);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = ActionButton("Cancel");
        cancel.IsCancel = false;
        cancel.Click += (_, _) => RequestClose(false);
        actions.Children.Add(cancel);
        var save = ActionButton("SAVE SETTINGS", primary: true);
        save.IsDefault = true;
        save.Margin = new Thickness(8, 0, 0, 0);
        save.Click += (_, _) =>
        {
            _settings.MusicFolders = _folders.Select(field => field.Text.Trim())
                .Where(folder => folder.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _settings.MusicFolder = _settings.MusicFolders.FirstOrDefault();
            _settings.Background = _customTheme.IsChecked == true ? "Custom"
                : _oceanTheme.IsChecked == true ? "Ocean Drive"
                : _violetTheme.IsChecked == true ? "Violet Dusk" : "Neon Grid";
            _settings.CustomPrimary = CustomPalette.Hex(_primaryPicker.SelectedColor);
            _settings.CustomSecondary = CustomPalette.Hex(_secondaryPicker.SelectedColor);
            _settings.CustomMix = _customMix;
            _settings.BackgroundImage = File.Exists(_image.Text) ? _image.Text : null;
            _settings.ClassicVisualizer = _spectrumLab.UsesPulse;
            _settings.Sensitivity = _sensitivity.Value;
            _settings.Volume = _volume.Value;
            _settings.EqualizerEnabled = _eq.IsChecked == true;
            _settings.AutomixTiming = _classicTiming.IsChecked == true ? AutomixTiming.Classic : AutomixTiming.Adaptive;
            _settings.StableAudioBuffering = _stableAudio.IsChecked == true;
            _settings.AutomixOverlayScale = _overlayScale.Value;
            RequestClose(true);
        };
        actions.Children.Add(save);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    private Border BuildEditModeCard()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(new TextBlock { Text = "MOVE · RESIZE · RESET", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Cyan });
        details.Children.Add(new TextBlock { Text = "Arrange the widget directly with your mouse", FontSize = 8, Foreground = Muted, Margin = new Thickness(0, 3, 0, 0) });
        row.Children.Add(details);
        var open = ActionButton("OPEN EDIT MODE", primary: true);
        open.Margin = new Thickness(8, 0, 0, 0);
        open.Click += (_, _) => { EditModeRequested = true; RequestClose(false); };
        Grid.SetColumn(open, 1);
        row.Children.Add(open);
        return Card("05  /  EDIT MODE", "Make the widget yours", row);
    }

    private Border Card(string index, string subtitle, UIElement content)
    {
        var outer = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("#FF39294F"),
            Background = Brush("#FF151326"),
            Padding = new Thickness(12, 10, 12, 11),
            Margin = new Thickness(0, 0, 0, 9),
            Opacity = 0
        };
        var contentStack = new StackPanel();
        var head = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var label = new TextBlock { Text = index, Foreground = Accent, FontSize = 8, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 1, 9, 0) };
        head.Children.Add(label);
        var title = new TextBlock { Text = subtitle, Foreground = Ink, FontSize = 11, FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        contentStack.Children.Add(head);
        contentStack.Children.Add(content);
        outer.Child = contentStack;
        _entranceItems.Add(outer);
        return outer;
    }
}
