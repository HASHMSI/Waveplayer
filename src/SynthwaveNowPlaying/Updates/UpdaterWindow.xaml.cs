using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SynthwaveNowPlaying;

public partial class UpdaterWindow : Window
{
    private readonly string _installedExe;
    private readonly int _previousPid;
    private bool _running;

    public UpdaterWindow(string installedExe, int previousPid)
    {
        InitializeComponent();
        _installedExe = Path.GetFullPath(installedExe);
        _previousPid = previousPid;
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
            _ = RunUpdateAsync();
        };
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private async Task RunUpdateAsync()
    {
        if (_running) return;
        _running = true;
        RetryButton.Visibility = Visibility.Collapsed;
        OpenButton.Visibility = Visibility.Collapsed;
        try
        {
            SetStage("Finding the latest release", "Connecting to GitHub Releases", 0);
            var release = await UpdateClient.FetchLatestAsync()
                ?? throw new InvalidOperationException("The release does not include a verified WaveplayerPortable.zip package.");
            if (Path.GetFileName(_installedExe) != "Waveplayer.exe" || !File.Exists(_installedExe))
                throw new InvalidOperationException("The installed Waveplayer executable was not found.");
            if (release.Version <= CurrentTargetVersion())
            {
                OpenInstalledPlayer();
                Close();
                return;
            }
            Detail.Text = $"Updating {CurrentTargetVersion().ToString(3)}  →  {release.Version.ToString(3)}";
            var work = Path.GetDirectoryName(Environment.ProcessPath!)!;
            var archivePath = Path.Combine(work, "WaveplayerPortable.zip");
            await DownloadPackageAsync(release, archivePath);
            SetStage("Preparing your new version", "Download verified · unpacking files", 82);
            var stage = Path.Combine(work, "stage");
            Directory.CreateDirectory(stage);
            var stagedExe = Path.Combine(stage, "Waveplayer.exe");
            var stagedClassic = Path.Combine(stage, "SynthwaveNowPlaying.dll");
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var player = archive.GetEntry("Waveplayer.exe") ?? throw new InvalidDataException("Player missing from update package.");
                var classic = archive.GetEntry("Classic/SynthwaveNowPlaying.dll") ?? throw new InvalidDataException("Visualizer missing from update package.");
                player.ExtractToFile(stagedExe, overwrite: true);
                classic.ExtractToFile(stagedClassic, overwrite: true);
            }
            var fileVersion = FileVersionInfo.GetVersionInfo(stagedExe);
            var packagedVersion = new Version(fileVersion.FileMajorPart, fileVersion.FileMinorPart, fileVersion.FileBuildPart);
            if (packagedVersion != release.Version)
                throw new InvalidDataException("Package version does not match the release.");
            SetStage("Installing Waveplayer", "Replacing the player while your settings remain intact", 91);
            await WaitForPreviousProcessAsync();
            InstallPackage(stagedExe, stagedClassic);
            SetStage("Update complete", "Opening Waveplayer", 100);
            await Task.Delay(500);
            Close();
        }
        catch (Exception ex)
        {
            Heading.Text = "Update paused";
            Detail.Text = ex.Message;
            StatusText.Text = "Your current player is still available";
            RetryButton.Visibility = Visibility.Visible;
            OpenButton.Visibility = Visibility.Visible;
        }
        finally { _running = false; }
    }

    private async Task DownloadPackageAsync(ReleasePackage release, string destination)
    {
        SetStage("Downloading the update", "Secure package from GitHub Releases", 3);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Waveplayer-Updater/1.0");
        using var response = await client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 131072, useAsync: true);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072];
        long total = 0;
        var lastUpdate = Environment.TickCount64;
        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read));
            sha.AppendData(buffer, 0, read);
            total += read;
            if (Environment.TickCount64 - lastUpdate < 100) continue;
            lastUpdate = Environment.TickCount64;
            var percent = release.Size > 0 ? Math.Clamp(total * 78d / release.Size, 0, 78) : 0;
            SetProgress(3 + percent);
            StatusText.Text = $"{total / 1048576d:0.0} / {release.Size / 1048576d:0.0} MB";
        }
        await output.FlushAsync();
        if (release.Size <= 0 || total != release.Size ||
            !CryptographicOperations.FixedTimeEquals(sha.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
            throw new InvalidDataException("The download did not pass SHA-256 verification.");
    }

    private async Task WaitForPreviousProcessAsync()
    {
        try
        {
            using var previous = Process.GetProcessById(_previousPid);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await previous.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException) { /* The player already exited. */ }
    }

    private Version CurrentTargetVersion()
    {
        var info = FileVersionInfo.GetVersionInfo(_installedExe);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
    }

    private void InstallPackage(string stagedExe, string stagedClassic)
    {
        var directory = Path.GetDirectoryName(_installedExe)!;
        var classicDirectory = Path.Combine(directory, "Classic");
        Directory.CreateDirectory(classicDirectory);
        var targetClassic = Path.Combine(classicDirectory, "SynthwaveNowPlaying.dll");
        var backupExe = _installedExe + ".previous";
        var backupClassic = targetClassic + ".previous";
        var hadClassic = File.Exists(targetClassic);
        try
        {
            File.Move(_installedExe, backupExe, overwrite: true);
            if (hadClassic) File.Move(targetClassic, backupClassic, overwrite: true);
            File.Copy(stagedExe, _installedExe);
            File.Copy(stagedClassic, targetClassic);
            var start = new ProcessStartInfo(_installedExe) { WorkingDirectory = directory, UseShellExecute = false };
            start.ArgumentList.Add("--cleanup-updater");
            start.ArgumentList.Add(Path.GetDirectoryName(Environment.ProcessPath!)!);
            using var updated = Process.Start(start) ?? throw new IOException("The updated player could not start.");
            if (updated.WaitForExit(1500)) throw new IOException("The updated player closed during startup.");
            try
            {
                File.Delete(backupExe);
                if (hadClassic) File.Delete(backupClassic);
            }
            catch (IOException) { /* The next update may remove a stale backup. */ }
        }
        catch
        {
            if (File.Exists(backupExe)) File.Move(backupExe, _installedExe, overwrite: true);
            if (hadClassic && File.Exists(backupClassic)) File.Move(backupClassic, targetClassic, overwrite: true);
            throw;
        }
    }

    private void SetStage(string heading, string status, double percent)
    {
        Heading.Text = heading;
        StatusText.Text = status;
        SetProgress(percent);
    }

    private void SetProgress(double percent)
    {
        PercentText.Text = $"{percent:0}%";
        var target = Math.Max(0, ProgressTrack.ActualWidth * percent / 100);
        ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(ProgressFill.ActualWidth, target, TimeSpan.FromMilliseconds(180)));
    }

    private void Retry_Click(object sender, RoutedEventArgs e) => _ = RunUpdateAsync();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        OpenInstalledPlayer();
        Close();
    }

    private void OpenInstalledPlayer()
    {
        if (!File.Exists(_installedExe)) return;
        var start = new ProcessStartInfo(_installedExe) { UseShellExecute = false };
        start.ArgumentList.Add("--cleanup-updater");
        start.ArgumentList.Add(Path.GetDirectoryName(Environment.ProcessPath!)!);
        Process.Start(start);
    }
}
