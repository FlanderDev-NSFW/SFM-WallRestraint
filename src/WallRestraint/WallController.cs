using System;
using System.Globalization;
using System.Text;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Scripts.Sound;
using SfmKit.Game;
using SfmKit.Pure;
using UnityEngine;
using WallRestraint.Logic;

namespace WallRestraint
{
    /// <summary>
    /// Owns the restraints in the scene. Bridge diagnostics (session with --allow-invoke):
    /// <c>game call WallRestraint.WallController LockHere</c> / <c>LockForced</c> / <c>Release</c> / <c>Bones</c> /
    /// <c>View out side up</c> / <c>ViewOff</c>; details in the BepInEx log.
    /// </summary>
    public sealed class WallController : MonoBehaviour
    {
        internal static WallModel Model;
        internal static WallController Instance;
        private WallRig _rig;
        private WallPlacer.Spot _spot;

        public WallController(IntPtr pointer) : base(pointer) { }

        public static string FootAtRender = ""; // diagnostics, see OnPreCull
        public static string MovedAfterLateUpdate = ""; // diagnostics: how far bones moved between our LateUpdate and the main camera's render
        private static readonly HumanBodyBones[] Watched = { HumanBodyBones.Hips, HumanBodyBones.Neck, HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerArm };
        private readonly Vector3[] _watchedAt = new Vector3[6];
        private static readonly HumanBodyBones[] Turned = { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm };
        private readonly Quaternion[] _localAt = new Quaternion[8], _parentLocalAt = new Quaternion[8];
        private Vector3 _rootAfter;
        private int _followedFrame = -1;

        [HideFromIl2Cpp]
        private void RecordAfterLateUpdate(PlayerController player)
        {
            var anim = player.Pca.Animator;
            for (int i = 0; i < Watched.Length; i++) _watchedAt[i] = anim.GetBoneTransform(Watched[i]).position;
            _rootAfter = player.transform.position;
            for (int i = 0; i < Turned.Length; i++)
            {
                var t = anim.GetBoneTransform(Turned[i]);
                _localAt[i] = t.localRotation;
                _parentLocalAt[i] = t.parent.localRotation;
            }
        }

        [HideFromIl2Cpp]
        private void CompareAtRender()
        {
            var player = PlayerController.Instance;
            var anim = player?.Pca?.Animator;
            if (anim == null) return;
            var sb = new StringBuilder("root " + F((player.transform.position - _rootAfter).magnitude));
            for (int i = 0; i < Watched.Length; i++)
            {
                Vector3 d = anim.GetBoneTransform(Watched[i]).position - _watchedAt[i];
                sb.Append(", ").Append(Watched[i]).Append(' ').Append(F(d.magnitude));
            }
            sb.Append("; local turn (own / parent '").Append(anim.GetBoneTransform(Turned[4]).parent.name).Append("'):");
            for (int i = 0; i < Turned.Length; i++)
            {
                var t = anim.GetBoneTransform(Turned[i]);
                sb.Append(' ').Append(Turned[i]).Append(' ').Append(F(Quaternion.Angle(t.localRotation, _localAt[i])))
                  .Append('/').Append(F(Quaternion.Angle(t.parent.localRotation, _parentLocalAt[i])));
            }
            MovedAfterLateUpdate = sb.ToString();
        }
        private Camera.CameraCallback _preCull;
        private bool _viewing;
        private System.Numerics.Vector3 _viewFrom;
        private GameObject _viewLight;

        // Ring entry, lock state, the player's interact key (set by Patches.ResolvePostfix).
        public const int RingItemValue = 9200; // SpreaderBar uses 9100
        internal RingEntry Ring;
        internal readonly LockMachine Machine = new LockMachine();
        internal bool InteractHeld;
        private readonly GameUi _ui = new GameUi();
        private IntPtr _gameData;
        private static int _failures;

        internal static void Fail(string where, Exception e)
        {
            if (_failures++ < 20) Plugin.Logger.LogWarning(where + " failed: " + e);
        }

        private void Awake()
        {
            Instance = this;
            Ring = new RingEntry(RingItemValue, Patches.LabelKey, OnRingChosen, Plugin.Logger, "wall restraint");
        }

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { Fail("Update", e); }
        }

        [HideFromIl2Cpp]
        private void Tick()
        {
            bool panel = _ui.PanelOpen();
            Ring.Tick(panel);
            TrackSaveIdentity();
            if (PlayerController.Instance == null || GameState.GameStateData == null) return;
            Machine.AttemptSeconds = Plugin.AttemptSeconds.Value; // live: config edits apply without a restart
            Machine.BaseChance = Plugin.SuccessChance.Value;
            // Interactions take precedence: no struggling inside an interaction area, in menus or with the ring open.
            bool unlockAllowed = InteractHeld && Machine.State == LockState.Locked && !panel && !GameUi.RingOpen() && !_ui.InInteractArea();
            switch (Machine.Tick(Time.deltaTime, unlockAllowed, UnityEngine.Random.value))
            {
                case LockMachine.Event.Locked:
                    GameUi.PlaySe(InGameSoundManager.SeType.AttachHandcuffs);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] lock: locked to the wall");
                    break;
                case LockMachine.Event.AttemptFailed:
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: failed, chance now " + Machine.Chance.ToString("0.00", CultureInfo.InvariantCulture));
                    break;
                case LockMachine.Event.Unlocking:
                    GameUi.PlaySe(InGameSoundManager.SeType.Unlock);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: success after " + Machine.Failures + " failures");
                    break;
                case LockMachine.Event.Unlocked:
                    Release();
                    break;
            }
        }

        /// <summary>The ring entry (click or number shortcut).</summary>
        [HideFromIl2Cpp]
        private void OnRingChosen()
        {
            if (Machine.Worn)
            {
                GameUi.Message("Already restrained to the wall - hold F to struggle free.");
                return;
            }
            string result = Lock(false);
            if (_locked) GameUi.PlaySe(InGameSoundManager.SeType.CommonEquip);
            else GameUi.Message("Can't use the wall restraint here: " + (result.StartsWith("refused: ") ? result.Substring(9).Split(" (")[0] : result));
        }

        /// <summary>A save was loaded: she starts free (the wall may not be there); the number shortcuts come back.</summary>
        [HideFromIl2Cpp]
        internal void OnSaveLoaded(int shortcuts)
        {
            if (Machine.Worn || _locked) Release();
            Ring.OnShortcutsLoaded(shortcuts);
            _gameData = GameState.GameStateData == null ? IntPtr.Zero : GameState.GameStateData.Pointer;
        }

        /// <summary>A new game or the title screen replaces the game data without a load: free her, forget the keys.</summary>
        [HideFromIl2Cpp]
        private void TrackSaveIdentity()
        {
            var data = GameState.GameStateData;
            IntPtr ptr = data == null ? IntPtr.Zero : data.Pointer;
            if (ptr == _gameData) return;
            _gameData = ptr;
            if (Machine.Worn || _locked) Release();
            Ring.Reset();
        }

        private void Start()
        {
            _preCull = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(OnPreCull));
            Camera.onPreCull = Camera.onPreCull == null
                ? _preCull
                : Il2CppSystem.Delegate.Combine(Camera.onPreCull, _preCull).Cast<Camera.CameraCallback>();
        }

        [HideFromIl2Cpp]
        private void OnPreCull(Camera cam)
        {
            try
            {
                if (_locked && _layout != null && Time.frameCount % 60 == 0)
                {
                    // Diagnostics: where the left foot is when each camera renders, against its target.
                    var anim = PlayerController.Instance?.Pca?.Animator;
                    var foot = anim == null ? null : anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                    if (foot != null)
                    {
                        float off = Vector3.Distance(foot.position, WallPlacer.U(_spot.Frame.Point(_layout.FootL)));
                        var knee = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                        var km = _spot.Frame.ToModel(WallPlacer.N(knee.position));
                        var fm = _spot.Frame.ToModel(WallPlacer.N(foot.position));
                        var shin = fm - km;
                        float angle = (float)(Math.Acos(Math.Max(-1, Math.Min(1, -shin.X / shin.Length()))) * 180 / Math.PI);
                        string line = cam.name + " (mask " + cam.cullingMask + "): left foot " + F(off) + " m from target, knee (model) "
                                      + F(km.X) + "/" + F(km.Y) + "/" + F(km.Z) + " tube " + F(_layout.LegAtL.X) + "/" + F(_layout.LegAtL.Y) + "/" + F(_layout.LegAtL.Z)
                                      + ", foot " + F(fm.X) + "/" + F(fm.Y) + "/" + F(fm.Z) + ", shin " + F(angle) + " deg from level-into-wall";
                        if (!FootAtRender.Contains(cam.name + " ")) FootAtRender += line + "; ";
                    }
                }
                if (_locked && _layout != null && _followedFrame != Time.frameCount)
                {
                    // The restraints follow the bones as they are drawn: between our LateUpdate and the render the knees
                    // still move ~8 cm (positions only, no rotations - see MovedAfterLateUpdate) [observed 2026-10-24].
                    _followedFrame = Time.frameCount;
                    var anim = PlayerController.Instance?.Pca?.Animator;
                    if (anim != null && (Plugin.DebugDisable.Value & 1) == 0)
                    {
                        WallPoser.Follow(anim, _spot.Frame, _layout, PoseParams.Default.CuffOnArm);
                        _rig.Place(_spot.Frame, _layout);
                    }
                }
                if (_locked && cam == Camera.main) CompareAtRender();
                if (!_viewing || cam != Camera.main || !_spot.Ok) return;
                // Diagnostics view: the game camera moved to a point in wall coordinates, aimed at the restraints'
                // middle (changing the transform in onPreCull still takes effect for this render).
                var f = _spot.Frame;
                Vector3 from = WallPlacer.U(f.Point(_viewFrom));
                var mid = _layout != null ? (_layout.Neck + _layout.KneeR + _layout.KneeL) / 3f : new System.Numerics.Vector3(0.1f, 0f, 1.2f);
                Vector3 at = WallPlacer.U(f.Point(mid));
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
                if (_viewLight != null) _viewLight.transform.position = from;
            }
            catch (Exception e) { Plugin.Logger.LogWarning("view: " + e.Message); _viewing = false; }
        }

        /// <summary>Diagnostics: look at the placed restraints from (out of the wall, sideways, up) in model metres, with a lamp.</summary>
        public static string View(float outward, float side, float up)
        {
            var c = Instance;
            if (c == null || !c._spot.Ok) return "nothing placed";
            c._viewFrom = new System.Numerics.Vector3(outward, side, up);
            c._viewing = true;
            if (c._viewLight == null)
            {
                c._viewLight = new GameObject("WallRestraint.ViewLight");
                DontDestroyOnLoad(c._viewLight);
                var l = c._viewLight.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 6f;
                l.intensity = 1.5f;
            }
            c._viewLight.SetActive(true);
            return "viewing from " + outward + ", " + side + ", " + up;
        }

        public static string ViewOff()
        {
            var c = Instance;
            if (c == null) return "no controller";
            c._viewing = false;
            if (c._viewLight != null) c._viewLight.SetActive(false);
            return "view off";
        }

        private void OnDestroy()
        {
            if (_preCull != null && Camera.onPreCull != null)
                Camera.onPreCull = Il2CppSystem.Delegate.Remove(Camera.onPreCull, _preCull)?.Cast<Camera.CameraCallback>();
            if (_viewLight != null) Destroy(_viewLight);
            _rig?.Destroy();
            if (Instance == this) Instance = null;
        }

        // The lock: her body measured, the pose laid out, the wall found; held every LateUpdate until released.
        private bool _locked;
        private PoseLayout _layout;
        private Vector3 _rootAt;
        private Quaternion _rootRot;
        private IntPtr _lockedPlayer;

        /// <summary>Lock her to the wall in front of her (diagnostics until the ring menu entry exists).</summary>
        public static string LockHere() => Lock(false);

        /// <summary>Diagnostics: lock even where <see cref="LockHere"/> would refuse the wall (the reason is still reported).</summary>
        public static string LockForced() => Lock(true);

        [HideFromIl2Cpp]
        private static string Lock(bool force)
        {
            try
            {
                var c = Instance;
                var player = PlayerController.Instance;
                if (c == null || player == null || player.Pca == null || player.Pca.Animator == null) return "not in game";
                if (c._locked) Release();
                var anim = player.Pca.Animator;
                if (!WallPoser.Measure(anim, out BodyDims body, out string problem)) return "refused: " + problem;
                var layout = WallPose.Layout(Model, body, Params());
                if (layout.Problem != null && !force) return "refused: " + layout.Problem;
                var spot = WallPlacer.Find(player, WallPose.Footprint(Model, layout), Plugin.Reach.Value, force);
                c._spot = spot;
                string text = Describe(spot) + "; body spine " + F(body.Spine) + " thigh " + F(body.Thigh) + " shin " + F(body.Shin)
                              + " upper arm " + F(body.UpperArm) + "; knees at " + F(layout.KneeR.Z) + " m, neck at " + F(layout.Neck.Z)
                              + " m; scales collar " + F(layout.CollarScale) + " rest " + F(layout.ArchScale) + " arm " + F(layout.ArmScale) + " leg " + F(layout.LegScale)
                              + (layout.Problem == null ? "" : "; layout problem: " + layout.Problem);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] lock: " + text + (spot.Map == null ? "" : Environment.NewLine + spot.Map));
                if (!spot.Ok) return text;
                if (c._rig == null || !c._rig.Built)
                {
                    c._rig = new WallRig(Model);
                    c._rig.Build(anim.gameObject.layer);
                }
                c._layout = layout;
                c._rig.Place(spot.Frame, layout);
                c._rig.SetVisible(true);
                // Her root stands on the floor under her hips, facing out of the wall (the camera and the game follow it).
                c._rootAt = WallPlacer.U(spot.Frame.Point(new System.Numerics.Vector3(layout.Hips.X + 0.1f, 0f, 0f)));
                c._rootRot = Quaternion.LookRotation(WallPlacer.U(spot.Frame.Normal), Vector3.up);
                c._lockedPlayer = player.Pointer;
                c._locked = true;
                c.Machine.RequestLock();
                return text;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("lock failed: " + e);
                return "failed: " + e.Message;
            }
        }

        public static string Release()
        {
            var c = Instance;
            if (c == null) return "no controller";
            c._locked = false;
            c.Machine.Reset();
            c._rig?.SetVisible(false);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] release: unlocked");
            return "released";
        }

        [HideFromIl2Cpp]
        private static PoseParams Params()
        {
            var p = PoseParams.Default;
            p.KneeHeightRatio = Plugin.KneeHeight.Value;
            p.ThighLean = Plugin.ThighLean.Value * Mathf.Deg2Rad;
            return p;
        }

        private void LateUpdate()
        {
            if (!_locked) return;
            try
            {
                var player = PlayerController.Instance;
                if (player == null || player.Pointer != _lockedPlayer || player.Pca == null || player.Pca.Animator == null)
                {
                    Release(); // map change or the player was replaced
                    return;
                }
                player.transform.SetPositionAndRotation(_rootAt, _rootRot);
                if ((Plugin.DebugDisable.Value & 1) == 0)
                {
                    WallPoser.Apply(player.Pca.Animator, _spot.Frame, _layout, Plugin.ArchBulge.Value);
                    RecordAfterLateUpdate(player);
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("holding the pose failed, released: " + e);
                Release();
            }
        }

        /// <summary>The player's bone positions relative to her feet and facing (x right, y up, z forward), for sizing the pose.</summary>
        public static string Bones()
        {
            var player = PlayerController.Instance;
            var anim = player == null ? null : player.Pca.Animator;
            if (anim == null) return "not in game";
            Transform pt = player.transform;
            var sb = new StringBuilder("bones (x right, y up, z forward; m):");
            foreach (HumanBodyBones b in new[] {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
                HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot })
            {
                var t = anim.GetBoneTransform(b);
                if (t == null) { sb.Append('\n').Append(b).Append(": none"); continue; }
                Vector3 p = pt.InverseTransformPoint(t.position);
                sb.Append('\n').Append(b).Append(": ").Append(F(p.x)).Append(", ").Append(F(p.y)).Append(", ").Append(F(p.z));
            }
            sb.Append("\nroot lossyScale ").Append(F(pt.lossyScale.x)).Append(", world height above feet: neck ")
              .Append(F(anim.GetBoneTransform(HumanBodyBones.Neck).position.y - pt.position.y)).Append(", head ")
              .Append(F(anim.GetBoneTransform(HumanBodyBones.Head).position.y - pt.position.y)).Append(", hips ")
              .Append(F(anim.GetBoneTransform(HumanBodyBones.Hips).position.y - pt.position.y)).Append(", knee ")
              .Append(F(anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position.y - pt.position.y));
            sb.Append("\nrenderer layers:");
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var r in pt.GetComponentsInChildren<Renderer>(true))
                if (r != null && seen.Add(r.gameObject.layer)) sb.Append(' ').Append(LayerMask.LayerToName(r.gameObject.layer)).Append(" (").Append(r.name).Append(')');
            Plugin.Logger.LogInfo(sb.ToString());
            return "logged";
        }

        [HideFromIl2Cpp]
        private static string Describe(WallPlacer.Spot spot)
        {
            var s = spot.Surface;
            string surface = "surface offset " + F(s.Offset * 100f) + " cm, bump " + F(s.Bump * 100f) + " cm, slope " + F(s.SlopeX) + " / " + F(s.SlopeY) + ", empty " + s.Empty;
            if (!spot.Ok || spot.Reason != null) return (spot.Ok ? "FORCED, would refuse: " : "refused: ") + spot.Reason + " (collider '" + spot.Collider + "'; " + surface + ")";
            var f = spot.Frame;
            return "ok on '" + spot.Collider + "': origin " + V(f.Origin) + " normal " + V(f.Normal) + " scale " + F(f.Scale) + "; " + surface;
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        private static string V(System.Numerics.Vector3 v) => "(" + F(v.X) + ", " + F(v.Y) + ", " + F(v.Z) + ")";
    }
}
