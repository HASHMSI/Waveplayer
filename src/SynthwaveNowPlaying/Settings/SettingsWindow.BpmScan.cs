using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SynthwaveNowPlaying;

internal sealed record ScanTrack(string File, string Title);

internal sealed partial class SettingsWindow
{
    private readonly ObservableCollection<BpmScanRow> _bpmRows = [];
    private string _playlistName = "";
    private IReadOnlyList<ScanTrack> _scanTracks = [];
    private TrackAnalysisCache _analysisCache = null!;
    private Action<string, TrackAnalysis> _onAnalysisReady = null!;
    private CancellationTokenSource? _bpmScanCts;
    private Button _bpmScanButton = null!;
    private TextBlock _scanCurrentTitle = null!;
    private TextBlock _scanCurrentMeta = null!;
    private TextBlock _scanSummary = null!;
    private Border _scanProgressTrack = null!;
    private Border _scanProgressFill = null!;
    private ListBox _scanList = null!;
    private readonly List<Border> _scanBars = [];
    private readonly SolidColorBrush _scanHeroBorder = Brush("#FF9150B8");
    private double _scanProgress;

    private Border BuildBpmScanCard(Style scrollBarStyle)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = _playlistName.ToUpperInvariant(), Foreground = Cyan, FontSize = 9,
            FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis
        });
        body.Children.Add(new TextBlock
        {
            Text = $"{_scanTracks.Count} TRACKS  ·  20–60 SECONDS PER NEW SCAN",
            Foreground = Muted, FontSize = 8, Margin = new Thickness(0, 4, 0, 12)
        });

        var hero = new Border
        {
            CornerRadius = new CornerRadius(12), Background = Gradient("#FF302046", "#FF172743"),
            BorderBrush = _scanHeroBorder, BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 13), Height = 94
        };
        var heroGrid = new Grid();
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition());
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heroText.Children.Add(new TextBlock
        {
            Text = "RHYTHM + HARMONY  /  NOW ANALYZING",
            Foreground = Accent, FontSize = 7, FontWeight = FontWeights.Bold
        });
        _scanCurrentTitle = new TextBlock
        {
            Text = "Ready to scan this playlist", Foreground = Ink, FontSize = 16,
            FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 7, 0, 2)
        };
        heroText.Children.Add(_scanCurrentTitle);
        _scanCurrentMeta = new TextBlock { Text = "Scan BPM + musical key locally · audio stays silent", Foreground = Muted, FontSize = 8 };
        heroText.Children.Add(_scanCurrentMeta);
        heroGrid.Children.Add(heroText);
        var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        for (var i = 0; i < 7; i++)
        {
            var bar = new Border
            {
                Width = 4, Height = 6, CornerRadius = new CornerRadius(2),
                Background = i % 2 == 0 ? Accent : Cyan, Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _scanBars.Add(bar);
            bars.Children.Add(bar);
        }
        Grid.SetColumn(bars, 1);
        heroGrid.Children.Add(bars);
        hero.Child = heroGrid;
        body.Children.Add(hero);

        var actions = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _scanSummary = new TextBlock
        {
            Text = _scanTracks.Count == 0 ? "No playlist tracks yet" : $"{_bpmRows.Count} tracks ready",
            Foreground = Muted, FontSize = 9, VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(_scanSummary);
        _bpmScanButton = ActionButton("SCAN PLAYLIST", primary: true);
        _bpmScanButton.IsEnabled = _scanTracks.Count > 0;
        _bpmScanButton.Click += async (_, _) => await ScanPlaylistBpmAsync();
        Grid.SetColumn(_bpmScanButton, 1);
        actions.Children.Add(_bpmScanButton);
        body.Children.Add(actions);

        _scanProgressFill = new Border
        {
            Width = 0, HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(4), Background = Gradient("#FFEF55C5", "#FF5DDCFF")
        };
        _scanProgressTrack = new Border
        {
            Height = 7, CornerRadius = new CornerRadius(4), Background = Brush("#FF28223E"),
            ClipToBounds = true, Child = _scanProgressFill, Margin = new Thickness(0, 0, 0, 14)
        };
        _scanProgressTrack.SizeChanged += (_, _) => _scanProgressFill.Width = _scanProgressTrack.ActualWidth * _scanProgress;
        body.Children.Add(_scanProgressTrack);

        body.Children.Add(new TextBlock
        {
            Text = "TRACKS  /  BPM + MUSICAL KEY", FontSize = 8, FontWeight = FontWeights.Bold,
            Foreground = Cyan, Margin = new Thickness(2, 0, 0, 7)
        });
        _scanList = new ListBox
        {
            ItemsSource = _bpmRows, ItemTemplate = BuildBpmRowTemplate(), Height = 262,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _scanList.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        _scanList.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        _scanList.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        _scanList.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), scrollBarStyle);
        var listStyle = new Style(typeof(ListBoxItem));
        listStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        listStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        listStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        _scanList.ItemContainerStyle = listStyle;
        body.Children.Add(_scanList);

        for (var index = 0; index < _scanTracks.Count; index++)
        {
            var track = _scanTracks[index];
            var row = new BpmScanRow(track, index + 1);
            if (_analysisCache.TryGet(track.File, out var cached)) row.SetResult(cached);
            _bpmRows.Add(row);
        }
        _scanSummary.Text = $"{_bpmRows.Count(row => row.HasBpm)} BPM · {_bpmRows.Count(row => row.HasKey)} KEYS · {_bpmRows.Count} TRACKS";
        return Card("06  /  BPM + KEY LAB", "Major / minor · Camelot · cached locally · uncertain keys stay UNSURE", body);
    }

    private static DataTemplate BuildBpmRowTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brush("#FF403154"));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 7, 10, 7));
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 3, 5));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(BpmScanRow.Background)));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var number = new FrameworkElementFactory(typeof(TextBlock));
        number.SetBinding(TextBlock.TextProperty, new Binding(nameof(BpmScanRow.Number)));
        number.SetValue(TextBlock.ForegroundProperty, Cyan);
        number.SetValue(TextBlock.FontSizeProperty, 8d);
        number.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        number.SetValue(FrameworkElement.WidthProperty, 29d);
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(BpmScanRow.Title)));
        title.SetValue(TextBlock.ForegroundProperty, Ink);
        title.SetValue(TextBlock.FontSizeProperty, 10d);
        title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        title.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        title.SetValue(FrameworkElement.MarginProperty, new Thickness(29, 0, 177, 0));
        var bpm = new FrameworkElementFactory(typeof(TextBlock));
        bpm.SetBinding(TextBlock.TextProperty, new Binding(nameof(BpmScanRow.BpmText)));
        bpm.SetValue(TextBlock.ForegroundProperty, Accent);
        bpm.SetValue(TextBlock.FontSizeProperty, 9d);
        bpm.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        bpm.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(BpmScanRow.AnalysisDetails)));
        bpm.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        bpm.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
        bpm.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        bpm.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        bpm.SetValue(FrameworkElement.WidthProperty, 165d);
        grid.AppendChild(number);
        grid.AppendChild(title);
        grid.AppendChild(bpm);
        border.AppendChild(grid);
        return new DataTemplate { VisualTree = border };
    }

    private async Task ScanPlaylistBpmAsync()
    {
        if (_bpmScanCts is not null || _bpmRows.Count == 0) return;
        var cts = new CancellationTokenSource();
        _bpmScanCts = cts;
        _bpmScanButton.IsEnabled = false;
        _bpmScanButton.Content = "SCANNING...";
        StartScanWave();
        var completed = 0;
        var changed = 0;
        try
        {
            foreach (var row in _bpmRows)
            {
                cts.Token.ThrowIfCancellationRequested();
                row.IsScanning = true;
                _scanList.ScrollIntoView(row);
                _scanCurrentTitle.Text = row.Title;
                _scanCurrentTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(280)));
                _scanCurrentMeta.Text = $"ANALYZING TRACK {completed + 1:00} / {_bpmRows.Count:00}  ·  {Path.GetExtension(row.File).TrimStart('.').ToUpperInvariant()}";
                try
                {
                    var cached = _analysisCache.TryGet(row.File, out var analysis) && analysis.DeepScanned;
                    if (cached && !analysis.KeyScanned)
                    {
                        _scanCurrentMeta.Text = "BPM cached · checking musical key across the track";
                        using var keyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        keyTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                        try
                        {
                            analysis = await Task.Run(() => _analysisCache.AnalyzeKey(row.File, keyTimeout.Token), keyTimeout.Token)
                                .WaitAsync(keyTimeout.Token);
                        }
                        catch (OperationCanceledException) when (!cts.IsCancellationRequested)
                        { throw new TimeoutException("Key analysis timed out."); }
                        changed++;
                    }
                    else if (!cached)
                    {
                        analysis = await TimedBpmScan.RunAsync(token => _analysisCache.AnalyzeDeep(row.File, token), (elapsed, ready) =>
                        {
                            _scanCurrentMeta.Text = ready
                                ? $"Analysis finished · next result in {Math.Max(0, Math.Ceiling(20 - elapsed.TotalSeconds))} s"
                                : $"Checking tempo + key · {elapsed.TotalSeconds:0} / 60 s";
                            UpdateScanProgress((completed + Math.Min(.98, elapsed.TotalSeconds / 60)) / _bpmRows.Count);
                        }, cts.Token);
                        changed++;
                    }
                    row.SetResult(analysis);
                    _onAnalysisReady(row.File, analysis);
                }
                catch (OperationCanceledException) { throw; }
                catch (TimeoutException) { row.MarkError("TIMED OUT"); }
                catch { row.MarkError(); }
                row.IsScanning = false;
                completed++;
                UpdateScanProgress((double)completed / _bpmRows.Count);
                _scanSummary.Text = $"{completed} / {_bpmRows.Count} SCANNED · {_bpmRows.Count(item => item.HasBpm)} BPM · {_bpmRows.Count(item => item.HasKey)} KEYS";
                if (changed >= 8) { await Task.Run(_analysisCache.Save); changed = 0; }
            }
            if (!cts.IsCancellationRequested)
            {
                _scanCurrentTitle.Text = "Playlist analysis complete";
                _scanCurrentMeta.Text = $"{_bpmRows.Count(item => item.HasBpm)} BPM · {_bpmRows.Count(item => item.HasKey)} usable keys · saved locally";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _scanCurrentTitle.Text = "Scan paused after an error";
            _scanCurrentMeta.Text = ex.Message;
        }
        finally
        {
            if (changed > 0)
            {
                try { await Task.Run(_analysisCache.Save); }
                catch { _scanCurrentMeta.Text = "Results could not be saved to disk"; }
            }
            StopScanWave();
            _bpmScanButton.Content = "SCAN PLAYLIST";
            _bpmScanButton.IsEnabled = _bpmRows.Count > 0;
            _bpmScanCts = null;
            cts.Dispose();
        }
    }

    private void UpdateScanProgress(double progress)
    {
        _scanProgress = progress;
        var target = _scanProgressTrack.ActualWidth * progress;
        _scanProgressFill.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(240))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void StartScanWave()
    {
        _scanHeroBorder.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Color.FromRgb(95, 220, 255), TimeSpan.FromMilliseconds(900))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        for (var i = 0; i < _scanBars.Count; i++)
            _scanBars[i].BeginAnimation(FrameworkElement.HeightProperty,
                new DoubleAnimation(5, 15 + i % 3 * 5, TimeSpan.FromMilliseconds(430 + i * 45))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(i * 75) });
    }

    private void StopScanWave()
    {
        _scanHeroBorder.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _scanHeroBorder.Color = Color.FromRgb(145, 80, 184);
        foreach (var bar in _scanBars) { bar.BeginAnimation(FrameworkElement.HeightProperty, null); bar.Height = 6; }
    }

    private sealed class BpmScanRow(ScanTrack track, int number) : INotifyPropertyChanged
    {
        private string _bpmText = "—";
        private Brush _background = Brush("#FF171426");
        private bool _isScanning;
        public string File => track.File;
        public string Title => track.Title;
        public string Number { get; } = number.ToString("00");
        public bool HasBpm { get; private set; }
        public bool HasKey { get; private set; }
        public string AnalysisDetails { get; private set; } = "Scan to estimate tempo and musical key.";
        public string BpmText { get => _bpmText; private set { _bpmText = value; Changed(); } }
        public Brush Background { get => _background; private set { _background = value; Changed(); } }
        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                _isScanning = value;
                var animated = new SolidColorBrush(value ? Color.FromRgb(23, 20, 38) : Color.FromRgb(72, 38, 87));
                animated.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(value ? Color.FromRgb(72, 38, 87) : Color.FromRgb(23, 20, 38),
                        TimeSpan.FromMilliseconds(230)));
                Background = animated;
                if (value) BpmText = "SCANNING";
            }
        }
        public void SetResult(TrackAnalysis result)
        {
            HasBpm = result.Bpm > 0;
            HasKey = result.KeyScanned && result.Key.Reliable;
            var bpm = HasBpm ? $"{(result.FromTag ? "" : "≈")}{Math.Round(result.Bpm):0} BPM" : result.DeepScanned ? "UNSURE BPM" : "— BPM";
            BpmText = $"{bpm}\n{(result.KeyScanned ? result.Key.Display : "— KEY")}";
            AnalysisDetails = result.KeyScanned
                ? $"Musical key: {result.Key.Display}. This is an audio estimate. Ambiguous, modal or changing harmony may be UNSURE."
                : "Musical key has not been scanned yet. Existing BPM will be reused.";
            Changed(nameof(AnalysisDetails));
        }
        public void MarkError(string text = "ERROR") { HasBpm = false; BpmText = text; }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
