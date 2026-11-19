# Releasing

## Current policy: offline setup only

Commit 3e2b196 intentionally disabled automatic launch checks. Publish only `installer/release/WavePlayerSetup.exe`. Never upload the entire output folder or a wildcard: old compatibility ZIPs may coexist there.

1. Review changes, media permissions and notices before distribution.
2. Keep player/setup project versions equal (currently 1.1.1); use a matching tag and reviewed GitHub release notes covering changes, compatibility and limitations. Release notes are the release-history record.
3. Run both [check programs](testing.md), then from root using PowerShell 7:

   ```powershell
   ./installer/build-setup.ps1
   ```

   `-Dotnet <dotnet.exe-path>` selects a local SDK without tracked machine settings.
4. Inspect installer/payload and installer/intermediate/payload.zip: WavePlayer.exe, WavePlayer.dll, dependencies and Classic/SynthwaveNowPlaying.dll must exist. Publish is self-contained win-x64 without trimming. Setup includes publish output including PDBs; legacy update ZIPs exclude PDBs. Changing symbol policy is a separate decision.
5. Compare the Classic hash with its README. Check setup resource identities: WavePlayerSetup.payload.zip, Setup.Classic.dll, Settings.SpectrumLab.xaml and WavePlayerSetup.Assets demo files.
6. In a disposable Windows account, install offline to a path with spaces. Verify shortcuts, previews, both visualizers, playback and tray controls. Reinstall with existing settings/playlist edits and verify preservation. Check failure/retry behavior without risking a personal installation.
7. Record EXE SHA-256, versions and verification results. Upload only the exact approved EXE. CI does not publish or prove GUI acceptance.

Installer extraction replaces files but does not reconcile obsolete files through a manifest. Shipped-file moves require a separate migration. Preserve `%LOCALAPPDATA%/NeonWave/settings.json` and track-analysis.json.

## Retained updater compatibility

Keep build-update.ps1, Updates sources and --apply-update support. They are not the normal release path; do not re-enable launch checks during organization work.

The script produces exact names WavePlayerFiles.zip, WavePlayerPortable.zip and, with -BasePayload, WavePlayerPatch.zip. WavePlayer-update.json uses format 2. Keep WavePlayer.exe/WavePlayer.dll and Classic/SynthwaveNowPlaying.dll entry names, hashes and versions. The client requires exact GitHub asset names and release digests.

For patches, retain an immutable base payload and manifest outside directories the script clears. Test full/patch/wrong-base fallback/legacy bridge plus startup and rollback before publishing compatibility packages. Renaming or recasing public assets can affect older clients.

Both scripts mutate installer/payload and installer/release. Run sequentially or in isolated checkouts, never concurrently in one checkout.

## Attribution in releases

Preserve Copyright © 2026 MSIHASH x SENIOR B and the Vibe coded via Codex credit. Include the applicable third-party license texts and notices for the actual published dependency set. Use THIRD_PARTY_NOTICES.md and docs/assets.md to identify dependencies and retained media. Keep copyright attribution separate from upstream component terms.

