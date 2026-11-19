# Asset inventory

Tracked binaries/media are intentional inputs or published presentation artifacts. None are approved for deletion merely because they appear generated or unreferenced. Hashes below identify the reviewed bytes, not ownership or permission.

| Path | Consumer | Provenance / review | SHA-256 |
| --- | --- | --- | --- |
| `docs/WavePlayer-3d-features.png` | README illustration/screenshot | Hero has adjacent prompt; README identifies screenshots as earlier Neon Wave UI. Project screenshots from the earlier Neon Wave interface; attribution: MSIHASH x SENIOR B. | `04a8f075fba440427622b3933c8ec084bf8a9f8b06c478cd3128ad6f23325050` |
| `docs/preview-cover-volume.png` | README illustration/screenshot | Hero has adjacent prompt; README identifies screenshots as earlier Neon Wave UI. Project screenshots from the earlier Neon Wave interface; attribution: MSIHASH x SENIOR B. | `064646cc5097a8af8da409455e85c10c2c97b3d58a75264242f2098c6e726b67` |
| `docs/preview-settings-custom.png` | README illustration/screenshot | Hero has adjacent prompt; README identifies screenshots as earlier Neon Wave UI. Project screenshots from the earlier Neon Wave interface; attribution: MSIHASH x SENIOR B. | `1a29a9d4a3c4f002d77c6a1210a9d331785828c57318ce3cae9a18d24714f4ec` |
| `docs/preview-settings.png` | README illustration/screenshot | Hero has adjacent prompt; README identifies screenshots as earlier Neon Wave UI. Project screenshots from the earlier Neon Wave interface; attribution: MSIHASH x SENIOR B. | `cab6327744244ef63fac6e6fecbca708b1ac5f1c6e990c825298d84ae33b2a56` |
| `docs/preview-theme-hover.png` | README illustration/screenshot | Hero has adjacent prompt; README identifies screenshots as earlier Neon Wave UI. Project screenshots from the earlier Neon Wave interface; attribution: MSIHASH x SENIOR B. | `009a2ce76fd89eb024efb1b3936a561f7ea161fd70f4690337c9f65a9f084457` |
| `docs/preview-widget-ocean.png` | Historical screenshot; no direct reference found | Hero has adjacent prompt; README identifies screenshots as earlier Neon Wave UI. Project screenshots from the earlier Neon Wave interface; attribution: MSIHASH x SENIOR B. | `467bc3fad174053b2994212f400747f54250b5c40e6fff30be2f6ed6200464bd` |
| `docs/preview-widget.png` | README illustration/screenshot | Hero has adjacent prompt; README identifies screenshots as earlier Neon Wave UI. Project screenshots from the earlier Neon Wave interface; attribution: MSIHASH x SENIOR B. | `cdc8bf0312ecc00f2eb396bfe3ebcddcd607ff479d28cd218111233062e93ff3` |
| `installer/src/Assets/mix-adaptive.mp3` | Embedded setup preview; SetupPreview.cs and setup project | Origin/redistribution permission unrecorded; spectrum BIN generation procedure also unrecorded. | `1ef4ff2886731e1f918864bfcb535f07f328a372e04b7abf8981b79ac4e2dbd4` |
| `installer/src/Assets/mix-classic.mp3` | Embedded setup preview; SetupPreview.cs and setup project | Origin/redistribution permission unrecorded; spectrum BIN generation procedure also unrecorded. | `22afbb04bb058b08f6f608fc11d93525d487401b60282a9b5fa3f4f854734f17` |
| `installer/src/Assets/spectrum-demo.bin` | Embedded setup preview; SetupPreview.cs and setup project | Origin/redistribution permission unrecorded; spectrum BIN generation procedure also unrecorded. | `9629e413e1baae747421b5c40b0e8491e3ba1738c247d45e20b39bd58f3488d3` |
| `installer/src/Assets/spectrum-demo.mp3` | Embedded setup preview; SetupPreview.cs and setup project | Origin/redistribution permission unrecorded; spectrum BIN generation procedure also unrecorded. | `6605ab44cec8a7450589e34b794aa9f61b5247110844f548d7a4934a54f4eb51` |
| `promo/Neon-Wave.mp4` | README introduction video | film.jsx authoring source; exact reproduction and permissions unverified. | `cf8e835fda7a3402a10ddc96797eaac01c4c4a920d2ab573c55839ce0d0262b5` |
| `promo/public/player.png` | film.jsx image input | Historical player screenshot; source capture/ownership unrecorded. | `bab3a75f58096b79a3350aa696c8b4d9a1c645dc28ee00f4c5bc98ea94059d26` |
| `promo/public/score.wav` | film.jsx audio input | score.py generates this path; exact regeneration not yet verified. | `6d9bc768f0085da6f1495bfce4906cba882ae314291e29e7999ddf4bc42799f7` |
| `src/SynthwaveNowPlaying/Assets/AutomixDisc.png` | AutomixOverlay resource | Adjacent generation prompt records creation; keep prompt with asset. | `cdc5eeaa7a44356c63ebe3d3935d8cc0224fd1fe936379bf88c4f08d1228a353` |
| `src/SynthwaveNowPlaying/Assets/Waveplayer-Palm-Circle.png` | Design source; no direct runtime reference found | Adjacent prompt retained; maintainer to confirm ownership and relation to icon. | `d64803d54bf5c486eae55f6c2e678c71ed60568f7058ebe2ddfb4f06e7bea4be` |
| `src/SynthwaveNowPlaying/Assets/Waveplayer-Palm.png` | Design source; no direct runtime reference found | Adjacent prompt retained; maintainer to confirm ownership and relation to icon. | `6ff70034bca4b62e215a8d592bebfb9108d262cce9e1abe5a1df5ef93871cf9f` |
| `src/SynthwaveNowPlaying/Assets/WavePlayer.ico` | Player/setup icon, WPF and tray resources | Palm design sources and prompt retained; exact conversion procedure unrecorded. | `b9ed20a824c14f5ce8f39bf61fae4b47c8c5b00fb7ea9a2ac1139e5484e2a882` |
| `src/SynthwaveNowPlaying/Classic/SynthwaveNowPlaying.dll` | Player runtime, setup embedded resource, legacy update ZIP | Historical build and verified SHA-256 in Classic/README.md; Preserved archived runtime assembly; SHA-256 is recorded here and in Classic/README.md. | `e588e55427f8a9f76963d56d97beed7cc1fe4c95ed5e1c2c66aa0ab52a5b528f` |

## Design and generated-output boundaries

Keep docs/WavePlayer-3d-features.prompt.txt with the hero image, Assets/AutomixDisc.prompt.txt with the overlay disc, and Assets/Waveplayer-Palm.prompt.txt with the palm sources. Prompts document creation intent, not proof of redistribution permission. The hero prompt uses docs/preview-widget.png as a reference.

No byte-identical duplicates were found in this inventory. Similar screenshots are not interchangeable: promo/player.png is a separate film input. Confirm external uses before removing unreferenced images.

Installer previews reference all three MP3s and spectrum-demo.bin; spectrum frames are synchronized at 60 frames/second. Preserve the media pair and logical resource names. Setup identifies the mix example as Revolve → Don't Sleep Yet; permission evidence is not recorded here.

Generated installer/payload, payload-legacy, intermediate, release, project bin/obj, root .build/.verify and promo/node_modules are ignored output, not source. Tracked Classic DLL, score.wav and Neon-Wave.mp4 remain retained assets even though they originated from generation/build steps.

Maintainers must confirm asset permissions, historical screenshot/design ownership and Classic reproducibility before claiming distribution clearance. See ../THIRD_PARTY_NOTICES.md.

