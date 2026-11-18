using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SynthwaveNowPlaying;

internal sealed record UpdateFile(string Path, long Size, string Sha256);
internal sealed record UpdateManifest(int Format, string Version, string? BaseVersion,
    List<UpdateFile> Files, List<string>? Changed);

/// <summary>Stages and verifies a full or file-level update before touching the installation.</summary>
internal static class UpdatePackage
{
    internal const string ManifestName = "WavePlayer-update.json";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    internal static UpdateManifest? ReadInstalled(string directory)
    {
        var path = Path.Combine(directory, ManifestName);
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(stream, Json)
            ?? throw new InvalidDataException("The installed update manifest is empty.");
        Validate(manifest);
        return manifest;
    }

    internal static UpdateManifest Stage(string package, string destination, string installedDirectory,
        Version releaseVersion, bool patch)
    {
        using var archive = ZipFile.OpenRead(package);
        var manifestEntry = archive.GetEntry(ManifestName)
            ?? throw new InvalidDataException("The update manifest is missing.");
        UpdateManifest manifest;
        using (var stream = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<UpdateManifest>(stream, Json)
                ?? throw new InvalidDataException("The update manifest is empty.");
        Validate(manifest);
        if (!Version.TryParse(manifest.Version, out var packagedVersion) || packagedVersion != releaseVersion)
            throw new InvalidDataException("The package version does not match the release.");

        var changed = new HashSet<string>(manifest.Changed ?? [], StringComparer.OrdinalIgnoreCase);
        UpdateManifest? installed = null;
        if (patch)
        {
            installed = ReadInstalled(installedDirectory);
            if (installed is null || manifest.BaseVersion != installed.Version ||
                changed.Count == 0 || changed.Count != manifest.Changed!.Count)
                throw new InvalidDataException("This patch does not match the installed version.");
        }
        else if (manifest.BaseVersion is not null || changed.Count != 0)
            throw new InvalidDataException("The full package has patch-only fields.");
        if (manifest.Files.Sum(file => file.Size) > 2_000_000_000)
            throw new InvalidDataException("The update is larger than the supported player package.");

        Directory.CreateDirectory(destination);
        var oldFiles = new HashSet<string>(installed?.Files.Select(file => file.Path) ?? [],
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            var target = Path.Combine(destination, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (patch && !changed.Contains(file.Path))
            {
                if (!oldFiles.Contains(file.Path))
                    throw new InvalidDataException($"The patch lacks a new file: {file.Path}");
                File.Copy(Path.Combine(installedDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar)), target);
            }
            else
            {
                var entry = archive.GetEntry(file.Path)
                    ?? throw new InvalidDataException($"The update lacks a file: {file.Path}");
                if (entry.Length != file.Size)
                    throw new InvalidDataException($"The package size is wrong for: {file.Path}");
                entry.ExtractToFile(target);
            }
            var info = new FileInfo(target);
            using var verify = File.OpenRead(target);
            if (info.Length != file.Size ||
                !Convert.ToHexString(SHA256.HashData(verify))
                    .Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"A file did not pass SHA-256 verification: {file.Path}");
        }
        if (patch && changed.Except(manifest.Files.Select(file => file.Path), StringComparer.OrdinalIgnoreCase).Any())
            throw new InvalidDataException("The patch lists an unexpected file.");
        var ready = manifest with { BaseVersion = null, Changed = null };
        File.WriteAllText(Path.Combine(destination, ManifestName), JsonSerializer.Serialize(ready));
        return ready;
    }

    internal static void Validate(UpdateManifest manifest)
    {
        if (manifest.Format != 2 || manifest.Files is not { Count: > 0 and < 1000 } ||
            !Version.TryParse(manifest.Version, out _))
            throw new InvalidDataException("Unsupported update manifest.");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            ValidatePath(file.Path);
            if (!unique.Add(file.Path) || file.Size < 0 || file.Sha256.Length != 64 ||
                !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid update file entry.");
        }
        foreach (var path in manifest.Changed ?? []) ValidatePath(path);
        if (!unique.Contains("WavePlayer.exe") || !unique.Contains("WavePlayer.dll"))
            throw new InvalidDataException("The player files are incomplete.");
    }

    internal static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == ManifestName || path.Contains('\\') ||
            path.Contains(':') || path.StartsWith('/') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("An update file path is invalid.");
    }
}
