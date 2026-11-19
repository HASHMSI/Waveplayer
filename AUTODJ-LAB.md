# WavePlayer AutoDJ Lab

This worktree is an isolated experiment. The installed WavePlayer and the
`main` branch are unchanged. Start the demo with `Launch AutoDJ Lab.cmd`.
Its settings and analysis cache are stored in `F:\WavePlayerAutoDjLabData`.

The experiment was informed by the transition design described in
[heydavidf/automix](https://github.com/heydavidf/automix). That project is
AGPL-3.0-only. Its TypeScript source, Web Audio engine, and dependencies are
not copied into this C# build. `TransitionPlannerLab` is an independent
implementation using WavePlayer's existing NAudio pipeline.

## What it does

- Adds an **AutoDJ Lab** choice in Settings > Automix timing. The test profile
  starts with Automix enabled.
- Analyzes the current and next local tracks ahead of the transition.
- Uses short bar-aligned blends when both beat grids are reliable and close in
  tempo. The existing audio deck performs the bass handoff.
- Uses a short clean crossfade when the grids are uncertain or incompatible.
- Chooses an overlap before the outgoing track's silent tail when possible.

This prototype does not stretch tempo, separate stems, reorder the playlist,
or claim Apple Music parity. The source project itself says its transitions
have not been quality-validated on long sessions of real music. Before any
public integration, compare its output on a varied listening set and review
the upstream AGPL license separately.

## Checks

`tests/AutomixV2Checks` exercises aligned, incompatible and uncertain grids,
then validates real-track plan bounds and renders a real transition through
the same `AdaptiveDeck` audio path used by the player.
