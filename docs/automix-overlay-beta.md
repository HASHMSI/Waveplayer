# Automix overlay beta

Local beta: 1.0.7-beta.1. Backup before implementation:
`backup/pre-automix-overlay-beta-2026-11-18/` (source ZIP, installed application files and settings).

The 280×132 DIP popup appears near the top-right work area only while the player
is hidden and an actual transition has started. Adaptive scheduling uses the
deck's rendered sample age; the decoder prewarm period does not trigger it.
Classic uses its existing transition progress. Audio timing/processing is unchanged.

Each half shows its own track title, artist, sharp cover thumbnail and a blurred
version of that same cover. Artwork decoding runs on a worker, outside the mixer
lock. Missing artwork uses a fallback gradient. Late artwork can update only
the still-current popup. The independently generated vinyl bitmap rotates during
mixing, stops on pause/handoff and is decoded at thumbnail resolution.

On handoff the entire card fades to the incoming track. It stays briefly, then
slides/fades out. It is click-through, nonactivating, excluded from the taskbar
and Alt-Tab; it does not take focus from the user's working application.

Tray menu:

- **Automix popup · On/Off** persists the visibility preference.
- **Preview Automix popup · Beta** runs a seven-second silent animation followed
  by the completion state and dismissal. It does not play, seek or reorder music.
- `Waveplayer.exe --preview-automix` triggers the same preview, including through
  the running player's single-instance signal.

Closing/cancelling a transition dismisses its popup. Preview/animation timers
are stopped on close. Restoring the main player dismisses the real-transition
notification on its next update. Previews remain explicitly available while
the main player is visible. No persistent visualization cache was introduced.

Validation: release build passed without warnings/errors; real-cover snapshots
were inspected for both mixing and completion. A WPF harness checked text bounds,
native nonactivation/toolwindow styles, unchanged foreground focus, pause/resume,
rotating-disc animation, correct per-track backdrop references, completion and
the full timed preview automatically closing with no remaining preview timer.
# Overlay size

Settings → Player → Automix popup / Size offers a 65–180% slider and a silent preview button. While the preview is visible, the slider resizes the actual popup live; Save Settings persists the scale for future transitions. Invalid saved values fall back to 100%.
