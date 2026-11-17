using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SynthwaveNowPlaying;

internal sealed record UpdateAsset(Uri DownloadUrl, long Size, string Sha256);
internal sealed record ReleasePackage(Version Version, string Tag, UpdateAsset? Files, UpdateAsset? Patch,
    UpdateAsset? Legacy);

internal static class UpdateClient
{
    private const string LatestRelease = "https://api.github.com/repos/HASHMSI/Waveplayer/releases/latest";
    private static readonly HttpClient Client = CreateClient();
    internal static Version InstalledVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Waveplayer-Updater/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal static async Task<ReleasePackage?> FetchLatestAsync(CancellationToken token = default)
    {
        using var response = await Client.GetAsync(LatestRelease, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var release = json.RootElement;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        UpdateAsset? FindAsset(string name)
        {
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != name ||
                    asset.GetProperty("state").GetString() != "uploaded") continue;
                var address = asset.GetProperty("browser_download_url").GetString();
                var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
                if (address is null || digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
                if (!Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
                    url.Host != "github.com" || !url.AbsolutePath.StartsWith($"/HASHMSI/Waveplayer/releases/download/{tag}/", StringComparison.Ordinal)) return null;
                return new UpdateAsset(url, asset.GetProperty("size").GetInt64(), digest[7..]);
            }
            return null;
        }
        var files = FindAsset("WaveplayerFiles.zip");
        var patch = FindAsset("WaveplayerPatch.zip");
        var legacy = FindAsset("WaveplayerPortable.zip");
        return files is null && legacy is null ? null : new ReleasePackage(version, tag, files, patch, legacy);
    }

    internal static async Task CheckOnLaunchAsync(MainWindow window)
    {
        // A VS Code build has no install manifest and must never update itself.
        var installDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (installDirectory is null || (File.Exists(Path.Combine(installDirectory, "Waveplayer.dll")) &&
            !File.Exists(Path.Combine(installDirectory, UpdatePackage.ManifestName)))) return;
        string? helperDirectory = null;
        try
        {
            var release = await FetchLatestAsync();
            var needsNewVersion = release is not null && release.Version > InstalledVersion;
            var needsLayoutMigration = release is not null && release.Version == InstalledVersion &&
                release.Files is not null && !File.Exists(Path.Combine(installDirectory, UpdatePackage.ManifestName));
            if ((!needsNewVersion && !needsLayoutMigration) || !window.IsLoaded) return;
            // The legacy updater observes its newly started player for 1.5 s.
            // Let that check finish before the bridge closes for layout migration.
            if (needsLayoutMigration)
            {
                await Task.Delay(3000);
                if (!window.IsLoaded || window.Dispatcher.HasShutdownStarted) return;
            }
            var installedExe = Environment.ProcessPath;
            if (installedExe is null || !File.Exists(installedExe)) return;
            helperDirectory = Path.Combine(Path.GetTempPath(), "WaveplayerUpdater", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(helperDirectory);
            // A multi-file apphost loads Waveplayer.dll beside Waveplayer.exe.
            var helperExe = Path.Combine(helperDirectory, "Waveplayer.exe");
            await Task.Run(() =>
            {
                var installedManifest = UpdatePackage.ReadInstalled(installDirectory);
                if (installedManifest is null) File.Copy(installedExe, helperExe);
                else
                {
                    foreach (var file in installedManifest.Files)
                    {
                        var relative = file.Path.Replace('/', Path.DirectorySeparatorChar);
                        var destination = Path.Combine(helperDirectory, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(Path.Combine(installDirectory, relative), destination);
                    }
                    File.Copy(Path.Combine(installDirectory, UpdatePackage.ManifestName),
                        Path.Combine(helperDirectory, UpdatePackage.ManifestName));
                }
            });
            var start = new ProcessStartInfo(helperExe) { UseShellExecute = false };
            start.ArgumentList.Add("--apply-update");
            start.ArgumentList.Add(installedExe);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            if (Process.Start(start) is not null)
            {
                helperDirectory = null;
                window.Close();
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Update check skipped: {ex}"); }
        finally
        {
            if (helperDirectory is not null)
                try { Directory.Delete(helperDirectory, recursive: true); } catch (IOException) { }
        }
    }

    internal static async Task CleanupStaleHelpersAsync()
    {
        await Task.Delay(2000);
        var root = Path.Combine(Path.GetTempPath(), "WaveplayerUpdater");
        if (!Directory.Exists(root)) return;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _) ||
                Directory.GetCreationTimeUtc(folder) > DateTime.UtcNow.AddHours(-1)) continue;
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static async Task CleanupHelperAsync(string path)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WaveplayerUpdater"));
        var candidate = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(candidate), root, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(candidate), "N", out _)) return;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(1000);
            try { Directory.Delete(candidate, recursive: true); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
