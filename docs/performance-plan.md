# Performance plan (research only)

No persistent spectrum/PCM cache is implemented by this plan. Keep the existing
16 ms mode unchanged; compare future options against its response and timing.

## Order of work

1. **Measure first.** Separate decoder/mixer callback time, rendering CPU/GPU,
   allocation/GC, and audio scheduling stalls. Record callback overruns and frame
   time percentiles during playback, Automix, scanning and background load.
   High process CPU alone does not identify the cause of audible glitches.
2. **Protect audio deadlines.** Consider a bounded decoder read-ahead ring in RAM
   and an event-driven WASAPI/MMCSS experiment. Avoid file reads, lengthy locks,
   UI work and allocations on the audio servicing path. Changes must be compared
   for sound stability and visual alignment, not only smaller CPU averages.
3. **Avoid invisible work.** Tray hiding can suspend visualization/rendering
   while preserving audio, track advancement, Automix and file watching.
   This lifecycle handling is part of the tray feature, not a new disk cache.
4. **Optimize rendering separately.** Cache static scene layers/geometry/brushes;
   profile animated layers before changing them. Retain the default 16 ms
   response. A cached spectrum still needs animated rendering every visible frame.
5. **Reuse existing analyses.** BPM/key/transition profiles already persist.
   Add bounded thumbnail/metadata reuse only if memory and allocation measurements
   identify artwork loading as a bottleneck.
6. **Optional spectrum cache.** Only if measured FFT work is significant, store
   pre-smoothing band energies plus timestamps for the raw player input. Reuse
   on repeat playback, with live processing during EQ, Automix or other changes
   to the audible signal. Do not sum cached magnitudes to impersonate an actual mix.

## Cache sizes and correctness

These are estimates, not measured compression results. With 56 bands, 16 ms
spacing, 180 seconds and 16-bit samples, a spectrum is about 1.20 MiB before
headers/compression. A thousand songs would exceed 1 GiB. By comparison, storing
180 seconds of stereo 44.1 kHz float PCM needs about 60.6 MiB per song. A bounded
two-second float PCM read-ahead ring needs about 0.67 MiB.

A future spectrum cache should have an LRU budget, a user-selectable location,
atomic writes, a versioned schema and invalidation by audio identity, size,
modification time, channel mix and analysis configuration. Seek uses track
position/sample time, not wall-clock playback time. Cache failure always falls
back to the live engine. No folder paths or user songs belong in the repository.

The current FFT already reuses its 1024-point work arrays and twiddle tables.
Therefore a disk cache is not assumed to be the biggest win. It adds I/O,
invalidation and synchronization costs and cannot fix driver scheduling by itself.

## Sources

- [NAudio output device types](https://github.com/naudio/NAudio/blob/main/Docs/OutputDeviceTypes.md):
  buffer servicing and total latency. More output buffering trades responsiveness
  for scheduling headroom; it does not guarantee glitch-free playback.
- [Windows low latency audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio):
  driver-dependent periods, scheduling and power tradeoffs.
- [MMCSS](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service):
  prioritize time-sensitive audio work through supported APIs, not process-wide
  real-time priority or registry changes.
- [WPF graphics performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-2d-graphics-and-imaging):
  reusable drawing resources and graphics caching tradeoffs.
