using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace SynthwaveNowPlaying;

internal sealed partial class SettingsWindow
{
    private CheckBox _startWithWindows = null!;
    public bool InstallLatestUpdateRequested { get; private set; }
    public string? LocalUpdatePackage { get; private set; }

    private Border BuildSystemCard()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "READY WHEN WINDOWS IS", FontSize = 11,
            FontWeight = FontWeights.Bold, Foreground = Cyan, Margin = new Thickness(0, 6, 0, 12) });
        _startWithWindows = new CheckBox { IsChecked = _settings.StartWithWindows, Content = "Start with Windows",
            Foreground = Ink, FontSize = 14, Cursor = System.Windows.Input.Cursors.Hand };
        _startWithWindows.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
              <StackPanel Orientation="Horizontal">
                <Border x:Name="Track" Width="42" Height="24" CornerRadius="12" Background="#29263F" BorderBrush="#6C5C86" BorderThickness="1" Margin="0,0,12,0">
                  <Ellipse x:Name="Thumb" Fill="#B3A9C6" Width="16" Height="16" HorizontalAlignment="Left" Margin="3,0"/>
                </Border>
                <ContentPresenter VerticalAlignment="Center"/>
              </StackPanel>
              <ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True">
                  <Setter TargetName="Track" Property="Background" Value="#523366"/>
                  <Setter TargetName="Track" Property="BorderBrush" Value="#FF78D7"/>
                  <Setter TargetName="Thumb" Property="Fill" Value="#FF99E4"/>
                  <Setter TargetName="Thumb" Property="HorizontalAlignment" Value="Right"/>
                </Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Track" Property="BorderBrush" Value="#69DEFF"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        body.Children.Add(_startWithWindows);
        body.Children.Add(SystemCopy("Launch quietly in the notification area at sign-in.\nThe widget stays hidden and music stays paused until you open it.", 12));
        body.Children.Add(new Border { Height = 1, Background = Brush("#FF3C3157"), Margin = new Thickness(0, 24, 0, 22) });
        body.Children.Add(new TextBlock { Text = "UPDATE PACKAGE INSTALLER", FontSize = 11,
            FontWeight = FontWeights.Bold, Foreground = Cyan });
        body.Children.Add(new TextBlock { Text = $"WavePlayer  v{UpdateClient.InstalledVersion.ToString(3)}",
            FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 12, 0, 0) });
        body.Children.Add(SystemCopy("Automatic checks run silently at launch. Updates use verified ZIP packages with a manifest and SHA-256 hashes. Settings, music and playlists stay in place.", 8));
        var delivery = new Border { CornerRadius = new CornerRadius(10), Background = Brush("#FF191B33"),
            BorderBrush = Brush("#FF445276"), BorderThickness = new Thickness(1), Padding = new Thickness(14),
            Margin = new Thickness(0, 17, 0, 18) };
        delivery.Child = new TextBlock { Text = "ZIP + MANIFEST   →   VERIFY   →   APPLY + RESTART",
            Foreground = Cyan, FontSize = 10, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(delivery);
        var status = SystemCopy("Use a local package when updating offline.", 14);
        var actions = new WrapPanel();
        var check = ActionButton("CHECK FOR UPDATES", primary: true);
        var install = ActionButton("INSTALL UPDATE");
        install.Visibility = Visibility.Collapsed;
        install.Margin = new Thickness(8, 0, 0, 8);
        install.Click += (_, _) => { InstallLatestUpdateRequested = true; RequestClose(false); };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            status.Text = "Connecting to GitHub Releases…";
            try
            {
                var latest = await UpdateClient.FetchLatestAsync();
                var newer = latest is not null && latest.Version > UpdateClient.InstalledVersion;
                status.Text = latest is null ? "No stable manifest update package is available."
                    : newer ? $"Version {latest!.Version.ToString(3)} is available. Install to continue."
                    : "You are up to date.";
                install.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { status.Text = "Could not reach GitHub. Try again, or install a local package."; }
            finally { check.IsEnabled = true; }
        };
        check.Margin = new Thickness(0, 0, 0, 8);
        var local = ActionButton("OPEN PACKAGE ZIP");
        local.Margin = new Thickness(8, 0, 0, 8);
        local.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Install a WavePlayer manifest package", Filter = "Update package|*.zip", CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return;
            try
            {
                var manifest = UpdatePackage.ReadArchive(picker.FileName);
                if (Version.Parse(manifest.Version) <= UpdateClient.InstalledVersion)
                    throw new InvalidDataException("Choose a package newer than the installed version.");
                LocalUpdatePackage = picker.FileName;
                RequestClose(false);
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        actions.Children.Add(check);
        actions.Children.Add(local);
        actions.Children.Add(install);
        body.Children.Add(actions);
        body.Children.Add(status);
        return Card("07  /  SYSTEM & UPDATES", "Quiet startup. Seamless updates.", body);
    }

    private static TextBlock SystemCopy(string text, double margin) => new()
    {
        Text = text, Foreground = Muted, FontSize = 11, LineHeight = 19,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, margin, 0, 0)
    };
}
