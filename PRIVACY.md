# Privacy and local data

WavePlayer is a Windows desktop music player for files on your computer. Playback, BPM analysis, musical-key analysis and Automix processing take place locally.

## Information stored on your computer

| Location | Purpose |
| --- | --- |
| `%LOCALAPPDATA%/NeonWave/settings.json` | Player preferences, music folders and saved playlist edits |
| `%LOCALAPPDATA%/NeonWave/track-analysis.json` | Cached analysis associated with local music files |
| `%LOCALAPPDATA%/Programs/WavePlayer` | Default application installation directory; setup allows another location |

Folder names, track paths and preferences can identify your personal music library. Keep backups and shared diagnostics private. Removing a track from WavePlayer's list updates the saved list; original songs and M3U/M3U8/PLS files stay in their existing locations.

## Network activity and updates

Current releases use manual updates through the GitHub download linked in README.md. Playback and analysis use local music files. Retained legacy updater code can contact the GitHub releases API and download update assets when invoked through its updater entry point. Downloading releases through GitHub uses that service's own privacy practices.

## Managing your data

Exit WavePlayer using its notification-area menu before backing up or restoring settings. Copy the two JSON files above to a safe location to retain preferences and analysis. Reinstalling the application preserves these local data locations. Share only the minimum information necessary for a bug report, with personal paths and metadata removed.

Copyright © 2026 MSIHASH x SENIOR B. Vibe coded via Codex.
