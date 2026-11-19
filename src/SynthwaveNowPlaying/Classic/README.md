# Classic visualizer

Classic loads the original visualizer, FFT analyzer and sample buffer directly from the archived player assembly. The binary shipped in this directory is the pre-sort build from `.build/adaptive-fx-test/SynthwaveNowPlaying.dll` (SHA-256 `E588E55427F8A9F76963D56D97BEED7CC1FE4C95ED5E1C2C66AA0AB52A5B528F`). Keep it alongside the host under `Classic/` when distributing the player.

The host creates only those three objects in a separate assembly load context. SpectrumTap supplies 20 ms packets of local player PCM to the original sample buffer. Current mode uses the current player's 16 ms buffers and renderer.

## Consumers and provenance limits

The player copies this DLL to Classic/SynthwaveNowPlaying.dll in build/publish output and loads it in an isolated context. Setup embeds it as Setup.Classic.dll; the legacy portable ZIP explicitly includes it. It is required runtime input, not disposable output.

The historical ignored build location does not establish a reproducible source build of these exact bytes. Preserve the hash until a separately reviewed replacement proves equivalent behavior and provenance. The adapter's same-identity comment predates the host's WavePlayer assembly name.
