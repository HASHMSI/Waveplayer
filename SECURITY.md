# Security information

## Reporting

Use the repository's Bug report issue form for reproducible, non-sensitive defects. Include the application and Windows versions, reproduction steps, expected and actual results, and relevant settings. Redact personal folder names, recording metadata and credentials before attaching diagnostics. Keep sensitive vulnerability details and exploit material out of public issues.

## Current implementation

WavePlayer plays and analyzes local music. Settings are stored in `%LOCALAPPDATA%/NeonWave/settings.json`; analysis results are in `track-analysis.json`. These files can contain personal music paths. The installed program defaults to `%LOCALAPPDATA%/Programs/WavePlayer` and setup runs for the current Windows account.

Current updates are manual offline setup installs. Automatic launch checks were removed in commit 3e2b196. Retained updater code accepts specific GitHub release assets, verifies package SHA-256, validates manifest paths and file hashes, and stages updates before replacement. That code remains accessible through --apply-update.

Installer extraction checks destination containment and writes temporary .partial files before replacement. It does not provide a manifest-driven removal of obsolete installed files. Use a disposable account for installation or malformed-package testing.

## Handling suspected problems

Stop using an affected build if you suspect it is unsafe. Preserve only the minimal reproduction material needed for investigation. Do not attach your entire settings/cache files or music library publicly. Back up personal settings before reinstalling. Get releases through the download link in README.md and avoid executing packages from unrelated sources.

Use the current release when reproducing an issue and include its version in the report.
