# Troubleshooting

- Closing may hide the player while music continues. Restore or exit through the notification-area icon; exit before reinstalling.
- For unreadable tracks, confirm file access and compare a known-good WAV/MP3. Playback depends on Windows codecs; skipped files are not deleted.
- Check configured folders and supported extensions if music is missing. Scanning does not start playback; select a track explicitly.
- For glitches, try Stable buffering and start the next track for it to apply. Compare with Automix/EQ disabled and reduce competing load. Driver glitches are not guaranteed to disappear.
- BPM/key are estimates; UNSURE is intentional. Synthetic accuracy is not real-library accuracy.
- Before investigating settings, exit and back up `%LOCALAPPDATA%/NeonWave/settings.json` and track-analysis.json. Do not delete them as a first step. Saved playlist edits do not modify source songs or playlist files.
- Updates are manual: download current setup through the root README, exit via tray, run setup and verify preferences.

Reports should include app/Windows versions, format/device, relevant settings, steps and expected/actual behavior. Redact private paths and metadata. Do not attach personal recordings by default. See SECURITY.md for sensitive-reporting status.
