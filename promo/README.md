# Introduction video sources

This independent Remotion project authors the historical Neon Wave introduction; it is not needed for player builds. Preserve Neon-Wave.mp4 and its branding until a replacement is reviewed.

Run from this directory with compatible Node.js/npm and Python 3. The original authoring runtime versions were not recorded.

```powershell
npm ci
# Optional: overwrites tracked public/score.wav; review before keeping.
python score.py
# Render outside tracked media.
npm run render
```

film.jsx registers NeonWave at 1280x720, 30 fps, 720 frames (24 seconds), using public/player.png and public/score.wav. score.py uses Python standard-library modules and a fixed seed but writes relative to the working directory. Exact reproduction has not been verified; browser/fonts/runtime versions can change results.

Use `npm run studio` to inspect the composition interactively and `npm run score` to regenerate the soundtrack. There is no automated promo test suite. Player CI does not run promo commands. Review Persian text/fonts, frames, duration, audio and credits before replacing the published movie. See [assets](../docs/assets.md) and [inventory](../THIRD_PARTY_NOTICES.md) for ownership/terms gaps.


Copyright © 2026 MSIHASH x SENIOR B. WavePlayer was vibe coded via Codex.
