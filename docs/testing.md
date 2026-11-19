# Testing

Run on Windows with the .NET 10 SDK from repository root:

```powershell
dotnet run --project tests/KeyDetectorChecks -c Release
dotnet run --project tests/AutomixV2Checks -c Release
```

These console programs are not run by dotnet test. Key checks set a failing exit code; Automix assertions throw. CI invokes each explicitly.

## Ownership and fixtures

MusicalKeyAnalyzer changes require KeyDetectorChecks, which compile-links that source. It synthesizes tonal/tuning/rejection/cancellation WAV fixtures under a temporary directory and removes them on normal completion. Transition/audio changes require AutomixV2Checks, which references the player through its named friend assembly. It checks planner constraints, effects, pre-cue scheduling and legacy isolation. Neither program replaces GUI/installer tests.

## Optional real tracks

Append `-- <absolute-track-paths...>` to either command. Automix needs at least two tracks for rendering. It currently silently filters nonexistent paths: verify Analyzed and Rendered real pair output before reporting real-track coverage.

Automix options:
- `--cached <tracks...>` after the dotnet separator reads the usual local analysis cache; this harness never calls Save.
- `WavePlayer_FIND_DROP=1` searches supplied profiles for drop candidates.
- `WavePlayer_AUTOMIX_PREVIEW=<absolute-output.wav>` writes a rendered pair. Use a disposable path; it may overwrite an existing file.

Clear these environment variables after use. Keep private recordings and output outside tracked source. No real-song fixture collection is required.

## Manual acceptance

Check playback/seek/queue advancement, both visualizers, all Automix modes, pause/resume, tray hide/restore/exit, settings persistence, library watching and the popup. Listen for glitches. Synthetic bounded output does not establish perceived quality. Follow [release checks](releasing.md) for packaging changes. Record fresh outcomes rather than reusing historical results.
