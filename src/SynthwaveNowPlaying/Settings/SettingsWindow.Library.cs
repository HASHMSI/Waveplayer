using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SynthwaveNowPlaying;

internal sealed partial class SettingsWindow
{
    private void AddMusicFolderRow(string path)
    {
        var field = new TextBox { Text = path, VerticalContentAlignment = VerticalAlignment.Center };
        StyleBox(field);
        field.ToolTip = "Music folder path";
        _folders.Add(field);

        var row = new Grid { Margin = new Thickness(0, 0, 0, 8), Opacity = 0 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(field);
        var browse = ActionButton("BROWSE");
        browse.Tag = field;
        browse.Margin = new Thickness(8, 0, 0, 0);
        browse.Click += BrowseFolder;
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        var remove = ActionButton("×", compact: true);
        remove.ToolTip = "Remove this music location";
        remove.Margin = new Thickness(6, 0, 0, 0);
        remove.Click += (_, _) =>
        {
            _folders.Remove(field);
            row.IsHitTestVisible = false;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170));
            fade.Completed += (_, _) => _folderRows.Children.Remove(row);
            row.BeginAnimation(OpacityProperty, fade);
        };
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);
        _folderRows.Children.Add(row);
        row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
