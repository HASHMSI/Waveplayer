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
using System.Windows.Media.Imaging;

namespace SynthwaveNowPlaying;

internal sealed partial class SettingsWindow : Window
{
    // Window lifecycle and entrance/exit transitions stay together here.
    private readonly AppSettings _settings;
    private readonly List<TextBox> _folders = [];
    private readonly StackPanel _folderRows = new();
    private RadioButton _neonTheme = null!;
    private RadioButton _violetTheme = null!;
    private RadioButton _oceanTheme = null!;
    private readonly TextBox _image = new();
    private CheckBox _eq = null!;
    private RadioButton _stableAudio = null!;
    private RadioButton _v2Timing = null!;
    private Slider _sensitivity = null!;
    private Slider _volume = null!;
    private Slider _overlayScale = null!;
    private CheckBox _overlayEnabled = null!;
    private readonly TextBlock _overlayScaleValue = new();
    private readonly Action<double> _previewAutomixOverlay;
    private readonly Action<double> _resizeAutomixOverlayPreview;
    private readonly TextBlock _sensitivityValue = new();
    private readonly TextBlock _volumeValue = new();
    private readonly List<FrameworkElement> _entranceItems = [];
    private readonly List<Popup> _themeHoverPopups = [];
    private readonly TranslateTransform _windowTransition = new();
    private bool _closeAnimationStarted;
    private bool _allowClose;
    private bool? _pendingResult;
    public bool EditModeRequested { get; private set; }

    private static readonly Brush Ink = Brush("#FFF7F1FF");
    private static readonly Brush Muted = Brush("#FFADA5C3");
    private static readonly Brush Accent = Brush("#FFFF69D3");
    private static readonly Brush Cyan = Brush("#FF69DEFF");

    private readonly Func<float[], bool> _readSpectrumPreview;

    public SettingsWindow(AppSettings settings, Style scrollBarStyle, IReadOnlyDictionary<string, BitmapSource?> themePreviews,
        string playlistName, IReadOnlyList<ScanTrack> scanTracks, TrackAnalysisCache analysisCache,
        Action<string, TrackAnalysis> onAnalysisReady, Func<float[], bool> readSpectrumPreview,
        Action<double> previewAutomixOverlay, Action<double> resizeAutomixOverlayPreview)
    {
        _settings = settings;
        _readSpectrumPreview = readSpectrumPreview;
        _previewAutomixOverlay = previewAutomixOverlay;
        _resizeAutomixOverlayPreview = resizeAutomixOverlayPreview;
        _themePreviews = themePreviews;
        _playlistName = playlistName;
        _scanTracks = scanTracks;
        _analysisCache = analysisCache;
        _onAnalysisReady = onAnalysisReady;
        Title = "WavePlayer · Control Deck";
        Width = Math.Min(820, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(680, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(700, Width);
        MinHeight = Math.Min(560, Height);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Bahnschrift");
        Foreground = Ink;

        var shell = new Border
        {
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("#FFB45BCE"),
            Background = Gradient("#FF16112C", "#FF090E20"),
            SnapsToDevicePixels = true
        };
        shell.RenderTransformOrigin = new Point(.5, .5);
        shell.RenderTransform = _windowTransition;
        var layout = new Grid { Margin = new Thickness(18, 14, 18, 14) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        layout.Children.Add(header);

        var sections = BuildSectionLayout(scrollBarStyle);
        Grid.SetRow(sections, 1);
        layout.Children.Add(sections);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        shell.Child = layout;
        Content = shell;

        Loaded += (_, _) => AnimateEntrance();
        Closing += SettingsWindow_Closing;
        Closed += (_, _) => _bpmScanCts?.Cancel();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            RequestClose(false);
        };
    }

    private UIElement BuildHeader()
    {
        var row = new Grid { Margin = new Thickness(2, 0, 1, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not Button)
                try { DragMove(); } catch (InvalidOperationException) { }
        };

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "WavePlayer  /  CONTROL DECK",
            FontSize = 8,
            FontWeight = FontWeights.SemiBold,
            Foreground = Cyan,
        });
        titles.Children.Add(new TextBlock
        {
            Text = "SYSTEM SETTINGS",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = Ink,
            Margin = new Thickness(0, 1, 0, 0)
        });
        row.Children.Add(titles);

        var close = ActionButton("×", compact: true);
        close.ToolTip = "Close settings";
        close.Click += (_, _) => RequestClose(false);
        Grid.SetColumn(close, 1);
        row.Children.Add(close);
        return row;
    }

    private void AnimateEntrance()
    {
        Opacity = 0;
        _windowTransition.Y = 10;
        BeginAnimation(OpacityProperty, Animation(1, 230, 0));
        _windowTransition.BeginAnimation(TranslateTransform.YProperty, Animation(0, 270, 0));
        for (var i = 0; i < _entranceItems.Count; i++)
        {
            var item = _entranceItems[i];
            if (item.Visibility != Visibility.Visible) continue;
            var shift = new TranslateTransform(0, 8);
            item.RenderTransform = shift;
            var delay = 65 + i * 55;
            item.BeginAnimation(OpacityProperty, Animation(1, 260, delay));
            shift.BeginAnimation(TranslateTransform.YProperty, Animation(0, 280, delay));
        }
    }

    private void SettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        RequestClose(false);
    }

    private void RequestClose(bool? result)
    {
        if (_closeAnimationStarted) return;
        _customPreviewTimer.Stop();
        foreach (var popup in _themeHoverPopups) popup.IsOpen = false;
        _closeAnimationStarted = true;
        _pendingResult = result;
        var duration = TimeSpan.FromMilliseconds(190);
        var fade = new DoubleAnimation(Opacity, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            _allowClose = true;
            if (_pendingResult is bool dialogResult) DialogResult = dialogResult;
            else Close();
        };
        BeginAnimation(OpacityProperty, fade);
        _windowTransition.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(_windowTransition.Y, 12, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
    }

    private void BrowseFolder(object? sender, RoutedEventArgs e)
    {
        var target = (sender as Button)?.Tag as TextBox;
        var initial = target is not null && Directory.Exists(target.Text)
            ? target.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        var dialog = new OpenFileDialog { Title = "Choose a music folder, then click Open", CheckFileExists = false, ValidateNames = false, FileName = "Select this folder", InitialDirectory = initial };
        if (dialog.ShowDialog(this) != true) return;
        var folder = Path.GetDirectoryName(dialog.FileName);
        if (string.IsNullOrWhiteSpace(folder)) return;
        if (target is null)
        {
            if (_folders.Any(entry => string.Equals(entry.Text.Trim(), folder, StringComparison.OrdinalIgnoreCase))) return;
            AddMusicFolderRow(folder);
        }
        else target.Text = folder;
    }

    private void BrowseImage(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose a background image", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) _image.Text = dialog.FileName;
    }

    private static void StyleBox(TextBox box)
    {
        box.Padding = new Thickness(9, 7, 9, 7);
        box.Background = Brush("#FF171426");
        box.Foreground = Ink;
        box.BorderBrush = Brush("#FF49365F");
        box.BorderThickness = new Thickness(1);
        box.CaretBrush = Cyan;
        box.FontSize = 9;
        box.FontFamily = new FontFamily("Bahnschrift");
        ToolTipService.SetShowDuration(box, 10000);
    }

    private static Brush Gradient(string start, string end)
    {
        var brush = new LinearGradientBrush(Brush(start).Color, Brush(end).Color, 90);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Brush(string hex) => new BrushConverter().ConvertFromString(hex) as SolidColorBrush ?? new SolidColorBrush(Colors.White);
}
