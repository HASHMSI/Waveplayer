using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace WavePlayer.ThemePreview;

public partial class ThemeWindow : Window
{
    private bool _modern;
    private int _hoverRevision;
    private bool _closing;
    private bool _allowClose;
    private bool? _result;
    public string SelectedStyle => _modern ? "Modern80s" : "NeonDrive";

    public ThemeWindow(bool modern = false)
    {
        _modern = modern;
        InitializeComponent();
        Icon = BrandIcon.Source = Asset("WavePlayer.ico");
        RetroScreenshot.Background = new ImageBrush(Asset("Classic80s.jpg")) { Stretch = Stretch.UniformToFill };
        ModernScreenshot.Background = new ImageBrush(Asset("Modern80s.jpg")) { Stretch = Stretch.UniformToFill };
        // A layered, borderless window avoids the white native resize frame.
        // Border.CornerRadius alone does not clip its children in WPF.
        AtmosphereSurface.SizeChanged += (_, _) => AtmosphereSurface.Clip =
            new RectangleGeometry(new Rect(0, 0, AtmosphereSurface.ActualWidth, AtmosphereSurface.ActualHeight), 17, 17);
        foreach (var card in new[] { RetroCard, ModernCard })
            if (card.Child is FrameworkElement surface)
                surface.SizeChanged += (_, _) => surface.Clip = new RectangleGeometry(
                    new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), 14, 14);
        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; Finish(null); } };
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(460)));
            UpdateSelection();
        };
    }

    // Shared by setup and player; resolve images against the owning assembly.
    private static BitmapImage Asset(string name) => new(new Uri(
        $"pack://application:,,,/{typeof(ThemeWindow).Assembly.GetName().Name};component/Assets/{name}"));

    private static void Animate(System.Windows.Media.Animation.Animatable target, DependencyProperty property, double value, int ms = 230)
    {
        target.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Card_Enter(object sender, MouseEventArgs e)
    {
        if (sender is not Border card) return;
        var retro = card == RetroCard;
        var scale = retro ? RetroScale : ModernScale;
        var shift = retro ? RetroShift : ModernShift;
        var shadow = retro ? RetroShadow : ModernShadow;
        Panel.SetZIndex(card, 4);
        Animate(scale, ScaleTransform.ScaleXProperty, 1.045, 300);
        Animate(scale, ScaleTransform.ScaleYProperty, 1.045, 300);
        Animate(shift, TranslateTransform.YProperty, -8, 300);
        Animate(shadow, System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, .54, 300);
        ShowPreview(retro);
    }

    private void Card_Move(object sender, MouseEventArgs e)
    {
        if (sender is not Border card) return;
        var point = e.GetPosition(card);
        var normalized = Math.Clamp(point.X / Math.Max(1, card.ActualWidth), 0, 1) * 2 - 1;
        var skew = card == RetroCard ? RetroSkew : ModernSkew;
        var shift = card == RetroCard ? RetroShift : ModernShift;
        Animate(skew, SkewTransform.AngleYProperty, normalized * -2.5, 110);
        Animate(shift, TranslateTransform.XProperty, normalized * 4, 110);
        if (HoverPreview.Visibility == Visibility.Visible)
        {
            var vertical = Math.Clamp(point.Y / Math.Max(1, card.ActualHeight), 0, 1) * 2 - 1;
            Animate(PreviewSkew, SkewTransform.AngleYProperty, normalized * -1.9, 160);
            Animate(PreviewSkew, SkewTransform.AngleXProperty, vertical * 1.1, 160);
            Animate(PreviewShift, TranslateTransform.XProperty, normalized * 5, 160);
        }
    }

    private void Card_Leave(object sender, MouseEventArgs e)
    {
        if (sender is not Border card) return;
        var retro = card == RetroCard;
        var scale = retro ? RetroScale : ModernScale;
        var skew = retro ? RetroSkew : ModernSkew;
        var shift = retro ? RetroShift : ModernShift;
        var shadow = retro ? RetroShadow : ModernShadow;
        Animate(scale, ScaleTransform.ScaleXProperty, 1, 300);
        Animate(scale, ScaleTransform.ScaleYProperty, 1, 300);
        Animate(skew, SkewTransform.AngleYProperty, 0, 300);
        Animate(shift, TranslateTransform.XProperty, 0, 300);
        Animate(shift, TranslateTransform.YProperty, 0, 300);
        Animate(shadow, System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, retro ? .28 : .12, 300);
        Panel.SetZIndex(card, 0);
        HidePreview();
    }

    private void ShowPreview(bool retro)
    {
        ++_hoverRevision;
        PreviewTitle.Text = retro ? "80s  /  NEON DRIVE" : "80s  /  MODERN";
        PreviewTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(retro ? "#FFD3F1" : "#9CE8F0")!);
        PreviewPanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(retro ? "#FF83D5" : "#76D9E5")!);
        PreviewPanel.HorizontalAlignment = retro ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        PreviewPanel.Margin = retro ? new Thickness(40, 0, 0, 0) : new Thickness(0, 0, 40, 0);
        if (PreviewPanel.Effect is System.Windows.Media.Effects.DropShadowEffect glow)
            glow.Color = (Color)ColorConverter.ConvertFromString(retro ? "#F363D3" : "#69DCEB")!;
        PreviewScreenshot.Background = new ImageBrush(Asset(retro ? "Classic80s.jpg" : "Modern80s.jpg"))
        { Stretch = Stretch.UniformToFill };
        HoverPreview.Visibility = Visibility.Visible;
        HoverPreview.BeginAnimation(OpacityProperty, null);
        HoverPreview.Opacity = 0;
        PreviewScale.ScaleX = PreviewScale.ScaleY = .96;
        PreviewShift.Y = 6;
        HoverPreview.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(230))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        Animate(PreviewScale, ScaleTransform.ScaleXProperty, 1, 260);
        Animate(PreviewScale, ScaleTransform.ScaleYProperty, 1, 260);
        Animate(PreviewShift, TranslateTransform.YProperty, 0, 260);
    }

    private void HidePreview()
    {
        var revision = ++_hoverRevision;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(190))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            if (revision == _hoverRevision) HoverPreview.Visibility = Visibility.Collapsed;
        };
        HoverPreview.BeginAnimation(OpacityProperty, fade);
        Animate(PreviewScale, ScaleTransform.ScaleXProperty, .97, 190);
        Animate(PreviewScale, ScaleTransform.ScaleYProperty, .97, 190);
        Animate(PreviewSkew, SkewTransform.AngleXProperty, 0, 190);
        Animate(PreviewSkew, SkewTransform.AngleYProperty, 0, 190);
        Animate(PreviewShift, TranslateTransform.YProperty, 6, 190);
    }

    private void Card_Click(object sender, MouseButtonEventArgs e) => Select(sender == ModernCard);
    private void Card_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        Select(sender == ModernCard);
        e.Handled = true;
    }
    private void Select(bool modern) { _modern = modern; UpdateSelection(); }
    private void UpdateSelection()
    {
        RetroCheck.Text = _modern ? "○" : "✓";
        ModernCheck.Text = _modern ? "✓" : "○";
        SelectionText.Text = _modern ? "02 / 02   ·   Modern 80s selected" : "01 / 02   ·   80s selected";
        RetroCard.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_modern ? "#865A82" : "#FF73C7")!);
        ModernCard.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_modern ? "#75D7E6" : "#65748E")!);
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Finish(null);
    private void Back_Click(object sender, RoutedEventArgs e) => Finish(false);
    private void Continue_Click(object sender, RoutedEventArgs e) => Finish(true);
    private void Finish(bool? result)
    {
        if (_closing) return;
        _closing = true;
        _result = result;
        IsHitTestVisible = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            _allowClose = true;
            if (_result is bool choice) DialogResult = choice;
            else Close();
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
