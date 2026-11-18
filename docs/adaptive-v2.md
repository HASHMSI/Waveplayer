# Adaptive (v2)

Pre-cue playback regression: the player arms both decks 1.25 seconds before the
transition. Previously the outgoing deck entered the effect renderer with a
negative age, producing a negative delay-ring index and stopping the audio
device with IndexOutOfRangeException. Negative-age reads now play the outgoing
source at its existing gain and keep the incoming source silent without
consuming it. Effects start only at the cue. The regression test fails with
the previous binary and passes with the fix; it covers all ending effects and
Sunshower → Heartline with the real pre-cue scheduling delay.

**NeonRoll** is now the main V2 effect for overlaps of at least 2 s. It freezes
the most recent short audio slice, repeats it, then halves its duration twice
approaching the handoff. Slice boundaries taper over 4 ms and stage changes
crossfade over 5 ms. The roll feeds the stereo echo and filter sweep. The next
track opens at the handoff. The processing uses preallocated short buffers on
the audio thread. A reliable beat controls the delay period; otherwise the
effect uses a fixed musical gesture without claiming tempo matching.

The current V2 sound uses **SweepEcho** for transitions lasting at least 1.5 s:
the outgoing low frequencies sweep away, a stereo echo trails it, and the new
track opens through a low-pass filter with a more pronounced volume entrance.
Echo delay follows a reliable outgoing beat, or uses 240 ms without one. This
produces an audible effect even without two compatible beat grids. It does not
claim beat matching in that case. The existing beat-locked DropCut can still
override it when the rhythmic conditions are met. Short transitions retain a
plain fade. Classic and original Adaptive do not use SweepEcho.

Regression checks compare the same two-tone signal through SweepEcho and a plain
fade (RMS difference 0.059), check that the delay tail fades to near zero, and
render Sunshower → Heartline from the actual library. That rendered sample peaked
at 0.614, below full scale. These measurements establish an audible signal
change and output bounds; they do not replace listening to each musical pair.

Player settings now offer three Automix timing choices: Classic, the original
Adaptive, and Adaptive (v2). The older modes retain their planners and effects.

V2 analyzes the cached intro and outro features, considers several fade lengths,
and ranks their overlap by simultaneous voice, attacks, level and skipped intro.
It aligns to local beats only when both beat grids are reliable and the tempos
are close (including half/double-time equivalence). A beat-locked drop draws
the outgoing sound down just before a reliable shared beat and brings the new
track in on it, only when both sections have clear rhythmic attacks and little
voice. Other normal-length transitions use SweepEcho, including natural tails.
V2 does not boost the incoming level above unity. A late
render tick inside the silent tail skips the blend rather than forcing a short
transition after the music has stopped.

The feature analysis is heuristic. Stems, key matching, tempo stretching and
perfect phrase detection are not implemented. An uncertain beat grid uses an
unquantized transition. Audio stays in the local player output chain.

Validation: `dotnet run --project tests/AutomixV2Checks -c Release -- <tracks...>`
checks a gentle and percussive synthetic outro, compatible and incompatible
beat grids, the new effect's finite output, and legacy-mode isolation. It was
run on 16 local FLAC songs (15 consecutive pairs) and 40 cached library tracks
(39 consecutive pairs), including an offline rendered real SpaceEcho mix; all
samples were finite and the output peaks stayed below
full scale. Manual listening remains useful for judging taste and perceived
loudness. No transition planner can guarantee a perfect mix for every song.
