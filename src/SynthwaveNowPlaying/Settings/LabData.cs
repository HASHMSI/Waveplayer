using System.IO;

namespace SynthwaveNowPlaying;

internal static class LabData
{
    public static string? DirectoryPath => Environment.GetEnvironmentVariable("WAVEPLAYER_LAB_DATA_DIR");
    public static string DataDirectory => string.IsNullOrWhiteSpace(DirectoryPath)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeonWave")
        : DirectoryPath;
    public static bool Isolated => !string.IsNullOrWhiteSpace(DirectoryPath);
}
