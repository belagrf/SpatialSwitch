# Which games should be on the watch list?

SpatialSwitch exists for games that render their **own** binaural / HRTF audio in-engine. Layering a
Windows spatializer (Dolby Atmos for Headphones, Windows Sonic, DTS Headphone:X) on top of that output
runs the already-binaural signal through a second generic HRTF — smearing the very directional cues the
game engineered. For those games: watch them, active format **Off**.

Games that only output a stereo or 7.1 bed have nothing to double-process; there, a Windows spatializer
is a legitimate immersion option and there is no need to watch them. And games built for Windows Spatial
Sound *want* it on.

This list is compiled from each game's own audio settings menu and developer notes — verify in-game,
options change between patches. Pull requests welcome.

## Watch these (native HRTF / binaural in-engine) → active format Off

| Game | In-game option to keep ON | Process to watch |
|---|---|---|
| Valorant | *Enable HRTF* (Speaker Configuration: Stereo). Riot: "turn off any other 3D audio processing" | `VALORANT.exe`, `VALORANT-Win64-Shipping.exe` |
| Counter-Strike 2 | Steam Audio HRTF (*Advanced 3D audio processing*) | `cs2.exe` |
| Overwatch 2 | *Dolby Atmos for Headphones* (Blizzard's in-engine render) — or stereo | `Overwatch.exe` |
| Fortnite | *3D Headphones* | `FortniteClient-Win64-Shipping.exe` |
| Escape from Tarkov | *Binaural audio* | `EscapeFromTarkov.exe` |
| Rust | *HRTF* | `RustClient.exe` |
| PUBG: Battlegrounds | *HRTF* | `TslGame.exe` |
| Battlefield 2042 / V | *Headphones (3D)* speaker mode | `BF2042.exe`, `bfv.exe` |

## Do NOT watch (built for Windows Spatial Sound — leave Atmos/Sonic on)

Call of Duty (Modern Warfare 2019 and later), Halo Infinite, Gears 5, Forza Horizon — these send audio
objects / height beds to the Windows spatial pipeline; the Windows renderer *is* their intended path.

## Don't need watching (no native HRTF; a Windows spatializer is optional)

Rainbow Six Siege (pros tend to prefer plain stereo — your call), Apex Legends, Deep Rock Galactic,
Satisfactory, Valheim, Minecraft, Roblox, and most co-op/party titles.

## Notes

- Some games launch through a stub executable first (Valorant's `VALORANT.exe` starts seconds before the
  shipping exe). Watching both gives SpatialSwitch a head start before the game initialises audio.
- Valorant since patch 8.0 offers *Speaker Configuration: Auto-Detect* as an officially supported way to
  use a Windows spatializer instead of Riot's HRTF (it disables HRTF automatically). If you prefer that
  path, simply don't watch Valorant.
