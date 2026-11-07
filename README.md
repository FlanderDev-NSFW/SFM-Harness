# Harness 0.1.0

TODO: what the mod does, in a sentence or two for players of **Secret Flasher Manaka**.

## Requirements
- Secret Flasher Manaka (tested with v1.1.3)
- BepInEx 6 for IL2CPP (tested with build 738)
- **RestraintHub 2.0.0** or newer (2.x)

## Install
Extract the zip into the game folder (the folder with `SecretFlasherManaka.exe`). You get `BepInEx/plugins/Harness/`.

## How to use
TODO: keys and where it is in the game.

## Settings
`BepInEx/config/FlanderDev.SFM.Harness.cfg` (created on the first start).

## Good to know
- TODO: known limits.

Single-player / offline only. Code: MIT licence (`LICENSE.txt`).

## Building from source
- .NET SDK 8, and a copy of the game with BepInEx 6 IL2CPP started once: copy `Directory.Build.local.props.example` to
  `Directory.Build.local.props` and set `GameDir`. Then `dotnet build src/Harness/Harness.csproj -c Release`.
- The source of the mod is `src/Harness` and its tests (`tests/`); `lib/SfmKit` is a copy of the shared
  [SfmKit](https://github.com/FlanderDev-NSFW/SfmKit) library; `docs/` holds the developer notes.
- Every push to `main` builds, tests and releases it (`.github/workflows/release.yml`, `build/ci.py`).
