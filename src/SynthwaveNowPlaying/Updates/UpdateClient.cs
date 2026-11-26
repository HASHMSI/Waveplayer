using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SynthwaveNowPlaying;

internal sealed record UpdateAsset(Uri DownloadUrl, long Size, string Sha256);
internal sealed record ReleasePackage(Version Version, string Tag, UpdateAsset Files, UpdateAsset? Patch);

internal static class UpdateClient
{
    private const string Releases = "https://api.github.com/repos/HASHMSI/WavePlayer/releases";
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WavePlayer-Updater/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal static async Task<ReleasePackage?> FetchLatestAsync(CancellationToken token = default)
    {
        ReleasePackage? newest = null;
        // GitHub's Latest label is a download-page choice, not our update feed.
        // Follow pagination because releases can be published out of version order.
        string? address = Releases + "?per_page=100";
        while (address is not null)
        {
            using var response = await Client.GetAsync(address, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            newest = FindNewestManifest(json.RootElement, newest);
            address = null;
            if (response.Headers.TryGetValues("Link", out var links))
                foreach (var link in string.Join(",", links).Split(','))
                {
                    var parts = link.Split(';', 2);
                    if (parts.Length != 2 || !parts[1].Contains("rel=\"next\"", StringComparison.Ordinal)) continue;
                    var next = parts[0].Trim().Trim('<', '>');
                    if (Uri.TryCreate(next, UriKind.Absolute, out var url) && url.Scheme == "https" &&
                        url.Host == "api.github.com" && url.AbsolutePath.Equals("/repos/HASHMSI/WavePlayer/releases", StringComparison.OrdinalIgnoreCase))
                        address = next;
                }
        }
        return newest;
    }

    internal static ReleasePackage? FindNewestManifest(JsonElement releases, ReleasePackage? newest = null)
    {
        foreach (var release in releases.EnumerateArray())
        {
            if ((release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) ||
                (release.TryGetProperty("prerelease", out var preview) && preview.GetBoolean())) continue;
            var candidate = ParseRelease(release);
            if (candidate is not null && (newest is null || candidate.Version > newest.Version)) newest = candidate;
        }
        return newest;
    }

    internal static ReleasePackage? ParseRelease(JsonElement release)
    {
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        UpdateAsset? FindAsset(string name)
        {
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(asset.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase) ||
                    asset.GetProperty("state").GetString() != "uploaded") continue;
                var address = asset.GetProperty("browser_download_url").GetString();
                var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
                if (address is null || digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
                if (!Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
                    url.Host != "github.com" || !url.AbsolutePath.StartsWith($"/HASHMSI/WavePlayer/releases/download/{tag}/", StringComparison.OrdinalIgnoreCase)) return null;
                var hash = digest[7..];
                var size = asset.GetProperty("size").GetInt64();
                if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || size <= 0) return null;
                return new UpdateAsset(url, size, hash);
            }
            return null;
        }
        var files = FindAsset("WavePlayerFiles.zip");
        var patch = FindAsset("WavePlayerPatch.zip");
        // Setup EXEs and old portable archives are deliberately not update sources.
        return files is null ? null : new ReleasePackage(version, tag, files, patch);
    }

    internal static async Task CheckOnLaunchAsync(MainWindow window)
    {
        // A VS Code build has no install manifest and must never update itself.
        var installDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (LabData.Isolated || window.SkipAutomaticUpdate || installDirectory is null ||
            !File.Exists(Path.Combine(installDirectory, UpdatePackage.ManifestName))) return;
        try
        {
            var release = await FetchLatestAsync();
            if (release is null || release.Version <= InstalledVersion || !window.IsLoaded) return;
            await StartInstallerAsync(window);
        }
        catch (Exception ex) { Debug.WriteLine($"Update check skipped: {ex}"); }
    }

    internal static async Task StartInstallerAsync(MainWindow window, string? localPackage = null)
    {
        string? helperDirectory = null;
        try
        {
            var installedExe = Environment.ProcessPath;
            if (installedExe is null || !File.Exists(installedExe)) return;
            var installDirectory = Path.GetDirectoryName(installedExe)!;
            var installedManifest = UpdatePackage.ReadInstalled(installDirectory)
                ?? throw new InvalidOperationException("Install the new setup first to enable manifest updates.");
            helperDirectory = Path.Combine(Path.GetTempPath(), "WavePlayerUpdater", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(helperDirectory);
            // A multi-file apphost loads WavePlayer.dll beside WavePlayer.exe.
            var helperExe = Path.Combine(helperDirectory, "WavePlayer.exe");
            await Task.Run(() =>
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
            });
            var start = new ProcessStartInfo(helperExe) { UseShellExecute = false };
            start.ArgumentList.Add(localPackage is null ? "--apply-update" : "--apply-package");
            start.ArgumentList.Add(installedExe);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            if (localPackage is not null) start.ArgumentList.Add(Path.GetFullPath(localPackage));
            if (!window.IsVisible) start.ArgumentList.Add("--resume-hidden");
            if (Process.Start(start) is not null)
            {
                helperDirectory = null;
                window.ExitApplication();
            }
        }
        finally
        {
            if (helperDirectory is not null)
                try { Directory.Delete(helperDirectory, recursive: true); } catch (IOException) { }
        }
    }

    internal static async Task CleanupStaleHelpersAsync()
    {
        await Task.Delay(2000);
        var root = Path.Combine(Path.GetTempPath(), "WavePlayerUpdater");
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
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WavePlayerUpdater"));
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
