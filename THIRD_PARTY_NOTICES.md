# Third-party notices

Copyright © 2026 MSIHASH x SENIOR B applies to the project's attribution; dependencies retain their own notices. WavePlayer was vibe coded via Codex. See COPYRIGHT.txt for the project's current copyright notice.

## Restored .NET package graph

The following entries were read from the restored package nuspec files and the player's project.assets.json on 2026-11-19. This includes build/reference packages; membership does not mean every package is redistributed in the installer. License identifiers below are the packages' declarations.

| Package | Resolved version | Declared license | Authors | Upstream |
| --- | --- | --- | --- | --- |
| NAudio.Core | 3.3.0 | MIT | Mark Heath | https://naudio.github.io/NAudio/ |
| NAudio.Midi | 3.3.0 | MIT | Mark Heath | https://naudio.github.io/NAudio/ |
| NAudio.Wasapi | 3.3.0 | MIT | Mark Heath | https://naudio.github.io/NAudio/ |
| NAudio.WinMM | 3.3.0 | MIT | Mark Heath | https://naudio.github.io/NAudio/ |
| NAudio | 3.3.0 | MIT | Mark Heath & Contributors | https://naudio.github.io/NAudio/ |
| System.Numerics.Tensors | 9.0.0 | MIT | Microsoft | https://dot.net/ |
| TagLibSharp | 2.3.0 | LGPL-2.1-only | Brian Nickel, Gabriel Burt, Stephen Shaw, etc | https://github.com/mono/taglib-sharp |

TagLibSharp's package copyright is: Copyright (c) 2006-2007 Brian Nickel. Copyright (c) 2009-2020 Other contributors. Its declared license is LGPL-2.1-only. NAudio packages declare MIT. These declarations must not be replaced by the WavePlayer copyright notice.

## Promo authoring dependencies

Exact direct-package versions and license fields from promo/package-lock.json:

| Package | Locked version | Lockfile license field |
| --- | --- | --- |
| @remotion/cli | 4.0.544 | SEE LICENSE IN LICENSE.md |
| remotion | 4.0.544 | SEE LICENSE IN LICENSE.md |
| react | 19.3.1 | MIT |
| react-dom | 19.3.1 | MIT |

The complete transitive promo dependency graph, integrity hashes and declared license fields remain in promo/package-lock.json. These tools author the introduction video; they are not referenced by the player or setup projects. The promo package's existing ISC metadata applies only to that package's declaration and is not a repository-wide license selection.

## Runtime and other assets

The setup script publishes the .NET and Windows Desktop runtime self-contained for win-x64. Runtime versions are selected by the installed SDK at publish time; inspect the resulting WavePlayer.deps.json for each release rather than assuming a fixed runtime version from this document.

The archived Classic DLL is a separately loaded shipped binary with a verified SHA-256 and historical origin in its README. Installer previews embed three MP3 files and spectrum-demo.bin. Artwork, screenshots, prompts, generated score and film inputs are individually listed with consumers and hashes in docs/assets.md.

Preserve the upstream copyright notices and license terms when distributing these components. The asset inventory records their consumers and SHA-256 identifiers; package metadata records the dependency versions used by the application.
