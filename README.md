# Waveplayer

A compact Windows music player for your local music library, with a synthwave interface, live visualizers, an equalizer, and automatic track mixing.

![Waveplayer feature illustration](docs/waveplayer-3d-features.png)

## Download and install

[**Download Waveplayer for Windows**](https://github.com/HASHMSI/Waveplayer/releases/latest/download/WaveplayerSetup.exe)

Run `WaveplayerSetup.exe` and follow the setup walkthrough to choose your music folders and player options. The installer works offline.

To update Waveplayer, exit it from the notification-area icon, then run the new installer. Your settings and playlist edits are kept. Updates are installed manually.

## What you can do

- Play music from multiple folders and browse M3U, M3U8, and PLS playlists.
- Search playlists, group tracks by artist or genre, and reorder or remove tracks from Waveplayer's list.
- Keep music playing when you hide the player. Use the notification-area icon to restore the player, control playback, open settings, or exit.
- Choose the Pulse or Glide visualizer and customize themes, colors, background, layout, and widget size.
- Adjust playback with the six-band equalizer. It affects Waveplayer only, not other apps or Windows audio.
- Use Classic, Adaptive, or **Adaptive (v2)** Automix to blend tracks. V2 checks several overlap lengths, protects against clashing voices and beats, and chooses a gentle space echo or a beat-locked drop only when the audio supports it.
- Scan for estimated BPM, musical key, and Camelot notation. Uncertain results are marked as such.
- Preview the album-art Automix popup from Settings or the notification-area menu.

## Music and playback

Waveplayer recognizes **MP3, WAV, AIFF, WMA, M4A, AAC, and FLAC** files. Playback depends on the audio codecs available on your Windows installation.

In Settings, add one or more music folders. Waveplayer scans their subfolders and picks up new files while it is running. Scanning does not start playback. Select a playlist or folder to browse; click a track to start playback. When a track ends, playback continues through the queue it was started from. Unreadable tracks are skipped.

Reordering or removing a track changes Waveplayer's saved list only. It does not move or delete music files or edit playlist files.

### Automix and audio options

Automix can skip detected opening silence and crossfade before a track ends. Adaptive mode can choose shorter or longer blends based on the audio, up to 12 seconds. Adaptive (v2) adds more selective timing and ending effects. Beat detection is an estimate; transitions remain usable when no reliable beat is found. [Read about v2 and its tests](docs/adaptive-v2.md).

Player settings offer **Original** and **Stable** audio buffer modes. Stable mode uses more buffering and may increase output latency. It can help on some systems but cannot prevent every audio-driver glitch. The selected mode applies when the next track starts.

Hold and drag up or down on the album cover to change Waveplayer's volume.

## Screenshots

These previews show an earlier version of the interface under the name Neon Wave.

| Player | Settings |
| --- | --- |
| ![Player preview](docs/preview-widget.png) | ![Settings preview](docs/preview-settings.png) |

More: [custom colors](docs/preview-settings-custom.png) · [theme preview](docs/preview-theme-hover.png) · [cover volume](docs/preview-cover-volume.png)

[▶ Introduction video](promo/Neon-Wave.mp4)

## Your settings and files

Waveplayer plays music from folders on your computer. It does not upload your music for playback or analysis.

- Settings and playlist edits: `%LOCALAPPDATA%\NeonWave\settings.json`
- BPM and key analysis cache: `%LOCALAPPDATA%\NeonWave\track-analysis.json`

Removing a track from a Waveplayer list does not delete its audio file or change its M3U, M3U8, or PLS file.

## Build from source

For developers: Waveplayer is a **.NET 10 WPF** app for Windows. Install the .NET 10 SDK, then run PowerShell from the repository folder:

```powershell
dotnet build .\src\SynthwaveNowPlaying\SynthwaveNowPlaying.csproj
```

To build the offline installer:

```powershell
.\installer\build-setup.ps1
```

The installer is created at `installer\release\WaveplayerSetup.exe`. The player uses [NAudio](https://github.com/naudio/NAudio) for audio and [TagLibSharp](https://github.com/mono/taglib-sharp) for track tags and artwork.

## Project contents

```text
src/SynthwaveNowPlaying/   Player app
installer/                Windows setup and build script
docs/                     Screenshots and technical notes
tests/KeyDetectorChecks/  Musical-key detector checks
tests/AutomixV2Checks/    Automix timing and offline audio checks
promo/                    Introduction video
```

Made by **MSIHASH × SENIOR B** 💜
