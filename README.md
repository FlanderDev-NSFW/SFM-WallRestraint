# WallRestraint

Wall-mounted restraints for SecretFlasherManaka (BepInEx 6 IL2CPP): she is held against a wall, lifted off the floor - neck in a
collar, back arched over a rest, upper arms in cuffs beside it, knees bent with the shins going through leather-lined tubes into the
wall. Single-player/offline only.

**Status 1.0.0:** usable. Face a flat wall (within `Reach`), pick **Wall Restraint** in the common ring (hold 1; 5th page when
SpreaderBar is installed too) or put it on a number key; hold **F** to struggle free (`Unlock.SuccessChance`, default: the first
attempt). Refusals are shown in the game's message bar ("Can't use the wall restraint here: ..."). Being restrained is not saved:
a load starts free. While restrained, walking, running, crouching, peeing, masturbating and the handcuff ring entry are ignored;
the camera can still be turned (it starts behind her, inside the wall - turn it half round).

## How it works
- **The model** comes from the `RestraintStuffs` collection of `TheWall.blend` (user's asset, may be shipped), exported by
  `tools/export_model.py` into `assets/wallrestraint.wrm` (embedded in the DLL):
  `blender -b --disable-autoexec TheWall.blend --python mods/WallRestraint/tools/export_model.py -- mods/WallRestraint/assets/wallrestraint.wrm`.
  Parts per kind / side / material in wall coordinates (x out of the wall, y sideways, z up from the floor) plus anchors (collar
  ring, back rest, arm rings, tube mouths). Format: `Logic/WallModel.cs`.
- **Her body** is measured from the bones at lock time (sizes differ per save: the first save's character is ~1.5 m, the "Amateur"
  save's much smaller). `Logic/WallPose.cs` lays out the pose from the knees up and scales each restraint to its limb
  (radii estimated from bone lengths for now).
- **The wall:** a ray finds the collider in front of her, then `DepthProbe` renders the patch the restraints cover with Unity's
  depth shader (her renderers switched off for it) and `Logic/WallSpot.cs` fits a plane: refused when anything is missing (invisible
  `PlayerBlocker` walls), bumpy (> 1.5 cm) or turned; the restraints snap to the drawn surface (colliders can be up to 18 cm off,
  see `docs/knowledge/api-notes/level-geometry.md`). A box check keeps furniture out of her way.
- **The pose** is held in `LateUpdate` (hips placed and turned, each bone aimed at its target joint); the restraints follow the
  drawn bones in `Camera.onPreCull`. Level walls cannot be cut, so a dark disc inside each tube stands in for the hole.

## Config (`BepInEx/config/com.local.wallrestraint.cfg`)
| Key | Default | Meaning |
|---|---|---|
| General.RingLabel | Wall Restraint | name of the ring entry |
| Unlock.AttemptSeconds | 4.5 | seconds of holding F per attempt |
| Unlock.SuccessChance | 1 | chance an attempt frees her (failures add 0.02, like the game's handcuffs) |
| Placement.Reach | 1.2 | how far in front of her a wall may be (m) |
| Pose.KneeHeight | 2.4 | knee height above the floor, in shin lengths |
| Pose.ThighLean | 20 | thighs lean out of the wall (degrees) |
| Pose.ArchBulge | 0.04 | how far the rest pushes her chest out (m) |
| Model.MetalShade | 0.3 | brightness of the metal parts |
| Debug.Disable | 0 | bit 1 = do not pose the body (negative control) |

## Diagnostics (bridge, session started with `--allow-invoke`)
`game call WallRestraint.WallController LockHere | LockForced | Release | Bones | View <out> <side> <up> | ViewOff`,
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

## Ring and lock code
The ring entry / number shortcuts (`SfmKit.Game.RingEntry`), UI helpers (`SfmKit.Game.GameUi`) and the lock rules
(`SfmKit.Pure.LockMachine`) are shared in `src/SfmKit` (moved out of SpreaderBar, which still has its own ring code - to migrate).

## Known limits / next
- Limb radii are estimates (12-15 % of bone lengths); measure them from the mesh like SpreaderBar's cuff fit.
- The body inset (`BodyCamera`) draws only her, so the shins and feet inside the wall show there.
- Thin walls: the feet may come out on the other side (not checked yet).
