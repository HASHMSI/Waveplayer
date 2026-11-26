using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace SynthwaveNowPlaying;

/// <summary>
/// Windows' built-in curl uses the OS network stack and can reach some release
/// CDNs when a managed TLS handshake is reset. No shell or extra download tool
/// is installed, and the same size and SHA-256 checks remain mandatory.
/// </summary>
internal static class WindowsPackageDownload
{
    private static string Tool => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
    internal static bool Available => OperatingSystem.IsWindows() && File.Exists(Tool);

    internal static async Task DownloadAsync(UpdateAsset asset, string destination, Action<long, long> progress)
    {
        if (!Available) throw new IOException("Windows download transport is unavailable.");
        var start = new ProcessStartInfo(Tool)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        // Arguments are passed directly to the trusted System32 binary, never
        // interpreted by PowerShell or cmd. Redirects must remain HTTPS.
        foreach (var arg in new[] { "--location", "--fail", "--silent", "--show-error",
            "--proto", "=https", "--proto-redir", "=https", "--connect-timeout", "20",
            "--max-time", "1800", "--retry", "2", "--retry-delay", "1",
            "--max-filesize", asset.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--user-agent", "WavePlayer-Updater/1.0", "--output", Path.GetFullPath(destination), asset.DownloadUrl.AbsoluteUri })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Windows could not start the download.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        while (!process.HasExited)
        {
            await Task.Delay(180);
            try { if (File.Exists(destination)) progress(new FileInfo(destination).Length, asset.Size); }
            catch (IOException) { /* Windows may briefly hold the file metadata. */ }
        }
        await process.WaitForExitAsync();
        await output;
        var message = await error;
        if (process.ExitCode != 0)
        {
            Debug.WriteLine($"Windows package download: {message}");
            throw new IOException("The download could not finish. Check your connection, then retry.");
        }
        await using var verify = File.OpenRead(destination);
        if (verify.Length != asset.Size || !CryptographicOperations.FixedTimeEquals(
            await SHA256.HashDataAsync(verify), Convert.FromHexString(asset.Sha256)))
            throw new InvalidDataException("The Windows download did not pass SHA-256 verification.");
        progress(asset.Size, asset.Size);
    }
}
