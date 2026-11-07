# WallRestraint

> Developer notes (written in [sfm-modkit](https://github.com/FlanderDev-NSFW), where the mod is developed with
> an in-game test bridge; paths like `./tools/dev.ps1` and `docs/knowledge/` refer to that kit): how it works, settings in detail, diagnostics and the test history. The player readme (shipped in the
> release zip) is [`README.md`](../README.md).
>
> **1.4.0 (2026-10-27): first release** (F3M). Plugin GUID `FlanderDev.SFM.WallRestraint` (was `com.local.wallrestraint`), so the
> config file is `BepInEx/config/FlanderDev.SFM.WallRestraint.cfg`. The release zips no longer include RestraintHub (it is its own download).

Wall-mounted restraints for SecretFlasherManaka (BepInEx 6 IL2CPP): she is held against a wall, lifted off the floor - neck in a
collar, back arched over a rest, upper arms in cuffs beside it, knees bent with the shins going through leather-lined tubes into the
wall. Single-player/offline only.

**Needs RestraintHub** (2.0.0+, a separate download): a placeable (`WallRestraintFitting`: placed from the panel, kept per map
and in the save by the hub, "F Wall Restraint" at it) whose in-use wearable is `WallRestraint` (the list of worn restraints, F
and + / - routing, input blocking, conflicts, missions).

**2.0.0 (RestraintHub 2):** placing probes the wall in front of her as locking did (`FindSpot`); the placed pose is the wall frame
(on the floor at the surface, +Z out of the wall) and its data the chosen knee height and the range the wall allowed. F locks her
there without probing again (the preferred height clamped into the saved range; the wall collider found again by a ray for the
room check of + / -); the empty restraint is hidden while she is in it. Missions (`RtHub_Apply`) still use the wall in front of
her. The ring entry and its shortcut save key are gone. Refused while another restraint holds her
neck, arms or legs (the yoke, the spreader bar, the game's handcuffs).

**Status 2.0.0:** usable. Face a flat wall (within `Reach`), place **Wall Restraint** from the hub's panel (`U`, Placeables), walk
to it and press **F**; hold **F** to struggle free (`Unlock.SuccessChance`, default: the first attempt). Refusals are shown in the game's message bar ("Can't use the wall restraint here: ..."). Being restrained is not saved:
a load starts free. While restrained, walking, running, crouching, peeing, pose skills and everything that needs her hands
(masturbating, toys, the handcuffs, drinking, dropping items, clothes; 1.2.0: refused with a message) are ignored;
the camera can still be turned (it starts behind her, inside the wall - turn it half round).

**Height (1.1.0):** she is placed as high as the wall allows, up to the preferred `Pose.KneeHeight` and down to kneeling (knees
just above the floor). While restrained, **+ / -** raise / lower her in `Pose.StepCm` steps within what that wall allows (the
message bar says when the wall ends or she is kneeling); the result is remembered in the config for the next time. Low walls
and fences work now: a 1.64 m concrete fence takes her with the knees at 0.8 m.

## How it works
- **The model** comes from the `RestraintStuffs` collection of `TheWall.blend` (user's asset, may be shipped), exported by
  `tools/export_model.py` into `assets/wallrestraint.wrm` (embedded in the DLL):
  `blender -b --disable-autoexec TheWall.blend --python mods/WallRestraint/tools/export_model.py -- mods/WallRestraint/assets/wallrestraint.wrm`.
  Parts per kind / side / material in wall coordinates (x out of the wall, y sideways, z up from the floor) plus anchors (collar
  ring, back rest, arm rings, tube mouths). Format: `Logic/WallModel.cs`.
- **Her body** is measured from the bones at lock time (sizes differ per save: the first save's character is ~1.5 m, the "Amateur"
  save's much smaller). `Logic/WallPose.cs` lays out the pose from the knees up and scales each restraint to its limb
  (radii estimated from bone lengths: neck 7 % of the spine, upper arm 7.5 %, knee 15 % of the thigh; + `Model.ClearanceCm`).
- **The wall:** a ray finds the collider in front of her, then `DepthProbe` renders the patch the restraints cover with Unity's
  depth shader (her renderers switched off for it) and `Logic/WallSpot.cs` fits a plane: refused when anything is missing (invisible
  `PlayerBlocker` walls), bumpy (> 1.5 cm) or turned; the restraints snap to the drawn surface (colliders can be up to 18 cm off,
  see `docs/knowledge/api-notes/level-geometry.md`). A box check keeps furniture out of her way.
- **The pose** is held in `LateUpdate` (hips placed and turned, each bone aimed at its target joint, using the bone lengths as
  drawn - the game changes them after LateUpdate, see `WallPoser`); the restraints follow the drawn bones in `Camera.onPreCull`.
  The leg tubes close around the shin below the knee, lifted over the calf. Level walls cannot be cut, so a dark disc inside each tube stands in for the hole.

## Config (`BepInEx/config/FlanderDev.SFM.WallRestraint.cfg`)
| Key | Default | Meaning |
|---|---|---|
| General.RingLabel | Wall Restraint | name in the panel, at the placed restraint (F) and in the worn list (key name kept from 1.x) |
| Unlock.AttemptSeconds | 4.5 | seconds of holding F per attempt |
| Unlock.SuccessChance | 1 | chance an attempt frees her (failures add 0.02, like the game's handcuffs) |
| Placement.Reach | 1.2 | how far in front of her a wall may be (m) |
| Pose.KneeHeight | 2.4 | preferred knee height in shin lengths (2.4 = lifted high, ~0.2 = kneeling); lower walls place her lower; + / - store their result here |
| Pose.StepCm | 3 | one + / - step (cm) |
| Pose.KneeOutside | 1.3 | knee joint outside the leg tube's mouth, in knee radii |
| Pose.TubeLift | 0.35 | leg tubes above the shin bones, in knee radii (the calf is above the bone when the shins are level) |
| Model.TubeClearanceCm | 1 | room between leg and leg tube (cm) |
| Model.ClearanceCm | 0.4 | room between neck / upper arms and collar / arm cuffs (cm; -1 .. 5, applied at the next lock) |
| Pose.ThighLean | 20 | thighs lean out of the wall (degrees) |
| Pose.ArchBulge | 0.04 | how far the rest pushes her chest out (m) |
| Model.MetalShade | 0.3 | brightness of the metal parts |
| Debug.Disable | 0 | bit 1 = do not pose the body; bit 2 = aim with the LateUpdate bone lengths (negative control for the leg fix) |

## Diagnostics (bridge, session started with `--allow-invoke`)
`game call WallRestraint.WallController LockHere | LockForced | Release | FaceNearestWall | Bones | View <out> <side> <up> | ViewOff`,
`game call WallRestraint.WallSurvey Run` (walls around her; output in the BepInEx log), `game get WallRestraint.WallController::MovedAfterLateUpdate`.

## Verification
- L1: `./tools/dev.ps1 test WallRestraint` - model file, format errors, wall frame, wall patch decisions, pose layout for both
  measured body types.
- L3 1.0.0 (2026-10-24, first save, Residence house wall `AE_House_10_LOD0` at (-30.9, -115.7)): lock accepted (surface offset 0.0 cm,
  bump 0.0 cm); screenshots from the side and front show her lifted on the wall, knees in the tubes, upper arms in the cuffs, collar
  on the neck; negative control `Debug.Disable = 1`: she stands on the floor under the empty restraints. Refusals seen: Apartment wall
  next to the nightstand (84 cm bump), the same house wall while her own body was still in the probe (fixed).

- L3 1.0.0 ring/unlock (2026-10-24, first save, with SpreaderBar installed): entry added at index 32 and shown on a 5th ring page;
  released ring key 1 over it -> key 1 shows "Wall Restraint", the stored index stayed 13 (the key's previous game entry, not 32);
  after `travel Residence` key 1 still held it; tapping 1 at the house wall locked her ("lock: locked to the wall"), holding F 4.5 s
  freed her ("attempt: success", restraints gone, she stands); tapping 1 facing the open alley showed "Can't use the wall restraint
  here: no wall in front of her". Save -> restart -> load: no exception, stage started, key 1 = wall restraint, key 4 = spreader bar.

- L3 1.1.0 heights (2026-10-24, first save): Residence house wall - wall allows knees 0.556..1.296 m (the stone plinth at its foot
  stops it lower), chosen 1.036 (preferred); 17 x "-" stepped down to 0.556, the next press showed "The wall is not flat enough lower
  down"; "+" raised it again (config followed). Concrete fence `Fence_Concrete_4m_01 (30)` (1.64 m): allows 0.796..0.806 (top: the
  fence's top edge; bottom: its openwork blocks), she is held lower with her head just above the fence (screenshot). At its end the
  patch ran past the fence ("gaps", refused). Mansion wall `SM_walls_building_suburbs_02 (1)` with preferred 0: kneeling, knees at
  0.076 m (screenshots, side + front + inset). Apartment: refused beside a door (40 cm bump), with furniture in the way, and
  kneeling refused over the bed.

- L3 1.1.0 legs (2026-10-24, first save, StationFront glass facade - the glass shows what goes into the wall): before, the shins
  rose 9 deg behind the glass, the knee's bulge covered the tube rims; diagnostics showed the bones' local positions change after
  LateUpdate. With the drawn lengths: shin 1.4-1.9 deg from level, foot 0.4-0.6 cm and knee < 1 cm from target; negative control
  (`Debug.Disable = 2`): the shin rises out of the tube again (screenshots compared). Knee 1.3 radii outside the mouth and tubes
  lifted 0.35 knee radii: tube rims clean from the side and from below (calf no longer through the top rim; tried 2.0 / 2.8 knee
  radii and 1 cm clearance without the lift - the calf still showed). Measuring the limbs from a bake while she stands was tried
  and dropped (the other leg / torso / hair got into the cuts: arm 1.9 cm, knee 7.4 cm).

- L3 1.2.0 with RestraintHub (2026-10-24, Residence house wall): key 1 -> on the wall, selected in the hub's list; the spreader
  bar's key refused ("holds her legs"); `-` at the lowest height -> "The wall is not flat enough lower down", `+` -> "height: knees
  at 0.576 m"; F freed her. Details: `docs/mods/RestraintHub.md`.

- L3 1.3.0 tighter collar and arm cuffs (2026-10-24, first save, Residence house wall; the Yoke showed the 12 % neck estimate ~2 cm
  too big): before, the inset showed the collar as a wide plate around the neck base and the arm tubes about twice her arms' width.
  Neck 7 % of the spine + 0.4 cm: the collar sits around the neck, the choker just above it (front and three-quarter view). Arms
  9 % still about twice her arm, 7.5 %: the cuffs close around the upper arms, nothing through them. Lock accepted as before.
- L3 1.1.0 (2026-11-02, with RestraintHub 1.1.0 and the test missions - details in RestraintHub's `docs/RestraintHub.md`, L3 1.1.0): `CanPutOn` (placement with `check`: "no wall in front of her" on the street, "the wall is not flat there" at the intercom, ok at the house wall), put on by a mission, setting `height` 75.6 -> 60 -> 500 clamped to 128.6 cm (the wall's top), config KneeHeight unchanged, taken off by the mission. `+` / `-` go through the same `SetKneeHeight`.
- L3 2.0.0 (2026-11-07, RestraintHub 2.0.0 - details in its `docs/RestraintHub.md`, "L3 2.0.0 with the ported mods"): placed on a
  street fence ("place: ok on 'Fence_Concrete_4m_01_LOD0'", the empty restraint on the fence, screenshot); F -> locked to the wall
  there; V held -> free; kept through save + restart + load, used again after the load ("knees at 0.786 m ... wall allows
  0.786..0.806" from the saved data); picked up from the panel. Not re-tested in 2.0: + / - on a placed one, missions.

## Panel and lock code
The panel, the placed restraints and the + / - and F routing are RestraintHub's (2.0); UI helpers (`SfmKit.Game.GameUi`) and the
lock rules (`SfmKit.Pure.LockMachine`) are shared in `src/SfmKit`.

## Known limits / next
- Limb radii are estimates (7-15 % of bone lengths; the neck's ratio checked against the Yoke's neck ring on the same body). Measuring them from a bake while she stands failed (other leg, torso and hair
  in the cuts); measuring in the held pose (limbs apart) is the next try - `SfmKit.Pure.LimbGirth` is ready for it.
- Through glass walls the shins and feet behind the wall are visible (nothing hides them).
- The body inset (`BodyCamera`) draws only her, so the shins and feet inside the wall show there.
- Thin walls: the feet may come out on the other side (not checked yet).
- Curtains drawn flat count as a wall (the probe sees a flat drawn surface; in the Apartment she was put on the curtains).
- After struggling free she stands facing away from the wall; to lock again she has to turn round.
