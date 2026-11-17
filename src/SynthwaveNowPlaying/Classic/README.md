# Classic visualizer

Classic loads the original visualizer, FFT analyzer and sample buffer directly from the archived player assembly. The binary shipped in this directory is the pre-sort build from `.build/adaptive-fx-test/SynthwaveNowPlaying.dll` (SHA-256 `E588E55427F8A9F76963D56D97BEED7CC1FE4C95ED5E1C2C66AA0AB52A5B528F`). Keep it alongside the host under `Classic/` when distributing the player.

The host creates only those three objects in a separate assembly load context. SpectrumTap supplies 20 ms packets of local player PCM to the original sample buffer. Current mode uses the current player's 16 ms buffers and renderer.
