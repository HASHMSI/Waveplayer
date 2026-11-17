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
using System.Windows.Media.Media3D;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace SynthwaveNowPlaying;

internal sealed partial class SettingsWindow : Window
{
    // Reusable visual controls and small animation helpers shared by the cards.
    private RadioButton ThemeChoice(string title, string caption, Color primary, Color secondary,
        bool selected, BitmapSource? widgetPreview)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = selected ? new SolidColorBrush(primary) : Brush("#FF403253"),
            Background = Brush(selected ? "#FF241934" : "#FF171426"),
            Padding = new Thickness(9, 9, 9, 10),
            Height = 198,
            Margin = new Thickness(0, 0, 8, 0),
            RenderTransformOrigin = new Point(.5, .5),
            RenderTransform = new ScaleTransform()
        };
        var layout = new StackPanel();
        layout.Children.Add(BuildThemePreview(primary, secondary, widgetPreview));
        var heading = new Grid { Margin = new Thickness(1, 10, 1, 0) };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Ink });
        var selectedLabel = new TextBlock
        {
            Text = "SELECTED", FontSize = 7, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(primary), VerticalAlignment = VerticalAlignment.Center,
            Visibility = selected ? Visibility.Visible : Visibility.Collapsed,
        };
        Grid.SetColumn(selectedLabel, 1);
        heading.Children.Add(selectedLabel);
        layout.Children.Add(heading);
        layout.Children.Add(new TextBlock { Text = caption, FontSize = 7, Foreground = Muted, Margin = new Thickness(1, 4, 0, 0) });
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 9, 0, 0) };
        swatches.Children.Add(new Border { Width = 28, Height = 3, Background = new SolidColorBrush(primary), CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 4, 0) });
        swatches.Children.Add(new Border { Width = 28, Height = 3, Background = new SolidColorBrush(secondary), CornerRadius = new CornerRadius(2) });
        layout.Children.Add(swatches);
        card.Child = layout;

        var radio = new RadioButton
        {
            Content = card,
            GroupName = "Background",
            IsChecked = selected,
            Focusable = true,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        radio.Template = RadioButtonTemplate();
        radio.Checked += (_, _) =>
        {
            card.BorderBrush = new SolidColorBrush(primary);
            card.Background = Brush("#FF241934");
            card.BorderThickness = new Thickness(2);
            selectedLabel.Visibility = Visibility.Visible;
            AnimateScale(card, 1.025);
        };
        radio.Unchecked += (_, _) =>
        {
            card.BorderBrush = Brush("#FF403253");
            card.Background = Brush("#FF171426");
            card.BorderThickness = new Thickness(1);
            selectedLabel.Visibility = Visibility.Collapsed;
            AnimateScale(card, 1);
        };
        radio.MouseEnter += (_, _) => AnimateScale(card, 1.025);
        radio.MouseLeave += (_, _) => AnimateScale(card, radio.IsChecked == true ? 1.015 : 1);
        AttachExpandedThemePreview(radio, title, caption, primary, secondary, widgetPreview);
        return radio;
    }

    private void AttachExpandedThemePreview(RadioButton target, string title, string caption,
        Color primary, Color secondary, BitmapSource? widgetPreview)
    {
        var content = new Border
        {
            Width = 480,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Background = Gradient("#FF201532", "#FF10182B"),
            BorderBrush = new SolidColorBrush(primary),
            BorderThickness = new Thickness(1.5),
            RenderTransformOrigin = new Point(.5, 0),
            RenderTransform = new ScaleTransform(.88, .88),
            Effect = new DropShadowEffect { Color = primary, BlurRadius = 18, ShadowDepth = 4, Opacity = .23 }
        };
        var stack = new StackPanel();
        var heading = new Grid { Margin = new Thickness(3, 0, 3, 10) };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = $"{title}  /  {caption}", Foreground = Ink, FontSize = 13, FontWeight = FontWeights.Bold });
        var hint = new TextBlock { Text = "HOVER PREVIEW", Foreground = new SolidColorBrush(secondary), FontSize = 9, FontWeight = FontWeights.Bold };
        Grid.SetColumn(hint, 1);
        heading.Children.Add(hint);
        stack.Children.Add(heading);
        var stage = new Border
        {
            Height = 238,
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, secondary.R, secondary.G, secondary.B)),
            BorderThickness = new Thickness(1),
            Background = Gradient("#FF0F1229", "#FF251437"),
            ClipToBounds = true,
            Child = BuildPreviewViewport(primary, secondary, widgetPreview)
        };
        stack.Children.Add(stage);
        stack.Children.Add(new TextBlock
        {
            Text = "CLICK THE CARD TO SELECT  ·  SAVE SETTINGS TO APPLY",
            Foreground = Muted, FontSize = 8, Margin = new Thickness(3, 10, 0, 0)
        });
        content.Child = stack;
        var popup = new Popup
        {
            Child = content,
            Placement = PlacementMode.Bottom,
            PlacementTarget = target,
            VerticalOffset = 8,
            AllowsTransparency = true,
            StaysOpen = true
        };
        _themeHoverPopups.Add(popup);
        var closeDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        var transition = 0;
        void OpenPreview()
        {
            transition++;
            closeDelay.Stop();
            var wasOpen = popup.IsOpen;
            if (!wasOpen)
            {
                content.BeginAnimation(OpacityProperty, null);
                content.Opacity = 0;
                popup.IsOpen = true;
            }
            if (!wasOpen && content.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = .88;
                scale.ScaleY = .88;
            }
            AnimateScale(content, 1);
            content.BeginAnimation(OpacityProperty, Animation(1, 220));
        }
        void ScheduleClose()
        {
            closeDelay.Stop();
            closeDelay.Start();
        }
        closeDelay.Tick += (_, _) =>
        {
            closeDelay.Stop();
            if (!popup.IsOpen) return;
            var current = ++transition;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, _) => { if (current == transition) popup.IsOpen = false; };
            content.BeginAnimation(OpacityProperty, fade);
            AnimateScale(content, .9);
        };
        target.MouseEnter += (_, _) => OpenPreview();
        target.MouseLeave += (_, _) => ScheduleClose();
        content.MouseEnter += (_, _) => { transition++; closeDelay.Stop(); AnimateScale(content, 1); content.BeginAnimation(OpacityProperty, Animation(1, 120)); };
        content.MouseLeave += (_, _) => ScheduleClose();
        target.Unloaded += (_, _) => { transition++; closeDelay.Stop(); popup.IsOpen = false; };
    }

    private static FrameworkElement BuildThemePreview(Color primary, Color secondary, BitmapSource? widgetPreview)
    {
        var frame = new Border
        {
            Height = 112,
            CornerRadius = new CornerRadius(8),
            Background = new LinearGradientBrush(Color.FromRgb(15, 17, 43), Color.FromRgb(38, 18, 58), 30),
            BorderBrush = new SolidColorBrush(Color.FromArgb(145, primary.R, primary.G, primary.B)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
        var grid = new Grid();
        grid.Children.Add(BuildPreviewViewport(primary, secondary, widgetPreview));
        var live = new TextBlock
        {
            Text = "●  WIDGET VIEW", FontSize = 6, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(secondary),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(7, 7, 0, 0)
        };
        grid.Children.Add(live);
        frame.Child = grid;
        return frame;
    }

    private static Viewport3D BuildPreviewViewport(Color primary, Color secondary, BitmapSource? widgetPreview)
    {
        // The material is a real snapshot of MainWindow, color-tinted per theme.
        var bounds = new Rect(0, 0, widgetPreview?.PixelWidth ?? 410, widgetPreview?.PixelHeight ?? 153);
        var drawing = new DrawingGroup();
        if (widgetPreview is not null)
            drawing.Children.Add(new ImageDrawing(widgetPreview, bounds));
        else
        {
            drawing.Children.Add(new GeometryDrawing(Brush("#FF111629"), null, new RectangleGeometry(bounds)));
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(primary), null, new RectangleGeometry(new Rect(12, 20, 83, 83))));
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(secondary), null, new RectangleGeometry(new Rect(112, 65, 270, 4))));
        }
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(42, primary.R, primary.G, primary.B)),
            null, new RectangleGeometry(bounds)));
        drawing.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(215, secondary.R, secondary.G, secondary.B)),
            null, new RectangleGeometry(new Rect(0, bounds.Height - Math.Max(3, bounds.Height * .025), bounds.Width, Math.Max(3, bounds.Height * .025)))));
        var texture = new DrawingBrush(drawing)
        {
            Viewbox = bounds,
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
        var mesh = new MeshGeometry3D
        {
            Positions = [new Point3D(-1.34, .5, 0), new Point3D(1.34, .5, 0), new Point3D(1.34, -.5, 0), new Point3D(-1.34, -.5, 0)],
            TextureCoordinates = [new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1)],
            TriangleIndices = [0, 1, 2, 0, 2, 3]
        };
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(texture));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), 40));
        var model = new GeometryModel3D { Geometry = mesh, Material = material, BackMaterial = material };
        var tilt = new Transform3DGroup();
        tilt.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -16)));
        tilt.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 8)));
        model.Transform = tilt;
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(220, 220, 232)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(255, 246, 255), new Vector3D(-1, -1, -2)));
        scene.Children.Add(model);
        var viewport = new Viewport3D
        {
            Camera = new PerspectiveCamera(new Point3D(0, 0, 4.2), new Vector3D(0, 0, -4.2), new Vector3D(0, 1, 0), 42),
            ClipToBounds = false
        };
        viewport.Children.Add(new ModelVisual3D { Content = scene });
        viewport.Effect = new DropShadowEffect { Color = primary, BlurRadius = 14, ShadowDepth = 3, Opacity = .4 };
        return viewport;
    }

    private static ControlTemplate RadioButtonTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        return new ControlTemplate(typeof(RadioButton)) { VisualTree = presenter };
    }

    private static Slider SliderControl(double minimum, double maximum, double value)
    {
        return new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            Height = 25,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Accent,
            Background = Brush("#FF332641"),
            Cursor = Cursors.Hand,
            IsMoveToPointEnabled = true,
            TickFrequency = maximum > 1 ? .1 : .05,
            SnapsToDevicePixels = true
        };
    }

    private static Button ActionButton(string text, bool primary = false, bool compact = false)
    {
        var normal = Brush(primary ? "#FF66317C" : "#FF211A34");
        var hover = Brush(primary ? "#FF8944A4" : "#FF302249");
        var border = Brush(primary ? "#FFFF76D9" : "#FF714F91");
        var button = new Button
        {
            Content = text,
            FontFamily = new FontFamily("Bahnschrift"),
            FontSize = compact ? 14 : 9,
            FontWeight = FontWeights.SemiBold,
            Padding = compact ? new Thickness(0) : new Thickness(12, 7, 12, 7),
            MinWidth = compact ? 30 : 76,
            MinHeight = compact ? 30 : 31,
            Margin = new Thickness(0, 0, 0, 0),
            Background = normal,
            Foreground = Ink,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Focusable = true,
            RenderTransformOrigin = new Point(.5, .5),
            RenderTransform = new ScaleTransform()
        };
        button.Template = ButtonTemplate();
        button.MouseEnter += (_, _) => { button.Background = hover; button.BorderBrush = Accent; AnimateScale(button, 1.035); };
        button.MouseLeave += (_, _) => { button.Background = normal; button.BorderBrush = border; AnimateScale(button, 1); };
        return button;
    }

    private static ControlTemplate ButtonTemplate()
    {
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        frame.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        frame.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        frame.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness)) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.MarginProperty, new Binding(nameof(Control.Padding)) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        frame.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = frame };
    }

    private static DoubleAnimation Animation(double to, int milliseconds, int delay = 0) => new(to, TimeSpan.FromMilliseconds(milliseconds))
    {
        BeginTime = TimeSpan.FromMilliseconds(delay),
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
    };

    private static void AnimateScale(FrameworkElement element, double scale)
    {
        if (element.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform();
            element.RenderTransformOrigin = new Point(.5, .5);
            element.RenderTransform = transform;
        }
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(135)) { EasingFunction = easing });
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(135)) { EasingFunction = easing });
    }
}
