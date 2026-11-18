# Adaptive (v2)

Player settings now offer three Automix timing choices: Classic, the original
Adaptive, and Adaptive (v2). The older modes retain their planners and effects.

V2 analyzes the cached intro and outro features, considers several fade lengths,
and ranks their overlap by simultaneous voice, attacks, level and skipped intro.
It aligns to local beats only when both beat grids are reliable and the tempos
are close (including half/double-time equivalence). A sustained, non-vocal outro
may receive a quiet, decaying 180 ms space echo. Natural tails are preserved;
strong percussion or voice calls for a clean handoff. V2 trims the summed center
of a two-song fade and does not boost the incoming level above unity. A late
render tick inside the silent tail skips the blend rather than forcing a short
transition after the music has stopped.

The feature analysis is heuristic. Stems, key matching, tempo stretching and
perfect phrase detection are not implemented. An uncertain beat grid uses an
unquantized transition. Audio stays in the local player output chain.

Validation: `dotnet run --project tests/AutomixV2Checks -c Release -- <tracks...>`
checks a gentle and percussive synthetic outro, compatible and incompatible
beat grids, the new effect's finite output, and legacy-mode isolation. It was
run on 16 local FLAC songs (15 consecutive pairs), including an offline rendered
real SpaceEcho mix; all samples were finite and the output peaks stayed below
full scale. Manual listening remains useful for judging taste and perceived
loudness. No transition planner can guarantee a perfect mix for every song.
