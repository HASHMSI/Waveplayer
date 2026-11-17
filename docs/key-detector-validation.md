# Musical key detector validation

Checked 2026-11-18. The engine estimates the sounding key of the local audio;
internet catalogs are comparison data, not ground truth. File names were matched
to the original versions, not to sped-up/slowed variants. No internet value is
used as a hard-coded override in the detector.

## Independent signal checks

`dotnet run --project tests/KeyDetectorChecks -c Release`

77 checks passed: all 12 major and 12 minor progressions at -27, 0 and +27 cents
(72 cases), white noise, a single sine tone, silence, phase-inverted stereo,
and cancellation. Fixtures contain chord changes, bass, overtones and kick hits.
These tests check transposition, mode, tuning and basic rejection; they are not
a benchmark of accuracy on commercially produced music.

## Local recording comparison

Six real recordings were analyzed with the final engine. Time per file was
about 0.6–0.9 seconds on the development laptop. An evidence score is an internal
ranking score, not a calibrated probability. A tentative candidate is shown here
for diagnosis even when the player correctly displays UNSURE.

| Recording | Player display | Candidate | Internet comparison | Assessment |
| --- | --- | --- | --- | --- |
| ChromeWave — Chrome Cassette | UNSURE | B minor | No matching key listing found after targeted searches. Album/title confirmed by [Apple Music](https://music.apple.com/us/artist/chromewave/1853663378). | Unverified, not counted as correct. |
| Eternxlkz — BRODYAGA SYNTH | B minor / 10A | B minor | [SongBPM](https://songbpm.com/%40eternxlkz/brodyaga-synth-g24x7) reports B major at the matching tempo and duration. | Same tonic, mode disagrees. Not a confirmed match. |
| VXLLAIN / iGRES / ENXK — Oceanfade (with ENXK) | UNSURE | B major | [Trackify](https://trackify.am/track/3IvxO1V4SVpKm2GHghjJuP/tempo) reports G-sharp/A-flat, 180 BPM, 2:53; the excerpt does not specify mode. | Candidate differs; detector abstains. Sped-up/over-slowed listings were excluded. |
| $uicideboy$ — Frenzy | B minor / 10A | B minor | [DJ-Pool](https://dj-pool.org/2026/01/16/page/2/) lists 10A for an intro edit; [SongBPM](https://songbpm.com/%40uicideboy/frenzy-cdt3e) reports D-flat major for the original. | Partial support from an edit, conflicting original catalog. Not a clean ground-truth match. |
| VXLLAIN — Those moments | C major / 8B | C major | [SongBPM](https://songbpm.com/%40vxllain/those-moments-jszg1) reports C major, 172 BPM, 2:37. | Exact agreement with the catalog. |
| $uicideboy$ — Antarctica | UNSURE | B minor | [Tunebat](https://tunebat.com/Info/Antarctica-uicideboy-/3NSgQjYihV40pOmn8AQevz) reports D major, while [Chordify](https://chordify.net/chords/uicideboy-songs/antarctica-chords) reports B minor. | Relative major/minor conflict; detector abstains. |

This small comparison does **not** establish high real-world key accuracy.
It demonstrates one exact catalog agreement, one mode disagreement, conflicting
references and conservative abstention. A larger independently annotated audio
set is required to quantify accuracy. Do not advertise the synthetic pass rate
as a music-library accuracy percentage.

## Method and scope

The managed implementation uses the existing NAudio decoder/resampler and FFT.
At most seven 24-second sections are read. Stereo channels contribute power
separately, preserving wide or phase-inverted pads. Spectral peaks form a
36-bin chromagram; tuning offsets are corrected before folding to 12 pitch
classes. Published Krumhansl–Kessler, Temperley and diatonic profiles are compared
with polyphonic overtone compensation and section agreement.

Relevant method references: [Essentia KeyExtractor](https://essentia.upf.edu/reference/std_KeyExtractor.html),
[Essentia Key](https://essentia.upf.edu/reference/std_Key.html),
and [Sha'ath's key estimation dissertation](https://ibrahimshaath.co.uk/keyfinder/KeyFinder.pdf).
The code was implemented independently; GPL libkeyfinder/Essentia source was not
copied or linked. No model weights or new player runtime dependencies were added.

Keys are cached separately from BPM. Upgrading a pre-key cache retains completed
BPM scans. Opening a song runs a serialized background key scan; opening the
playlist does not scan every file. The settings scanner fills missing keys for
cached BPM rows. Source audio files and tags are never modified. This change
displays key estimates; it does not transpose audio or change Automix decisions.
