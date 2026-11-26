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
    private readonly string? _localPackage;
    private readonly bool _resumeHidden;
    private bool _installedNewVersion;
    // Test seams exercise the real downloader and install workflow without
    // publishing a release or replacing the user's running installation.
    internal Func<Task<ReleasePackage?>>? ReleaseProvider { get; set; }
    internal HttpMessageHandler? DownloadHandler { get; set; }

    public UpdaterWindow(string installedExe, int previousPid, string? localPackage = null,
        bool resumeHidden = false, bool preview = false)
    {
        InitializeComponent();
        UpdateSurface.SizeChanged += (_, _) => UpdateSurface.Clip = new System.Windows.Media.RectangleGeometry(
            new Rect(0, 0, UpdateSurface.ActualWidth, UpdateSurface.ActualHeight), 16.5, 16.5);
        _installedExe = Path.GetFullPath(installedExe);
        _previousPid = previousPid;
        _localPackage = localPackage;
        _resumeHidden = resumeHidden;
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
            if (preview)
            {
                SetStage("Update Package Installer", "Preview · ZIP + manifest · SHA-256 verified files", 64);
                Detail.Text = "Full packages or smaller patches · your library stays in place";
                OpenButton.Content = "CLOSE PREVIEW";
                OpenButton.Visibility = Visibility.Visible;
            }
            else _ = RunUpdateAsync();
        };
        Closing += (_, e) => { if (_running) e.Cancel = true; };
        Closed += (_, _) => { if (!preview && !_installedNewVersion) OpenInstalledPlayer(); };
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
            if (!Path.GetFileName(_installedExe).Equals("WavePlayer.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(_installedExe))
                throw new InvalidOperationException("The installed WavePlayer executable was not found.");
            var directory = Path.GetDirectoryName(_installedExe)!;
            _ = UpdatePackage.ReadInstalled(directory)
                ?? throw new InvalidDataException("This installation has no update manifest. Install the new setup first.");
            var work = Path.GetDirectoryName(Environment.ProcessPath!)!;
            string stage;
            UpdateManifest manifest;
            Version version;
            if (_localPackage is not null)
            {
                var header = UpdatePackage.ReadArchive(_localPackage);
                version = Version.Parse(header.Version);
                if (version <= CurrentTargetVersion())
                    throw new InvalidDataException("Choose a package newer than the installed player.");
                SetStage("Verifying update package", "Checking the manifest and every SHA-256 hash", 20);
                stage = Path.Combine(work, "stage-" + Guid.NewGuid().ToString("N"));
                manifest = await Task.Run(() => UpdatePackage.Stage(_localPackage, stage, directory, version,
                    header.BaseVersion is not null));
            }
            else
            {
                SetStage("Finding the latest release", "Connecting to GitHub Releases", 0);
                var release = await (ReleaseProvider?.Invoke() ?? UpdateClient.FetchLatestAsync())
                    ?? throw new InvalidOperationException("The release does not include a verified manifest package.");
                version = release.Version;
                if (version <= CurrentTargetVersion())
                {
                    _running = false;
                    Close();
                    return;
                }
                string? prepared = null;
                UpdateManifest? verified = null;
                if (release.Patch is {} patch)
                    try { (prepared, verified) = await PrepareFilesAsync(patch, work, directory, version, true); }
                    catch (Exception ex) { Debug.WriteLine($"Patch unavailable; using full manifest package: {ex.Message}"); }
                if (verified is null)
                    (prepared, verified) = await PrepareFilesAsync(release.Files, work, directory, version, false);
                stage = prepared!;
                manifest = verified;
            }
            Detail.Text = $"Updating {CurrentTargetVersion().ToString(3)}  →  {version.ToString(3)}";
            SetStage("Installing WavePlayer", "Applying verified files · preserving settings and music", 91);
            await WaitForPreviousProcessAsync();
            await Task.Run(() => InstallFiles(stage, manifest));
            _installedNewVersion = true;
            SetStage("Update complete", "Opening WavePlayer", 100);
            await Task.Delay(500);
            _running = false;
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

    private async Task<(string Stage, UpdateManifest Manifest)> PrepareFilesAsync(UpdateAsset asset,
        string work, string installedDirectory, Version releaseVersion, bool patch)
    {
        var archivePath = Path.Combine(work, patch ? "WavePlayerPatch.zip" : "WavePlayerFiles.zip");
        await DownloadPackageAsync(asset, archivePath);
        SetStage("Preparing your new version", "Verifying every player file", 82);
        var stage = Path.Combine(work, "stage-" + Guid.NewGuid().ToString("N"));
        var manifest = await Task.Run(() => UpdatePackage.Stage(archivePath, stage, installedDirectory,
            releaseVersion, patch));
        return (stage, manifest);
    }

    private async Task DownloadPackageAsync(UpdateAsset asset, string destination)
    {
        SetStage("Downloading the update", "Secure package from GitHub Releases", 3);
        using var client = DownloadHandler is null ? new HttpClient() : new HttpClient(DownloadHandler, disposeHandler: false);
        client.Timeout = TimeSpan.FromMinutes(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WavePlayer-Updater/1.0");
        using var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
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
            if (total > asset.Size) throw new InvalidDataException("The download exceeded its declared size.");
            if (Environment.TickCount64 - lastUpdate < 100) continue;
            lastUpdate = Environment.TickCount64;
            var percent = asset.Size > 0 ? Math.Clamp(total * 78d / asset.Size, 0, 78) : 0;
            SetProgress(3 + percent);
            StatusText.Text = $"{total / 1048576d:0.0} / {asset.Size / 1048576d:0.0} MB";
        }
        await output.FlushAsync();
        if (asset.Size <= 0 || total != asset.Size ||
            !CryptographicOperations.FixedTimeEquals(sha.GetHashAndReset(), Convert.FromHexString(asset.Sha256)))
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

    private void InstallFiles(string stage, UpdateManifest manifest)
    {
        var directory = Path.GetDirectoryName(_installedExe)!;
        UpdateInstall.Apply(stage, directory, manifest, () =>
        {
            var start = new ProcessStartInfo(_installedExe) { WorkingDirectory = directory, UseShellExecute = false };
            start.ArgumentList.Add("--cleanup-updater");
            start.ArgumentList.Add(Path.GetDirectoryName(Environment.ProcessPath!)!);
            if (_resumeHidden) start.ArgumentList.Add("--resume-hidden");
            using var updated = Process.Start(start) ?? throw new IOException("The updated player could not start.");
            if (updated.WaitForExit(1500)) throw new IOException("The updated player closed during startup.");
        });
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
        Close();
    }

    private void OpenInstalledPlayer()
    {
        if (!File.Exists(_installedExe)) return;
        var start = new ProcessStartInfo(_installedExe) { UseShellExecute = false };
        start.ArgumentList.Add("--cleanup-updater");
        start.ArgumentList.Add(Path.GetDirectoryName(Environment.ProcessPath!)!);
        if (_resumeHidden) start.ArgumentList.Add("--resume-hidden");
        Process.Start(start);
    }
}
