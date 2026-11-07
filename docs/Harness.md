# Harness

> Developer notes (written in [sfm-modkit](https://github.com/FlanderDev-NSFW), where the mod is developed with
> an in-game test bridge; paths like `./tools/dev.ps1` and `docs/knowledge/` refer to that kit): how it works, settings and
> diagnostics. The player readme (shipped in the release zip) is [`README.md`](../README.md).

A wearable harness for SecretFlasherManaka (BepInEx 6 IL2CPP): belt, thigh hoops and two pistons driven by crossed cords.
Single-player/offline only.

**Needs RestraintHub** (2.0.0+, a separate download): the panel row, the worn list, F routing and missions are the hub's. Holds
no body part and blocks nothing, so it can be worn with any other restraint.

**Status 2.0.0:** first release (2.x like the other restraint mods for RestraintHub 2).

## How it works
- **Model:** `src/Harness/assets/harness.hrn` (embedded), the mod's own format (`Logic/HarnessFile.cs`), exported from the model's
  Blender source. Credit and licence: `LICENSE-MODELS`.
- **Fit (`HarnessRig.cs`):** placed by body landmarks from a bake of her body: the belt and the thigh hoops shaped to her outline,
  the guides on the game's pelvis anchors, pistons and guides with their own size (`Fit.PelvisSize`) and a clearance report.
- **Drive:** each piston's stroke follows its cord's length between the thigh hoops - they move as she moves her legs.
- **Lock:** SfmKit's `LockMachine` (`Unlock.*`); the worn state is saved in the slot (`Harness.Locked`) through the hub's
  `OnSave` / `OnLoad`.

## Config (`BepInEx/config/FlanderDev.SFM.Harness.cfg`)
| Key | Default | Meaning |
|---|---|---|
| Unlock.AttemptSeconds | 4.5 | seconds of holding F per attempt |
| Unlock.SuccessChance | 1 | chance that an attempt frees her |
| Fit.Size | 0.78 | hardware and straps against her body |
| Fit.BeltHeight | 0.55 | belt from hip joints (0) to chest (1) |
| Fit.HoopAlongThigh | 0.6 | hoops from hip (0) to knee (1) |
| Fit.PelvisSize | 0.6 | pistons and guides against the rest |
| Fit.FrontSeat | 0 | front guide out (+) / in (-), mm |
| Fit.GuideDrop | 0.03 | guide band centre below its anchor (model metres) |
| Fit.ScaleTrim | 1 | multiplies the model's size |

## Diagnostics (bridge, session started with `--allow-invoke`)
`game call Harness.HarnessController PutOnNow`, `MeasureNow`, `Stroke <0..1>` (forces the stroke), `game get
Harness.HarnessController::Status` (size, strokes, cord lengths). Log markers: `[SFM-ACCEPT] load:`, `parts:`.

## Verification
- L3 2.0.0 port (2026-11-07, RestraintHub 2.0.0): listed in the hub's panel (`U`, Wearables) next to the other wearables.
- Earlier fit work (2026-11-06): see the commit history.

## Next
- Influence the ecstasy gauge like the game's piston machine (planned; `docs/knowledge/api-notes/player-stats.md`).
