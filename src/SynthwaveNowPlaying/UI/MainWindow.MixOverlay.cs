using System.IO;
using NAudio.Wave;

namespace SynthwaveNowPlaying;

public partial class MainWindow
{
    private AutomixOverlay? _mixOverlay;
    private AudioFileReader? _overlayReader;

    private AutomixOverlay CreateMixOverlay(string? current, string? next)
    {
        _mixOverlay?.Dismiss();
        var view = new AutomixOverlay();
        _mixOverlay = view;
        view.Closed += (_, _) => { if (ReferenceEquals(_mixOverlay, view)) _mixOverlay = null; };
        TrackMetadata Fallback(string? file, string title) => file is not null && _trackMetadataCache.TryGetValue(file, out var cached)
            ? cached : new TrackMetadata(file is null ? title : Path.GetFileNameWithoutExtension(file), "LOCAL LIBRARY", "", TimeSpan.Zero, null);
        var a = Fallback(current, "Acedia"); var b = Fallback(next, "Heartline");
        view.SetTracks(a.Title, a.Artist, a.Artwork, b.Title, b.Artist, b.Artwork);
        view.Show();
        // Artwork decoding is independent of the playback/mixer lock. A late
        // completion may update only the same still-active overlay, never a later mix.
        _ = LoadOverlayArtworkAsync(view, current, next, a, b);
        return view;
    }

    private async Task LoadOverlayArtworkAsync(AutomixOverlay view, string? current, string? next, TrackMetadata a, TrackMetadata b)
    {
        try
        {
            (a, b) = await Task.Run(() => (current is null ? a : ReadOverlayMetadata(current), next is null ? b : ReadOverlayMetadata(next)));
            if (!ReferenceEquals(_mixOverlay, view) || !view.IsLoaded) return;
            view.SetTracks(a.Title, a.Artist, a.Artwork, b.Title, b.Artist, b.Artwork);
        }
        catch { /* A missing cover keeps the two fallback gradients. */ }
    }

    private static TrackMetadata ReadOverlayMetadata(string file)
    {
        var metadata = ReadTrackMetadata(file);
        if (metadata.Artwork is not null) return metadata;
        // Some embedded album images exceed the small playlist metadata limit.
        // Decode a thumbnail off-thread, never a full-resolution background.
        try
        {
            using var tagged = TagLib.File.Create(file);
            var bytes = tagged.Tag.Pictures.FirstOrDefault()?.Data.Data;
            if (bytes is { Length: > 0 and <= 16_000_000 })
                return metadata with { Artwork = ImageFromBytes(bytes, 192) };
        }
        catch { }
        return metadata;
    }

    private void UpdateMixOverlay(double progress)
    {
        if (IsVisible)
        {
            if (_overlayReader is not null) DismissMixOverlay();
            return;
        }
        if (!_settings.AutomixOverlayEnabled || _incomingReader is null || _localTrack is null || _incomingTrack is null) return;
        if (!ReferenceEquals(_overlayReader, _incomingReader))
        {
            _overlayReader = _incomingReader;
            CreateMixOverlay(_localTrack, _incomingTrack);
        }
        _mixOverlay?.SetProgress(progress);
    }

    private void FinishMixOverlay() { _mixOverlay?.Complete(); _overlayReader = null; }

    private void DismissMixOverlay(bool immediately = false)
    {
        if (immediately) _mixOverlay?.Close(); else _mixOverlay?.Dismiss();
        _mixOverlay = null;
    }

    internal void PreviewMixOverlay()
    {
        if (_incomingReader is not null) return;
        var current = _library.FirstOrDefault(file => Path.GetFileNameWithoutExtension(file).Contains("Acedia", StringComparison.OrdinalIgnoreCase))
            ?? _localTrack ?? _library.FirstOrDefault();
        var next = _library.FirstOrDefault(file => Path.GetFileNameWithoutExtension(file).Contains("Heartline", StringComparison.OrdinalIgnoreCase))
            ?? _library.FirstOrDefault(file => !string.Equals(file, current, StringComparison.OrdinalIgnoreCase));
        CreateMixOverlay(current, next).RunPreview();
    }
}
