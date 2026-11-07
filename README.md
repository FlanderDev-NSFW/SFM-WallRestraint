# WallRestraint 2.0.0

Wall restraints for **Secret Flasher Manaka**: she is locked against a wall, lifted off the floor. A collar holds her neck,
a rest arches her back, cuffs hold her upper arms, and her shins go into tubes in the wall.

Model: ["Display Model - Wall Restraint"](https://smutba.se/project/0c0e09e3-9217-4634-a568-54e9c23285a7/) by bruhmoment ([CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/)).

## Requirements
- Secret Flasher Manaka (tested with v1.1.3)
- BepInEx 6 for IL2CPP (tested with build 738)
- **RestraintHub 2.0.0** or newer (2.x)

## Install
Extract the zip into the game folder (the folder with `SecretFlasherManaka.exe`). You get `BepInEx/plugins/WallRestraint/`.

## How to use
Stand facing a flat wall, open the restraint panel with `U` and click **Wall Restraint** (in Placeables): it is fitted to the
wall. If the wall does not fit (too far, bumpy, furniture in the way), a message says why. Walk to it and press `F` to be locked
in. It stays there (also in the save) until you pick it up in the panel ("Pick up: Wall Restraint"); one per map. While locked:

| Key | Does |
|---|---|
| `+` / `-` | raise / lower her (as far as the wall allows, down to kneeling) |
| hold `F` | struggle free |

The camera starts behind her, inside the wall: turn it round to see her.

Missions (Custom Missions v2) can put it on (at the wall in front of her) and lock it through RestraintHub: `RtHub_CanApply("WallRestraint")` tells whether there is a wall, `RtHub_Apply("WallRestraint")`, setting `height` (cm, her knees) - see [Missions.md](https://github.com/FlanderDev-NSFW/SFM-RestraintHub/blob/main/docs/Missions.md).

## Settings
`BepInEx/config/FlanderDev.SFM.WallRestraint.cfg` (created on the first start). Most useful:

| Setting | Default | |
|---|---|---|
| Placement.Reach | 1.2 | how far in front of her a wall may be (m) |
| Pose.KneeHeight | 2.4 | preferred height (`+` / `-` store theirs here) |
| Unlock.SuccessChance | 1 | chance that struggling frees her (1 = first try) |

## Good to know
- Being restrained is not saved: after loading she is free (a placed restraint stays).
- Through glass walls the legs behind the wall can be seen.
- Curtains can count as a wall.
- After struggling free she faces away from the wall.

Single-player / offline only. Code: MIT licence (`LICENSE.txt`). Model: ["Display Model - Wall Restraint"](https://smutba.se/project/0c0e09e3-9217-4634-a568-54e9c23285a7/) by bruhmoment, CC BY-NC-ND 4.0 (smutba.se; details in `LICENSE-MODELS.txt`).

## Building from source
- .NET SDK 8, and a copy of the game with BepInEx 6 IL2CPP started once: copy `Directory.Build.local.props.example` to
  `Directory.Build.local.props` and set `GameDir`. Then `dotnet build src/WallRestraint/WallRestraint.csproj -c Release`.
- It compiles against RestraintHub: the [SFM-RestraintHub](https://github.com/FlanderDev-NSFW/SFM-RestraintHub) repository next to this one, `lib/RestraintHub/RestraintHub.dll`, or the hub installed in the game.
- The source of the mod is `src/WallRestraint` and its tests (`tests/`); `lib/SfmKit` is a copy of the shared [SfmKit](https://github.com/FlanderDev-NSFW/SfmKit)
  library; `docs/` holds the developer notes.
- Every push to `main` builds, tests and releases it (`.github/workflows/release.yml`, `build/ci.py`).
