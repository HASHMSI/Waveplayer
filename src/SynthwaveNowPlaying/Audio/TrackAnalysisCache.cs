using System.IO;
using System.Text.Json;

namespace SynthwaveNowPlaying;

internal sealed record TrackAnalysis(
    long FileLength, long LastWriteUtcTicks, TransitionProfile Profile,
    double Bpm, double BpmConfidence, bool DeepScanned, bool FromTag,
    MusicalKey Key = default, bool KeyScanned = false);

/// <summary>Persists song analysis locally and invalidates it when the audio file changes.</summary>
internal sealed class TrackAnalysisCache
{
    private sealed record CacheDocument(int Version, Dictionary<string, TrackAnalysis> Tracks);
    // Invalidate older tempo and outro estimates when either analyzer changes.
    private const int CacheVersion = 9;
    private readonly string _filePath;
    private readonly object _sync = new();
    private readonly Dictionary<string, TrackAnalysis> _tracks;

    public TrackAnalysisCache(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(LabData.DataDirectory, "track-analysis.json");
        try
        {
            var document = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(_filePath));
            _tracks = document?.Version is >= 2 and <= CacheVersion
                ? new Dictionary<string, TrackAnalysis>(document.Tracks.Select(pair =>
                    new KeyValuePair<string, TrackAnalysis>(pair.Key, document.Version >= 4
                        ? (document.Version == 8 ? pair.Value with { Profile = default }
                        : document.Version < CacheVersion ? pair.Value with
                        {
                            Profile = default, Bpm = 0, BpmConfidence = 0,
                            DeepScanned = false, FromTag = false
                        } : pair.Value)
                        : pair.Value with { Profile = default, DeepScanned = false })), StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch { _tracks = new(StringComparer.OrdinalIgnoreCase); }
    }

    public bool TryGet(string file, out TrackAnalysis result)
    {
        result = null!;
        try
        {
            var info = new FileInfo(file);
            lock (_sync)
                if (_tracks.TryGetValue(file, out var cached) && cached.FileLength == info.Length &&
                    cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
                { result = cached; return true; }
        }
        catch { }
        return false;
    }

    public TrackAnalysis AnalyzeQuick(string file)
    {
        if (TryGet(file, out var cached) && cached.Profile.DurationSeconds > 0) return cached;
        var profile = TransitionAnalyzer.Analyze(file, knownTempo: cached is { Bpm: > 0 }
            ? new BpmEstimate(cached.Bpm, cached.BpmConfidence) : default);
        if (cached is not null && (cached.DeepScanned || cached.Bpm > 0))
            return Store(file, profile, cached.Bpm, cached.BpmConfidence, cached.DeepScanned, cached.FromTag);
        var tagBpm = ReadTagBpm(file);
        var bestBeat = profile.IntroBeat.Confidence >= profile.OutroBeat.Confidence
            ? profile.IntroBeat : profile.OutroBeat;
        var bpm = tagBpm > 0 ? tagBpm : bestBeat.IsReliable ? bestBeat.Bpm : 0;
        return Store(file, profile, bpm, tagBpm > 0 ? 1 : bestBeat.Confidence, false, tagBpm > 0);
    }

    public TrackAnalysis AnalyzeDeep(string file, CancellationToken cancellationToken)
    {
        if (TryGet(file, out var cached) && cached.DeepScanned) return AnalyzeKey(file, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var tagBpm = ReadTagBpm(file);
        // A metadata BPM can be stale or wrong, so the explicit scan always
        // checks the audio. The tag helps when the audio is rhythmically ambiguous.
        var audioEstimate = FullTrackBpmAnalyzer.Analyze(file, cancellationToken);
        var tagRatio = tagBpm > 0 ? audioEstimate.Bpm / tagBpm : 0;
        var useTag = tagBpm is >= 40 and <= 240 &&
            (audioEstimate.Bpm <= 0 || audioEstimate.Confidence < .18 ||
             Math.Abs(tagRatio - 1) < .035 || Math.Abs(tagRatio - .5) < .015 || Math.Abs(tagRatio - 2) < .06);
        var estimate = useTag ? new BpmEstimate(tagBpm, 1) : audioEstimate;
        if (!useTag && estimate.Confidence < .19) estimate = default;
        cancellationToken.ThrowIfCancellationRequested();
        var profile = TransitionAnalyzer.Analyze(file, cancellationToken, estimate);
        Store(file, profile, estimate.Bpm, estimate.Confidence, true, useTag);
        return AnalyzeKey(file, cancellationToken);
    }

    public TrackAnalysis AnalyzeKey(string file, CancellationToken token)
    {
        var analysis = AnalyzeQuick(file);
        if (analysis.KeyScanned) return analysis;
        var key = MusicalKeyAnalyzer.Analyze(file, token);
        token.ThrowIfCancellationRequested();
        var info = new FileInfo(file);
        if (info.Length != analysis.FileLength || info.LastWriteTimeUtc.Ticks != analysis.LastWriteUtcTicks)
            throw new IOException("The audio file changed during key analysis.");
        lock (_sync)
        {
            var latest = _tracks.GetValueOrDefault(file, analysis);
            return _tracks[file] = latest with { Key = key, KeyScanned = true };
        }
    }

    private TrackAnalysis Store(string file, TransitionProfile profile, double bpm, double confidence, bool deepScanned, bool fromTag)
    {
        var info = new FileInfo(file);
        var entry = new TrackAnalysis(info.Length, info.LastWriteTimeUtc.Ticks, profile,
            double.IsFinite(bpm) && bpm is >= 40 and <= 240 ? bpm : 0,
            double.IsFinite(confidence) ? Math.Clamp(confidence, 0, 1) : 0, deepScanned, fromTag);
        lock (_sync)
        {
            if (_tracks.TryGetValue(file, out var keyed) && keyed.KeyScanned &&
                keyed.FileLength == entry.FileLength && keyed.LastWriteUtcTicks == entry.LastWriteUtcTicks)
                entry = entry with { Key = keyed.Key, KeyScanned = true };
            // A playback lookup can finish after a simultaneous full BPM scan.
            // Keep the deep profile too: its local beat phase used the verified BPM.
            if (!deepScanned && _tracks.TryGetValue(file, out var newer) && newer.DeepScanned &&
                newer.FileLength == entry.FileLength && newer.LastWriteUtcTicks == entry.LastWriteUtcTicks)
                entry = newer.Profile.DurationSeconds > 0 ? newer : newer with { Profile = profile };
            _tracks[file] = entry;
            return entry;
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temporary = _filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new CacheDocument(CacheVersion, _tracks)));
            File.Move(temporary, _filePath, true);
        }
    }

    private static double ReadTagBpm(string file)
    {
        try
        {
            using var tagged = TagLib.File.Create(file);
            return tagged.Tag.BeatsPerMinute;
        }
        catch { return 0; }
    }
}
