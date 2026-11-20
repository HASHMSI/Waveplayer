# Theme choice preview

The shared WPF theme chooser used by WavePlayer on first launch, by setup,
and from Settings. The two cards use captured images of the original player
and the Modern 80s layout. The installed player uses the same Modern layout.

Build and run with .NET 10 on Windows:

```powershell
dotnet run --project installer/theme-preview/WavePlayer.ThemePreview.csproj
```

The cards contain real window captures: `Classic80s.jpg` from the installed
player, and `Modern80s.jpg` from the isolated Radio Concept UI prototype.
They are still images; hovering a card opens a roughly 1.3× screenshot with
depth, pointer-responsive tilt and a short enter/exit animation. The enlarged
preview stays below the screenshots' native size to avoid upscaling. The host
application saves `UiStyle` and `ThemeChoiceMade` after Continue.
