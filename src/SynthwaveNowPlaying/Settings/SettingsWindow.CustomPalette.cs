using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SynthwaveNowPlaying;

internal sealed partial class SettingsWindow
{
    private RadioButton _customTheme = null!;
    private ColorPickerPanel _primaryPicker = null!;
    private ColorPickerPanel _secondaryPicker = null!;
    private readonly Image _customLiveImage = new();
    private readonly TextBlock _mixValue = new();
    private readonly TextBlock _previewColors = new();
    private readonly Border _mixTrack = new();
    private readonly Border _mixMarker = new();
    private readonly DispatcherTimer _customPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private double _customMix;
    private bool _draggingMix;

    private FrameworkElement BuildAppearancePage()
    {
        var page = new StackPanel();
        page.Children.Add(BuildUiStyleCard());
        var preset = BuildAppearanceCard();
        preset.Margin = new Thickness(0, 0, 0, 15);
        page.Children.Add(preset);
        page.Children.Add(BuildCustomPaletteCard());
        return page;
    }

    private Border BuildCustomPaletteCard()
    {
        _customMix = double.IsFinite(_settings.CustomMix) ? Math.Clamp(_settings.CustomMix, 0, 1) : .5;
        var body = new StackPanel();
        var introduction = new Grid { Margin = new Thickness(0, 2, 0, 12) };
        introduction.ColumnDefinitions.Add(new ColumnDefinition());
        introduction.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        introduction.Children.Add(new TextBlock
        {
            Text = "PICK COLORS  /  DRAG THE PALETTE  /  WATCH THE WIDGET",
            Foreground = Muted, FontSize = 8, VerticalAlignment = VerticalAlignment.Center
        });
        var choiceFrame = new Border
        {
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(_settings.Background == "Custom" ? "#FFFF78D7" : "#FF6A4A86"),
            Background = Brush(_settings.Background == "Custom" ? "#FF44235D" : "#FF221932"),
            Padding = new Thickness(10, 6, 10, 6),
            Child = new TextBlock { Text = "USE CUSTOM PALETTE", Foreground = Ink, FontSize = 8, FontWeight = FontWeights.Bold }
        };
        _customTheme = new RadioButton
        {
            GroupName = "Background", Content = choiceFrame,
            Template = RadioButtonTemplate(), IsChecked = _settings.Background == "Custom",
            Cursor = Cursors.Hand, Focusable = true
        };
        _customTheme.Checked += (_, _) =>
        {
            choiceFrame.BorderBrush = Accent;
            choiceFrame.Background = Brush("#FF44235D");
        };
        _customTheme.Unchecked += (_, _) =>
        {
            choiceFrame.BorderBrush = Brush("#FF6A4A86");
            choiceFrame.Background = Brush("#FF221932");
        };
        Grid.SetColumn(_customTheme, 1);
        introduction.Children.Add(_customTheme);
        body.Children.Add(introduction);

        var previewFrame = new Border
        {
            Height = 215,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("#FF6C4B92"),
            Background = Gradient("#FF17172C", "#FF251335"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var previewLayout = new Grid();
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(19) });
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        previewLayout.Children.Add(new TextBlock
        {
            Text = "LIVE PREVIEW  /  YOUR COLOR COMBINATION",
            Foreground = Cyan, FontSize = 8, FontWeight = FontWeights.Bold
        });
        _customLiveImage.Stretch = Stretch.Uniform;
        _customLiveImage.SnapsToDevicePixels = true;
        _customLiveImage.HorizontalAlignment = HorizontalAlignment.Center;
        _customLiveImage.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(_customLiveImage, 1);
        previewLayout.Children.Add(_customLiveImage);
        previewFrame.Child = previewLayout;
        body.Children.Add(previewFrame);

        var pickers = new Grid();
        pickers.ColumnDefinitions.Add(new ColumnDefinition());
        pickers.ColumnDefinitions.Add(new ColumnDefinition());
        _primaryPicker = new ColorPickerPanel("PRIMARY NEON", CustomPalette.Parse(_settings.CustomPrimary, CustomPalette.DefaultPrimary));
        _secondaryPicker = new ColorPickerPanel("SECONDARY GLOW", CustomPalette.Parse(_settings.CustomSecondary, CustomPalette.DefaultSecondary));
        _secondaryPicker.Margin = new Thickness(0);
        pickers.Children.Add(_primaryPicker);
        Grid.SetColumn(_secondaryPicker, 1);
        pickers.Children.Add(_secondaryPicker);
        _primaryPicker.ColorChanged += _ => { _customTheme.IsChecked = true; QueueCustomPreview(); };
        _secondaryPicker.ColorChanged += _ => { _customTheme.IsChecked = true; QueueCustomPreview(); };
        body.Children.Add(pickers);

        var mixHeader = new Grid { Margin = new Thickness(1, 17, 1, 6) };
        mixHeader.ColumnDefinitions.Add(new ColumnDefinition());
        mixHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mixHeader.Children.Add(new TextBlock
        {
            Text = "COLOR BALANCE", Foreground = Ink, FontSize = 9, FontWeight = FontWeights.Bold
        });
        _mixValue.Foreground = Cyan;
        _mixValue.FontSize = 9;
        _mixValue.FontWeight = FontWeights.Bold;
        Grid.SetColumn(_mixValue, 1);
        mixHeader.Children.Add(_mixValue);
        body.Children.Add(mixHeader);

        var mixSurface = new Grid { Height = 26, Background = Brushes.Transparent, Cursor = Cursors.SizeWE };
        _mixTrack.Height = 12;
        _mixTrack.CornerRadius = new CornerRadius(6);
        _mixTrack.BorderBrush = Brush("#CCFFFFFF");
        _mixTrack.BorderThickness = new Thickness(1);
        _mixTrack.VerticalAlignment = VerticalAlignment.Center;
        mixSurface.Children.Add(_mixTrack);
        var markerCanvas = new Canvas { IsHitTestVisible = false };
        _mixMarker.Width = 14;
        _mixMarker.Height = 22;
        _mixMarker.CornerRadius = new CornerRadius(7);
        _mixMarker.Background = Brushes.White;
        _mixMarker.BorderBrush = Brush("#FF382048");
        _mixMarker.BorderThickness = new Thickness(2);
        markerCanvas.Children.Add(_mixMarker);
        mixSurface.Children.Add(markerCanvas);
        mixSurface.SizeChanged += (_, _) => PositionMixMarker(mixSurface);
        mixSurface.MouseLeftButtonDown += (_, e) => { _draggingMix = true; mixSurface.CaptureMouse(); UpdateMixFromPointer(mixSurface, e.GetPosition(mixSurface)); e.Handled = true; };
        mixSurface.MouseMove += (_, e) => { if (_draggingMix) UpdateMixFromPointer(mixSurface, e.GetPosition(mixSurface)); };
        mixSurface.MouseLeftButtonUp += (_, _) => { _draggingMix = false; mixSurface.ReleaseMouseCapture(); };
        mixSurface.LostMouseCapture += (_, _) => _draggingMix = false;
        body.Children.Add(mixSurface);
        _previewColors.Foreground = Muted;
        _previewColors.FontSize = 8;
        _previewColors.Margin = new Thickness(1, 7, 0, 0);
        body.Children.Add(_previewColors);

        _customPreviewTimer.Tick += (_, _) => RefreshCustomPreview();
        QueueCustomPreview();
        return Card("CUSTOM  /  PALETTE LAB", "Build a personal colorway", body);
    }

    private void UpdateMixFromPointer(Grid surface, Point point)
    {
        if (surface.ActualWidth < 1) return;
        _customMix = Math.Clamp(point.X / surface.ActualWidth, 0, 1);
        _customTheme.IsChecked = true;
        PositionMixMarker(surface);
        QueueCustomPreview();
    }

    private void PositionMixMarker(Grid surface)
    {
        Canvas.SetLeft(_mixMarker, _customMix * surface.ActualWidth - _mixMarker.Width / 2);
        Canvas.SetTop(_mixMarker, 2);
    }

    private void QueueCustomPreview()
    {
        if (_primaryPicker is null || _secondaryPicker is null) return;
        var primary = _primaryPicker.SelectedColor;
        var secondary = _secondaryPicker.SelectedColor;
        _mixTrack.Background = new LinearGradientBrush(primary, secondary, 0);
        _mixValue.Text = $"{Math.Round((1 - _customMix) * 100):0}%  /  {Math.Round(_customMix * 100):0}%";
        var effective = CustomPalette.Resolve(primary, secondary, _customMix);
        _previewColors.Text = $"PRIMARY  {CustomPalette.Hex(effective.Primary)}     SECONDARY  {CustomPalette.Hex(effective.Secondary)}";
        // Throttle while dragging, so the preview keeps updating during a long gesture.
        if (!_customPreviewTimer.IsEnabled) _customPreviewTimer.Start();
    }

    private void RefreshCustomPreview()
    {
        _customPreviewTimer.Stop();
        _themePreviews.TryGetValue("Neon Grid", out BitmapSource? baseImage);
        var effective = CustomPalette.Resolve(_primaryPicker.SelectedColor, _secondaryPicker.SelectedColor, _customMix);
        _customLiveImage.Source = MainWindow.RecolorPreview(baseImage,
            Color.FromRgb(255, 62, 189), Color.FromRgb(61, 220, 255), effective.Primary, effective.Secondary);
    }
}
