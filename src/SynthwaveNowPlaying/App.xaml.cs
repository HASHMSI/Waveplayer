using System.Configuration;
using System.Data;
using System.Windows;

namespace SynthwaveNowPlaying;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0] == "--apply-update" && int.TryParse(e.Args[2], out var previousPid))
            MainWindow = new UpdaterWindow(e.Args[1], previousPid);
        else
        {
            MainWindow = new MainWindow();
            if (e.Args.Length == 2 && e.Args[0] == "--cleanup-updater")
                _ = UpdateClient.CleanupHelperAsync(e.Args[1]);
            else _ = UpdateClient.CleanupStaleHelpersAsync();
        }
        MainWindow.Show();
    }
}

