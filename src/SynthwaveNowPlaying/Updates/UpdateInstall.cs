using System.IO;

namespace SynthwaveNowPlaying;

/// <summary>
/// A file transaction shared by setup and the in-app updater. Staging and hash
/// verification happen first; failed copies or failed startup restore the old files.
/// </summary>
internal static class UpdateInstall
{
    internal static void Apply(string stage, string directory, UpdateManifest manifest,
        Action? verifyStartup = null, bool allowLegacyInstall = false, IProgress<int>? progress = null)
    {
        UpdatePackage.Validate(manifest);
        directory = Path.GetFullPath(directory);
        stage = Path.GetFullPath(stage);
        Directory.CreateDirectory(directory);
        var previous = UpdatePackage.ReadInstalled(directory);
        if (previous is null && !allowLegacyInstall)
            throw new InvalidDataException("The installed update manifest is missing.");
        var oldPaths = (previous?.Files ?? manifest.Files).Select(file => file.Path).ToList();
        oldPaths.Add(UpdatePackage.ManifestName);
        var managed = oldPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            var target = Path.Combine(directory, Local(file.Path));
            if (!managed.Contains(file.Path) && File.Exists(target))
                throw new IOException($"An unmanaged file occupies the install path: {file.Path}");
            if (!File.Exists(Path.Combine(stage, Local(file.Path))))
                throw new InvalidDataException($"A staged file is missing: {file.Path}");
        }
        // Check file locks before the first mutation, including a running tray copy.
        foreach (var relative in oldPaths)
        {
            var path = Path.Combine(directory, Local(relative));
            if (!File.Exists(path)) continue;
            using var check = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        var backup = Path.Combine(directory, ".WavePlayer-backup-" + Guid.NewGuid().ToString("N"));
        var moved = new List<string>();
        var copied = new List<string>();
        Directory.CreateDirectory(backup);
        try
        {
            foreach (var relative in oldPaths)
            {
                var source = Path.Combine(directory, Local(relative));
                if (!File.Exists(source)) continue;
                var saved = Path.Combine(backup, Local(relative));
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Move(source, saved);
                moved.Add(relative);
            }
            var index = 0;
            foreach (var relative in manifest.Files.Select(file => file.Path).Append(UpdatePackage.ManifestName))
            {
                var target = Path.Combine(directory, Local(relative));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                copied.Add(relative); // Also remove partial copies during rollback.
                File.Copy(Path.Combine(stage, Local(relative)), target);
                progress?.Report(++index * 100 / (manifest.Files.Count + 1));
            }
            verifyStartup?.Invoke();
        }
        catch (Exception failure)
        {
            var restoreErrors = new List<Exception>();
            foreach (var relative in copied)
                try
                {
                    var target = Path.Combine(directory, Local(relative));
                    if (File.Exists(target)) File.Delete(target);
                }
                catch (Exception ex) { restoreErrors.Add(ex); }
            foreach (var relative in moved)
                try { File.Move(Path.Combine(backup, Local(relative)), Path.Combine(directory, Local(relative))); }
                catch (Exception ex) { restoreErrors.Add(ex); }
            if (restoreErrors.Count > 0)
                throw new AggregateException($"Could not fully restore the previous player. Recovery files: {backup}",
                    new[] { failure }.Concat(restoreErrors));
            DeleteOwnedBackup(backup, directory);
            throw;
        }
        DeleteOwnedBackup(backup, directory);
    }

    private static string Local(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private static void DeleteOwnedBackup(string backup, string directory)
    {
        // Never recursively remove a caller-controlled path outside this install.
        var name = Path.GetFileName(backup);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(backup)), directory, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(".WavePlayer-backup-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[19..], "N", out _)) return;
        try { Directory.Delete(backup, recursive: true); }
        catch (IOException) { /* A locked backup can be recovered manually. */ }
        catch (UnauthorizedAccessException) { }
    }
}
