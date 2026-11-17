using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SynthwaveNowPlaying;

// A compact WPF color picker: hue, saturation/value, curated swatches, and exact hex.
internal sealed class ColorPickerPanel : Border
{
    private static readonly Color[] Swatches =
    [
        Color.FromRgb(255, 62, 189), Color.FromRgb(255, 95, 116),
        Color.FromRgb(255, 177, 100), Color.FromRgb(255, 224, 114),
        Color.FromRgb(105, 229, 172), Color.FromRgb(61, 220, 255),
        Color.FromRgb(103, 126, 255), Color.FromRgb(186, 107, 244),
        Color.FromRgb(255, 143, 209), Color.FromRgb(234, 77, 131),
        Color.FromRgb(255, 141, 65), Color.FromRgb(198, 241, 97),
        Color.FromRgb(68, 190, 169), Color.FromRgb(60, 163, 233),
        Color.FromRgb(91, 93, 213), Color.FromRgb(129, 69, 177)
    ];

    private readonly Border _sample = new();
    private readonly TextBox _hex = new();
    private readonly Grid _colorField = new();
    private readonly Grid _hueField = new();
    private readonly Ellipse _colorMarker = new();
    private readonly Border _hueMarker = new();
    private Color _selected;
    private double _hue;
    private double _saturation;
    private double _value;
    private bool _draggingColor;
    private bool _draggingHue;

    public Color SelectedColor => _selected;
    public event Action<Color>? ColorChanged;

    public ColorPickerPanel(string title, Color initial)
    {
        CornerRadius = new CornerRadius(10);
        BorderBrush = new SolidColorBrush(Color.FromRgb(70, 53, 96));
        BorderThickness = new Thickness(1);
        Background = new SolidColorBrush(Color.FromRgb(22, 19, 42));
        Padding = new Thickness(11);
        Margin = new Thickness(0, 0, 9, 0);

        var layout = new StackPanel();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        _sample.Width = 24;
        _sample.Height = 24;
        _sample.CornerRadius = new CornerRadius(6);
        _sample.BorderBrush = Brushes.White;
        _sample.BorderThickness = new Thickness(1);
        header.Children.Add(_sample);
        header.Children.Add(new TextBlock
        {
            Text = title, Foreground = Brushes.White, FontSize = 9,
            FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        });
        Grid.SetColumn(header.Children[^1], 1);
        _hex.Height = 25;
        _hex.Padding = new Thickness(6, 4, 6, 3);
        _hex.Background = new SolidColorBrush(Color.FromRgb(13, 13, 30));
        _hex.Foreground = Brushes.White;
        _hex.CaretBrush = Brushes.White;
        _hex.BorderBrush = new SolidColorBrush(Color.FromRgb(83, 67, 113));
        _hex.BorderThickness = new Thickness(1);
        _hex.FontSize = 9;
        _hex.ToolTip = "Type any #RRGGBB color";
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitHex(); Keyboard.ClearFocus(); e.Handled = true; } };
        _hex.LostKeyboardFocus += (_, _) => CommitHex();
        Grid.SetColumn(_hex, 2);
        header.Children.Add(_hex);
        layout.Children.Add(header);

        _colorField.Height = 104;
        _colorField.Cursor = Cursors.Cross;
        _colorField.ClipToBounds = true;
        _colorField.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                GradientStops = [new GradientStop(Colors.White, 0), new GradientStop(Colors.Transparent, 1)]
            }, IsHitTestVisible = false
        });
        _colorField.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 1)]
            }, IsHitTestVisible = false
        });
        var colorMarkers = new Canvas { IsHitTestVisible = false };
        _colorMarker.Width = 12;
        _colorMarker.Height = 12;
        _colorMarker.Stroke = Brushes.White;
        _colorMarker.StrokeThickness = 2;
        _colorMarker.Fill = Brushes.Transparent;
        colorMarkers.Children.Add(_colorMarker);
        _colorField.Children.Add(colorMarkers);
        _colorField.MouseLeftButtonDown += (_, e) => { _draggingColor = true; _colorField.CaptureMouse(); UpdateColorPointer(e.GetPosition(_colorField)); e.Handled = true; };
        _colorField.MouseMove += (_, e) => { if (_draggingColor) UpdateColorPointer(e.GetPosition(_colorField)); };
        _colorField.MouseLeftButtonUp += (_, _) => { _draggingColor = false; _colorField.ReleaseMouseCapture(); };
        _colorField.LostMouseCapture += (_, _) => _draggingColor = false;
        _colorField.SizeChanged += (_, _) => PositionMarkers();
        layout.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6), BorderBrush = new SolidColorBrush(Color.FromRgb(84, 70, 111)),
            BorderThickness = new Thickness(1), ClipToBounds = true, Child = _colorField
        });

        _hueField.Height = 13;
        _hueField.Margin = new Thickness(0, 9, 0, 11);
        _hueField.Cursor = Cursors.Hand;
        _hueField.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops =
            [
                new GradientStop(Colors.Red, 0), new GradientStop(Colors.Yellow, 1d / 6),
                new GradientStop(Colors.Lime, 2d / 6), new GradientStop(Colors.Cyan, 3d / 6),
                new GradientStop(Colors.Blue, 4d / 6), new GradientStop(Colors.Magenta, 5d / 6),
                new GradientStop(Colors.Red, 1)
            ]
        };
        var hueMarkers = new Canvas { IsHitTestVisible = false };
        _hueMarker.Width = 5;
        _hueMarker.Height = 17;
        _hueMarker.BorderBrush = Brushes.White;
        _hueMarker.BorderThickness = new Thickness(2);
        _hueMarker.Background = Brushes.Transparent;
        hueMarkers.Children.Add(_hueMarker);
        _hueField.Children.Add(hueMarkers);
        _hueField.MouseLeftButtonDown += (_, e) => { _draggingHue = true; _hueField.CaptureMouse(); UpdateHuePointer(e.GetPosition(_hueField)); e.Handled = true; };
        _hueField.MouseMove += (_, e) => { if (_draggingHue) UpdateHuePointer(e.GetPosition(_hueField)); };
        _hueField.MouseLeftButtonUp += (_, _) => { _draggingHue = false; _hueField.ReleaseMouseCapture(); };
        _hueField.LostMouseCapture += (_, _) => _draggingHue = false;
        _hueField.SizeChanged += (_, _) => PositionMarkers();
        layout.Children.Add(_hueField);

        var palette = new UniformGrid { Columns = 8 };
        foreach (var swatch in Swatches)
        {
            var tile = new Border
            {
                Height = 21, CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(swatch),
                BorderBrush = new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand,
                ToolTip = CustomPalette.Hex(swatch)
            };
            tile.MouseLeftButtonDown += (_, e) => { SetColor(swatch); e.Handled = true; };
            palette.Children.Add(tile);
        }
        layout.Children.Add(palette);
        Child = layout;
        SetColor(initial, notify: false);
    }

    private void UpdateColorPointer(Point point)
    {
        if (_colorField.ActualWidth < 1 || _colorField.ActualHeight < 1) return;
        _saturation = Math.Clamp(point.X / _colorField.ActualWidth, 0, 1);
        _value = 1 - Math.Clamp(point.Y / _colorField.ActualHeight, 0, 1);
        ApplyHsv();
    }

    private void UpdateHuePointer(Point point)
    {
        if (_hueField.ActualWidth < 1) return;
        _hue = Math.Clamp(point.X / _hueField.ActualWidth, 0, 1) * 360;
        ApplyHsv();
    }

    private void ApplyHsv()
    {
        _selected = FromHsv(_hue, _saturation, _value);
        _sample.Background = new SolidColorBrush(_selected);
        _hex.Text = CustomPalette.Hex(_selected);
        _colorField.Background = new SolidColorBrush(FromHsv(_hue, 1, 1));
        PositionMarkers();
        ColorChanged?.Invoke(_selected);
    }

    private void SetColor(Color color, bool notify = true)
    {
        _selected = Color.FromRgb(color.R, color.G, color.B);
        ToHsv(_selected, out _hue, out _saturation, out _value);
        _sample.Background = new SolidColorBrush(_selected);
        _hex.Text = CustomPalette.Hex(_selected);
        _hex.BorderBrush = new SolidColorBrush(Color.FromRgb(83, 67, 113));
        _colorField.Background = new SolidColorBrush(FromHsv(_hue, 1, 1));
        PositionMarkers();
        if (notify) ColorChanged?.Invoke(_selected);
    }

    private void CommitHex()
    {
        var parsed = CustomPalette.Parse(_hex.Text, _selected);
        if (CustomPalette.Hex(parsed).Equals(_hex.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            SetColor(parsed);
        else
        {
            _hex.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 95, 116));
            _hex.Text = CustomPalette.Hex(_selected);
        }
    }

    private void PositionMarkers()
    {
        Canvas.SetLeft(_colorMarker, _saturation * _colorField.ActualWidth - _colorMarker.Width / 2);
        Canvas.SetTop(_colorMarker, (1 - _value) * _colorField.ActualHeight - _colorMarker.Height / 2);
        Canvas.SetLeft(_hueMarker, _hue / 360 * _hueField.ActualWidth - _hueMarker.Width / 2);
        Canvas.SetTop(_hueMarker, -2);
    }

    private static void ToHsv(Color color, out double hue, out double saturation, out double value)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        value = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = value - min;
        saturation = value == 0 ? 0 : delta / value;
        hue = delta == 0 ? 0 : value == r ? 60 * (((g - b) / delta) % 6)
            : value == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x), < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        var offset = value - chroma;
        return Color.FromRgb((byte)Math.Round((r + offset) * 255),
            (byte)Math.Round((g + offset) * 255), (byte)Math.Round((b + offset) * 255));
    }
}
