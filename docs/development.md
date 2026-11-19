# Development

Use Windows x64 and the .NET 10 SDK, not just a runtime. Check `dotnet --info`. Restore needs NuGet access. Use PowerShell 7 for packaging, including the legacy script's modern .NET APIs. Player targets Windows 10.0.19041.0; setup targets net10.0-windows. Supported OS claims still require runtime testing.

From the repository root:

```powershell
dotnet build ./src/SynthwaveNowPlaying/SynthwaveNowPlaying.csproj
dotnet run --project ./src/SynthwaveNowPlaying/SynthwaveNowPlaying.csproj
```

For VS Code, open `src/SynthwaveNowPlaying`, install the C# debugging extension, and press F5. The task resolves dotnet from PATH. Keep personal SDK overrides in user settings. Restart the editor after changing PATH.

## Source map

- Audio: processing, EQ, BPM/key analysis, cache and transition planners.
- Settings: preferences and configuration UI; SpectrumLab loads embedded XAML.
- UI: MainWindow partials, playlists, watching, tray and overlay. MainWindow.RemixDemo.cs contains production Automix despite its historical name.
- Visuals: current spectrum, background and cover-volume rendering.
- Classic: required archived DLL and reflection adapter; read its README before changing.
- Updates: retained compatibility implementation; automatic launch checks are disabled.
- Installer: setup UI, extraction and media previews. Its project links SpectrumView, ArchivedVisualizer and SpectrumLab sources from the player.
- Tests: executable harnesses, not test SDK projects.

Root AssemblyInfo.cs contains WPF ThemeInfo; Properties/AssemblyInfo.cs contains the AutomixV2Checks friend-assembly declaration. They are not duplicates. Keep project/namespace names distinct from public branding.

## Outputs and data

Player-local Directory.Build.props directs outputs to `.build/SynthwaveNowPlaying/`. Tests and setup use their own bin/obj. Moving that props file to root would change its scope.

Build setup with `./installer/build-setup.ps1`. It publishes the player and generates `installer/intermediate/payload.zip` before compiling setup. Direct setup-project builds require this input. Generated outputs are ignored.

Normal app launches use real `%LOCALAPPDATA%/NeonWave` data. Use a disposable Windows account for install and persistence testing. See [releasing](releasing.md).
