using System.Text.Json;
using System.IO;

namespace SynthwaveNowPlaying;

internal sealed class AppSettings
{
    public string? MusicFolder { get; set; }
    public List<string> MusicFolders { get; set; } = [];

    // Older settings have one MusicFolder. Keep its playlist identity after migration.
    public IReadOnlyList<string> GetMusicFolders()
    {
        var folders = MusicFolders is { Count: > 0 } ? MusicFolders : [MusicFolder ?? ""];
        return folders.Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public string Background { get; set; } = "Neon Grid";
    public string? BackgroundImage { get; set; }
    public string CustomPrimary { get; set; } = "#FF47B8";
    public string CustomSecondary { get; set; } = "#45D8FF";
    public double CustomMix { get; set; } = .5;
    public bool EqualizerEnabled { get; set; }
    public bool RemixDemoEnabled { get; set; }
    public AutomixTiming AutomixTiming { get; set; } = AutomixTiming.Adaptive;
    public bool ClassicVisualizer { get; set; } = true;
    public double Sensitivity { get; set; } = 1;
    public double Volume { get; set; } = 1;
    public double[] Bands { get; set; } = new double[6];
    public Dictionary<string, UiPlacement> WidgetLayout { get; set; } = new();
    // Local playlist edits never modify or delete the original audio or playlist files.
    public Dictionary<string, PlaylistEdits> PlaylistEdits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double? WidgetWidth { get; set; }

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeonWave", "settings.json");
    public static AppSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class PlaylistEdits
{
    public List<string> Removed { get; set; } = [];
    public List<string> Order { get; set; } = [];
}

internal sealed class UiPlacement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Scale { get; set; } = 1;
    public UiPlacement Copy() => new() { X = X, Y = Y, Scale = Scale };
}
