using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WaveplayerSetup;

public partial class MainWindow : Window
{
    private static readonly string[] StageNames = ["WELCOME", "DESTINATION", "INSTALLING", "COLORWAY", "MUSIC FOLDERS", "PLAYBACK", "READY"];
    private static readonly string[] Titles = ["Make room for the night", "Choose an install location", "Building your listening space", "Make it yours", "Find your music", "Set the rhythm", "Ready to play"];
    private int _stage;
    private string _destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Waveplayer");
    private readonly List<string> _folders = [];
    private string _theme = "Neon Grid";
    private string _primary = "#FF47B8";
    private string _secondary = "#45D8FF";
    private string _timing = "Adaptive";
    private bool _pulse = true;
    private bool _autoMix;
    private bool _desktopShortcut = true;
    private TextBox? _destinationBox;
    private TextBox? _primaryBox;
    private TextBox? _secondaryBox;
    private Border? _palettePreview;
    private StackPanel? _folderList;
    private ProgressBar? _progress;
    private TextBlock? _progressText;
    private bool _installing;
    private bool _installed;
    private bool _spectrumStep;
    private FrameworkElement? _spectrumSection;

    private static readonly DependencyProperty AnimatedScrollProperty = DependencyProperty.Register(
        "AnimatedScroll", typeof(double), typeof(MainWindow),
        new PropertyMetadata(0d, (sender, args) =>
            ((MainWindow)sender).PageScroll.ScrollToVerticalOffset((double)args.NewValue)));

    public MainWindow()
    {
        InitializeComponent();
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeonWave", "settings.json");
        try
        {
            if (File.Exists(settingsPath))
            {
                var previous = JsonNode.Parse(File.ReadAllText(settingsPath));
                _folders.AddRange(previous?["MusicFolders"]?.AsArray().Select(item => item?.GetValue<string>() ?? "").Where(Directory.Exists) ?? []);
                if (_folders.Count == 0 && previous?["MusicFolder"]?.GetValue<string>() is { } single && Directory.Exists(single)) _folders.Add(single);
                _theme = previous?["Background"]?.GetValue<string>() ?? _theme;
                _primary = previous?["CustomPrimary"]?.GetValue<string>() ?? _primary;
                _secondary = previous?["CustomSecondary"]?.GetValue<string>() ?? _secondary;
                _timing = previous?["AutomixTiming"]?.GetValue<int>() switch
                {
                    1 => "Classic", 2 => "Adaptive (v2)", _ => "Adaptive"
                };
                _autoMix = previous?["RemixDemoEnabled"]?.GetValue<bool>() ?? _autoMix;
                _pulse = previous?["ClassicVisualizer"]?.GetValue<bool>() ?? _pulse;
            }
        }
        catch { }
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)));
            var scale = new ScaleTransform(.97, .97);
            WindowFrame.RenderTransformOrigin = new Point(.5, .5);
            WindowFrame.RenderTransform = scale;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.97, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.97, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = ease });
        };
        Closed += (_, _) => StopDemo();
        RenderStage();
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) { if (!_installing) Close(); }
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        if (_stage == 5 && _spectrumStep)
        {
            _spectrumStep = false;
            StopDemo();
            NextButton.Content = "NEXT  →";
            AnimateScroll(0);
            return;
        }
        if (_stage > 0) { _stage--; RenderStage(); }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        if (_stage == 6) { Process.Start(new ProcessStartInfo(Path.Combine(_destination, "Waveplayer.exe")) { UseShellExecute = true }); Close(); return; }
        if (_stage == 1)
        {
            _destination = _destinationBox?.Text.Trim() ?? _destination;
            if (string.IsNullOrWhiteSpace(_destination)) { MessageBox.Show(this, "Choose an install folder.", "Waveplayer"); return; }
            _stage = 2;
            RenderStage();
            await InstallAsync();
            return;
        }
        if (_stage == 3)
        {
            if (_primaryBox is not null) _primary = NormalizeColor(_primaryBox.Text, _primary);
            if (_secondaryBox is not null) _secondary = NormalizeColor(_secondaryBox.Text, _secondary);
        }
        if (_stage == 4) _folders.RemoveAll(folder => !Directory.Exists(folder));
        if (_stage == 5)
        {
            if (!_spectrumStep)
            {
                _spectrumStep = true;
                StopDemo();
                NextButton.Content = "SAVE  →";
                if (_spectrumSection is not null)
                {
                    var position = _spectrumSection.TransformToAncestor(PageScroll).Transform(new Point(0, 0)).Y;
                    AnimateScroll(PageScroll.VerticalOffset + position);
                }
                return;
            }
            try { SavePreferences(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save setup choices"); return; }
        }
        _stage++;
        RenderStage();
    }

    private static string NormalizeColor(string value, string fallback)
    {
        value = value.Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$") ? value.ToUpperInvariant() : fallback;
    }

    private void AnimateScroll(double offset)
    {
        var target = Math.Clamp(offset, 0, PageScroll.ScrollableHeight);
        BeginAnimation(AnimatedScrollProperty, null);
        SetValue(AnimatedScrollProperty, PageScroll.VerticalOffset);
        BeginAnimation(AnimatedScrollProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(440))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void RenderStage()
    {
        StopDemo();
        _spectrumStep = false;
        _spectrumSection = null;
        BeginAnimation(AnimatedScrollProperty, null);
        PageScroll.ScrollToTop();
        Eyebrow.Text = "WAVEPLAYER  /  " + StageNames[_stage];
        Heading.Text = Titles[_stage];
        StepCounter.Text = $"{_stage + 1:00}  /  07     {StageNames[_stage]}";
        FooterNote.Text = _stage < 3 ? "Files stay on this PC" : "Your choices are saved for the first launch";
        BackButton.Visibility = _stage is 0 or 2 or 3 ? Visibility.Hidden : Visibility.Visible;
        NextButton.IsEnabled = !_installing;
        NextButton.Content = _stage switch { 1 => "INSTALL  →", 2 => "INSTALLING", 6 => "LAUNCH  ↗", _ => "NEXT  →" };
        Steps.Children.Clear();
        for (var i = 0; i < StageNames.Length; i++)
        {
            var label = new TextBlock
            {
                Text = $"{i + 1:00}    {StageNames[i]}",
                FontSize = 10,
                FontWeight = i == _stage ? FontWeights.Bold : FontWeights.Normal,
                Foreground = i == _stage ? Brush("#FF75DFFF") : i < _stage ? Brush("#FFB77FE2") : Brush("#FF827794"),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Steps.Children.Add(label);
        }
        UIElement content = _stage switch
        {
            0 => Welcome(),
            1 => Destination(),
            2 => Installing(),
            3 => Colorway(),
            4 => Folders(),
            5 => Playback(),
            _ => Finish()
        };
        PageHost.Content = content;
        content.Opacity = 0;
        content.RenderTransform = new TranslateTransform(0, 12);
        content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        ((TranslateTransform)content.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex)!);
    private static TextBlock Copy(string value, int size = 13) => new()
    {
        Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size, Foreground = Brush("#D8C8E5"), Margin = new Thickness(0, 0, 0, 16)
    };
    private static StackPanel Stack() => new() { VerticalAlignment = VerticalAlignment.Top };
    private static Border Panel(UIElement child) => new()
    {
        Child = child, CornerRadius = new CornerRadius(12), BorderBrush = Brush("#665884"),
        BorderThickness = new Thickness(1), Background = Brush("#CC17172F"), Padding = new Thickness(16),
        Margin = new Thickness(0, 0, 0, 12)
    };

    private UIElement Welcome()
    {
        var panel = Stack();
        panel.Children.Add(Copy("A compact player with a live spectrum, album-driven colors and adaptive transitions.", 15));
        panel.Children.Add(Panel(Copy("THIS SETUP WILL\n\n• Install Waveplayer for this Windows account\n• Let you choose the install folder\n• Configure the colors, music folders and playback\n• Create a Start menu shortcut", 12)));
        panel.Children.Add(Copy("Select Next to choose where the player should live.", 11));
        return panel;
    }

    private UIElement Destination()
    {
        var panel = Stack();
        panel.Children.Add(Copy("Choose a folder where the player files will be installed. The default location works without administrator access."));
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _destinationBox = new TextBox { Text = _destination, VerticalContentAlignment = VerticalAlignment.Center };
        row.Children.Add(_destinationBox);
        var browse = new Button { Content = "BROWSE", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var selected = ChooseFolder(_destinationBox.Text);
            if (selected is not null) _destinationBox.Text = selected;
        };
        Grid.SetColumn(browse, 1); row.Children.Add(browse);
        panel.Children.Add(Panel(row));
        var desktopShortcut = new CheckBox
        {
            Content = "Create desktop shortcut",
            IsChecked = _desktopShortcut,
            Margin = new Thickness(2, 3, 0, 16)
        };
        desktopShortcut.Checked += (_, _) => _desktopShortcut = true;
        desktopShortcut.Unchecked += (_, _) => _desktopShortcut = false;
        panel.Children.Add(desktopShortcut);
        panel.Children.Add(Copy("Installed files will be placed directly in this folder. Existing user settings are preserved until you save your choices later.", 11));
        return panel;
    }

    private UIElement Installing()
    {
        var panel = Stack();
        panel.Children.Add(Copy("Unpacking the player and preparing its sound engine.", 14));
        _progressText = Copy("Preparing package…", 11);
        panel.Children.Add(_progressText);
        _progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 10, Foreground = Brush("#FF73DFFF"), Background = Brush("#322047") };
        panel.Children.Add(Panel(_progress));
        return panel;
    }

    private UIElement Colorway()
    {
        var panel = Stack();
        panel.Children.Add(Copy("Choose a starting colorway. You can refine it later inside Waveplayer.", 12));
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, first, second) in new[] {
            ("Neon Grid", "#FF47B8", "#45D8FF"), ("Violet Dusk", "#D95BEF", "#FFB164"),
            ("Ocean Drive", "#4CD6FF", "#677EFF"), ("Custom", _primary, _secondary) })
        {
            var button = new Button { Content = name, Width = 122, Height = 60, Margin = new Thickness(0, 0, 7, 12),
                BorderBrush = Brush(first), Background = Brush("#352040") };
            button.Click += (_, _) => { _primary = first; _secondary = second; _primaryBox!.Text = first; _secondaryBox!.Text = second; _theme = name; UpdatePalettePreview(); };
            choices.Children.Add(button);
        }
        panel.Children.Add(choices);
        panel.Children.Add(Copy("PERSONAL PALETTE", 10));
        var colors = new StackPanel { Orientation = Orientation.Horizontal };
        _primaryBox = new TextBox { Text = _primary, Width = 140, Margin = new Thickness(0, 0, 12, 0) };
        _secondaryBox = new TextBox { Text = _secondary, Width = 140 };
        _primaryBox.TextChanged += (_, _) => { _theme = "Custom"; UpdatePalettePreview(); };
        _secondaryBox.TextChanged += (_, _) => { _theme = "Custom"; UpdatePalettePreview(); };
        colors.Children.Add(_primaryBox); colors.Children.Add(_secondaryBox);
        panel.Children.Add(Panel(colors));
        _palettePreview = new Border { Height = 125, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = Brush("#9161B2"), Child = CreatePlayerPreview(), Name = "PalettePreview" };
        panel.Children.Add(_palettePreview);
        UpdatePalettePreview();
        return panel;
    }

    private void UpdatePalettePreview()
    {
        if (_primaryBox is null || _secondaryBox is null || _palettePreview is null) return;
        var first = NormalizeColor(_primaryBox.Text, _primary);
        var second = NormalizeColor(_secondaryBox.Text, _secondary);
        _palettePreview.Background = Brush("#171728");
        _palettePreview.BorderBrush = Brush(first);
        if (_palettePreview.Child is FrameworkElement preview)
        {
            preview.Resources["PreviewPrimary"] = Brush(first);
            preview.Resources["PreviewSecondary"] = Brush(second);
        }
    }

    private UIElement Folders()
    {
        var panel = Stack();
        panel.Children.Add(Copy("Add the folders that contain your songs. Waveplayer will scan their subfolders and playlists too.", 12));
        _folderList = new StackPanel();
        panel.Children.Add(_folderList);
        foreach (var folder in _folders.ToArray()) AddFolderLine(folder);
        var add = new Button { Content = "+  ADD MUSIC FOLDER", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            var folder = ChooseFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            if (folder is not null && !_folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            { _folders.Add(folder); AddFolderLine(folder); }
        };
        panel.Children.Add(add);
        panel.Children.Add(Copy("You can add more folders or change these choices in Settings.", 11));
        return panel;
    }

    private void AddFolderLine(string folder)
    {
        if (_folderList is null) return;
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        var remove = new Button { Content = "×", Width = 33, Padding = new Thickness(0) };
        DockPanel.SetDock(remove, Dock.Right);
        remove.Click += (_, _) => { _folders.Remove(folder); _folderList.Children.Remove(row); };
        row.Children.Add(remove);
        var path = new TextBlock { Text = folder, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Panel(path));
        _folderList.Children.Add(row);
    }

    private UIElement Playback() => BuildPlayback();

    private UIElement Finish()
    {
        var panel = Stack();
        panel.Children.Add(Panel(Copy("✓  Waveplayer is installed and ready to launch.\n\nYour colors, music folders and playback choices are saved.", 15)));
        panel.Children.Add(Copy(_destination, 11));
        panel.Children.Add(new TextBlock
        {
            Text = "Create by MSIHASH",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#FF75DFFF"),
            Margin = new Thickness(2, 8, 0, 0)
        });
        return panel;
    }

    private string? ChooseFolder(string initial)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder for Waveplayer", InitialDirectory = Directory.Exists(initial) ? initial : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private async Task InstallAsync()
    {
        _installing = true;
        BackButton.IsEnabled = false; NextButton.IsEnabled = false;
        try
        {
            var progress = new Progress<int>(value =>
            {
                _progress!.Value = value;
                _progressText!.Text = value < 100 ? $"Installing…  {value}%" : "Preparing your preferences…";
            });
            await Task.Run(() => InstallPayload(_destination, _desktopShortcut, progress));
            _installed = true;
            _stage = 3;
            RenderStage();
        }
        catch (Exception ex)
        {
            _progressText!.Text = "Installation stopped. " + ex.Message;
            _stage = 1;
            RenderStage();
            MessageBox.Show(this, ex.Message, "Installation could not finish");
        }
        finally { _installing = false; BackButton.IsEnabled = true; NextButton.IsEnabled = true; }
    }

    private static void InstallPayload(string destination, bool desktopShortcut, IProgress<int> progress)
    {
        var full = Path.GetFullPath(destination);
        Directory.CreateDirectory(full);
        using var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("WaveplayerSetup.payload.zip")
            ?? throw new InvalidOperationException("Player package is missing.");
        using var archive = new ZipArchive(resource);
        var entries = archive.Entries.Where(entry => entry.Name.Length > 0).ToArray();
        long totalBytes = entries.Sum(entry => entry.Length);
        long copiedBytes = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var target = Path.GetFullPath(Path.Combine(full, entry.FullName));
            if (!target.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package path is invalid.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target + ".partial", FileMode.Create, FileAccess.Write);
            var block = new byte[256 * 1024];
            int read;
            while ((read = input.Read(block)) > 0)
            {
                output.Write(block, 0, read);
                copiedBytes += read;
                progress.Report((int)(copiedBytes * 100 / Math.Max(1, totalBytes)));
            }
            output.Close();
            File.Move(target + ".partial", target, true);

        }
        var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        Directory.CreateDirectory(menu);
        try
        {
            CreateShortcut(Path.Combine(menu, "Waveplayer.lnk"), full);
        }
        catch { }
        if (desktopShortcut)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop)) throw new IOException("Windows could not locate the desktop folder.");
            CreateShortcut(Path.Combine(desktop, "Waveplayer.lnk"), full);
        }
    }

    private static void CreateShortcut(string shortcutPath, string destination)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = Path.Combine(destination, "Waveplayer.exe");
        shortcut.WorkingDirectory = destination;
        shortcut.Description = "Waveplayer music player";
        shortcut.Save();
    }

    private void SavePreferences()
    {
        if (!_installed) throw new InvalidOperationException("Install the player before saving.");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeonWave", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonObject data;
        try { data = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new() : new(); }
        catch { data = new(); }
        data["Background"] = _theme;
        data["CustomPrimary"] = _primary;
        data["CustomSecondary"] = _secondary;
        data["MusicFolders"] = new JsonArray(_folders.Select(folder => JsonValue.Create(folder)).ToArray());
        data["MusicFolder"] = _folders.FirstOrDefault();
        data["AutomixTiming"] = _timing == "Classic" ? 1 : _timing == "Adaptive (v2)" ? 2 : 0;
        data["RemixDemoEnabled"] = _autoMix;
        data["ClassicVisualizer"] = _pulse;
        var temporary = path + ".setup-tmp";
        File.WriteAllText(temporary, data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
