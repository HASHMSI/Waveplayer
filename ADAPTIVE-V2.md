# WavePlayer Adaptive v2 timing

Adaptive v2 combines bar-aware timing with WavePlayer's existing NAudio effects.
For a separate local preview, run `Launch Adaptive v2 Preview.cmd`. The preview
uses its own settings and analysis cache in `F:\WavePlayerAutoDjLabData`.
This source change is local; it has not been pushed to GitHub.

The experiment was informed by the transition design described in
[heydavidf/automix](https://github.com/heydavidf/automix). That project is
AGPL-3.0-only. Its TypeScript source, Web Audio engine, and dependencies are
not copied into this C# build. `TransitionPlannerLab` is an independent
implementation using WavePlayer's existing NAudio pipeline.

## What it does

- Merges the bar-aware timing experiment into **Adaptive (v2)**. The test profile
  starts with Automix enabled; older AutoDJ Lab settings load as Adaptive v2.
- Analyzes the current and next local tracks ahead of the transition.
- Uses short bar-aligned blends when both beat grids are reliable and close in
  tempo. The existing audio deck performs the bass handoff.
- Uses a short fallback when the grids are uncertain or incompatible.
- Selects the V2 effects after timing is chosen and keeps dense vocal or
  percussive overlaps clean.
- Chooses an overlap before the outgoing track's silent tail when possible.

This mode does not stretch tempo, separate stems, reorder the playlist,
or claim Apple Music parity. The reference project itself says its transitions
have not been quality-validated on long sessions of real music. Before a
public release, compare output on a varied listening set and review the
upstream AGPL license separately.

## Checks

`tests/AutomixV2Checks` exercises aligned, incompatible and uncertain grids,
then validates real-track plan bounds and renders a real transition through
the same `AdaptiveDeck` audio path used by the player.
