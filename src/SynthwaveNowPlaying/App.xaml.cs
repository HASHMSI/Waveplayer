using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WavePlayer.ThemePreview;

namespace SynthwaveNowPlaying;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    private EventWaitHandle? _restoreSignal;
    private RegisteredWaitHandle? _restoreWait;
    private EventWaitHandle? _previewSignal;
    private RegisteredWaitHandle? _previewWait;
    private bool _ownsInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0] == "--apply-update" && int.TryParse(e.Args[2], out var previousPid))
            MainWindow = new UpdaterWindow(e.Args[1], previousPid);
        else
        {
            // Reopening the shortcut restores the hidden player instead of
            // creating a second audio engine and notification icon.
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            var labSuffix = LabData.Isolated ? "-" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(LabData.DataDirectory).ToUpperInvariant())))[..8] : "";
            var name = @"Local\WavePlayer-" + identity + labSuffix;
            _restoreSignal = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-restore");
            _previewSignal = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-mixpreview");
            _instance = new Mutex(true, name, out _ownsInstance);
            if (!_ownsInstance)
            {
                if (e.Args.Contains("--preview-automix")) _previewSignal.Set(); else _restoreSignal.Set();
                Shutdown(); return;
            }
            var settings = AppSettings.Load();
            if (!settings.ThemeChoiceMade)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var chooser = new ThemeWindow(settings.UiStyle == "Modern80s");
                if (chooser.ShowDialog() != true) { Shutdown(); return; }
                settings.UiStyle = chooser.SelectedStyle;
                settings.ThemeChoiceMade = true;
                settings.Save();
            }
            MainWindow = new MainWindow();
            ((MainWindow)MainWindow).InitializeTray();
            _restoreWait = ThreadPool.RegisterWaitForSingleObject(_restoreSignal, (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.RestoreFromTray());
            }, null, Timeout.Infinite, false);
            _previewWait = ThreadPool.RegisterWaitForSingleObject(_previewSignal, (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.PreviewMixOverlay());
            }, null, Timeout.Infinite, false);
            if (e.Args.Length == 2 && e.Args[0] == "--cleanup-updater")
                _ = UpdateClient.CleanupHelperAsync(e.Args[1]);
            else _ = UpdateClient.CleanupStaleHelpersAsync();
        }
        MainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        if (e.Args.Contains("--preview-automix") && MainWindow is MainWindow player) player.PreviewMixOverlay();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _restoreWait?.Unregister(null);
        _previewWait?.Unregister(null);
        _previewSignal?.Dispose();
        _restoreSignal?.Dispose();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

