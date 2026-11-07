# Harness 2.0.0

A harness for **Secret Flasher Manaka**: a belt around her waist, hoops around her thighs and two pistons between her legs,
driven by crossed cords from the thigh hoops - they move as she moves her legs. Fitted to her body: sized and shaped to her
waist, pelvis and thighs.

Model: made for this mod by FlanderDev, the design after fan art by [yakou450770](https://www.pixiv.net/en/users/35003939)
([CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/); details in `LICENSE-MODELS.txt`).

## Requirements
- Secret Flasher Manaka (tested with v1.1.3)
- BepInEx 6 for IL2CPP (tested with build 738)
- **RestraintHub 2.0.0** or newer (2.x)

## Install
Extract the zip into the game folder (the folder with `SecretFlasherManaka.exe`). You get `BepInEx/plugins/Harness/`.

## How to use
Open the restraint panel with `U` and click **Harness** (in Wearables). Hold `F` to struggle free. It holds no body part, so it
can be worn together with the other restraints, and it blocks none of her actions.

## Settings
`BepInEx/config/FlanderDev.SFM.Harness.cfg` (created on the first start). Most useful:

| Setting | Default | |
|---|---|---|
| Fit.Size | 0.78 | size of the hardware and straps against her body |
| Fit.BeltHeight | 0.55 | where the belt sits, from her hip joints (0) to her chest (1) |
| Fit.HoopAlongThigh | 0.6 | where the thigh hoops sit, from hip (0) to knee (1) |
| Unlock.SuccessChance | 1 | chance that struggling frees her (1 = first try) |

## Good to know
- Saved with the game: after loading she still wears it.
- Clothes are not measured: skirts and trousers can show through it.

Single-player / offline only. Code: MIT licence (`LICENSE.txt`). Model: CC BY-NC-ND 4.0 (`LICENSE-MODELS.txt`).

## Building from source
- .NET SDK 8, and a copy of the game with BepInEx 6 IL2CPP started once: copy `Directory.Build.local.props.example` to
  `Directory.Build.local.props` and set `GameDir`. Then `dotnet build src/Harness/Harness.csproj -c Release`.
- The source of the mod is `src/Harness` and its tests (`tests/`); `lib/SfmKit` is a copy of the shared
  [SfmKit](https://github.com/FlanderDev-NSFW/SfmKit) library; `docs/` holds the developer notes.
- Every push to `main` builds, tests and releases it (`.github/workflows/release.yml`, `build/ci.py`).
