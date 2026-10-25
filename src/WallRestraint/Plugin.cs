using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using RestraintHub;
using WallRestraint.Logic;

namespace WallRestraint
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Hub.Guid, "0.3.0")] // OnSave / OnLoad / OnNewGame
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "com.local.wallrestraint";
        public const string PluginName = "WallRestraint";
        public const string PluginVersion = "1.3.1";
        private const string ModelResource = "WallRestraint.assets.wallrestraint.wrm";

        internal static ManualLogSource Logger;
        /// <summary>The wall restraint's registration with RestraintHub (ring entry, selection, F and + / -).</summary>
        internal static Restraint Entry;
        public static ConfigEntry<float> Reach, MetalShade, KneeHeight, StepCm, ThighLean, ArchBulge, TubeClearanceCm, ClearanceCm, KneeOutside, TubeLift;
        public static ConfigEntry<int> DebugDisable;
        public static ConfigEntry<string> RingLabel;
        public static ConfigEntry<float> AttemptSeconds, SuccessChance;

        public override void Load()
        {
            Logger = Log;
            RingLabel = Config.Bind("General", "RingLabel", "Wall Restraint", "Name of the ring menu entry.");
            AttemptSeconds = Config.Bind("Unlock", "AttemptSeconds", 4.5f, new ConfigDescription(
                "Seconds of holding the interact key (F) per attempt to struggle free.", new AcceptableValueRange<float>(0.5f, 30f)));
            SuccessChance = Config.Bind("Unlock", "SuccessChance", 1f, new ConfigDescription(
                "Chance that an attempt frees her (1 = the first attempt always does; failures raise it like the game's handcuffs).",
                new AcceptableValueRange<float>(0.01f, 1f)));
            Reach = Config.Bind("Placement", "Reach", 1.2f, new ConfigDescription(
                "How far in front of her (m) a wall may be.", new AcceptableValueRange<float>(0.3f, 3f)));
            KneeHeight = Config.Bind("Pose", "KneeHeight", 2.4f, new ConfigDescription(
                "Preferred knee height above the floor, in her shin lengths (the shins go level into the wall; 2.4 = lifted high, about 0.2 = " +
                "kneeling). On a lower wall she is placed as high as it allows, down to kneeling. + / - while restrained raise / lower her " +
                "(as far as the wall allows) and store the result here.", new AcceptableValueRange<float>(0f, 3f)));
            StepCm = Config.Bind("Pose", "StepCm", 3f, new ConfigDescription(
                "How far one press of + / - moves her (cm).", new AcceptableValueRange<float>(0.5f, 20f)));
            ThighLean = Config.Bind("Pose", "ThighLean", 20f, new ConfigDescription(
                "How far the thighs lean out of the wall (degrees from vertical).", new AcceptableValueRange<float>(0f, 60f)));
            ArchBulge = Config.Bind("Pose", "ArchBulge", 0.04f, new ConfigDescription(
                "How far the back rest pushes her chest out (m).", new AcceptableValueRange<float>(0f, 0.15f)));
            DebugDisable = Config.Bind("Debug", "Disable", 0, "Bit mask for tests: 1 = do not pose the body; 2 = aim with the LateUpdate bone lengths (not the drawn ones).");
            KneeOutside = Config.Bind("Pose", "KneeOutside", 1.3f, new ConfigDescription(
                "How far her knee joint is outside the leg tube's mouth, in knee radii (more = the tube sits further down the shin, " +
                "clear of the bent knee).", new AcceptableValueRange<float>(0.5f, 4f)));
            TubeLift = Config.Bind("Pose", "TubeLift", 0.35f, new ConfigDescription(
                "Leg tubes above her shin bones, in knee radii (the calf is above the bone when the shins are level).",
                new AcceptableValueRange<float>(0f, 1.5f)));
            TubeClearanceCm = Config.Bind("Model", "TubeClearanceCm", 1f, new ConfigDescription(
                "Room between her leg and the leg tubes (cm). Raise it if the legs show through the tubes (they are sized from an " +
                "estimate of her knee, the calf can be thicker).", new AcceptableValueRange<float>(0f, 5f)));
            ClearanceCm = Config.Bind("Model", "ClearanceCm", 0.4f, new ConfigDescription(
                "Room between her neck / upper arms and the collar / arm cuffs (cm); both limbs are estimated from her bone lengths. " +
                "Raise it if they show through, lower it (down to -1) for a tighter fit. Applies the next time she is put on the wall.",
                new AcceptableValueRange<float>(-1f, 5f)));
            MetalShade = Config.Bind("Model", "MetalShade", 0.3f, new ConfigDescription(
                "Brightness of the metal parts (0 = black, 1 = white).", new AcceptableValueRange<float>(0f, 1f)));

            try
            {
                WallController.Model = LoadModel();
            }
            catch (Exception e)
            {
                Logger.LogError("Wall restraint model could not be loaded, the mod is disabled: " + e.Message);
                return;
            }
            ClassInjector.RegisterTypeInIl2Cpp<WallController>();
            AddComponent<WallController>();
            Entry = new Restraint
            {
                Id = "WallRestraint",
                RingValue = WallController.RingItemValue,
                Label = () => RingLabel.Value,
                ShortcutSaveKey = Patches.SaveKeyShortcuts,
                OnLoad = Patches.Load,
                OnNewGame = () => WallController.Instance?.OnNewGame(),
                Holds = BodyParts.Neck | BodyParts.Arms | BodyParts.Legs | BodyParts.Spot,
                IsWorn = () => WallController.Instance != null && WallController.Instance.Machine.Worn,
                // Held on the wall: no walking, running, crouching, peeing, pose skills or anything that needs her hands.
                Blocks = () => RestraintHub.Blocks.Moving | RestraintHub.Blocks.Running | RestraintHub.Blocks.Crouching | RestraintHub.Blocks.Pee
                               | RestraintHub.Blocks.HandActions | RestraintHub.Blocks.PoseSkills,
                OnChosen = () => { var c = WallController.Instance; if (c != null) c.OnRingChosen(); },
                Attempt = () => WallController.Instance == null ? 0f : WallController.Instance.Machine.Attempt,
                Controls = "+/- height",
            };
            Hub.Register(Entry);
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready (" + WallController.Model.Parts.Count + " parts)");
        }

        private static WallModel LoadModel()
        {
            using Stream s = typeof(Plugin).Assembly.GetManifestResourceStream(ModelResource)
                             ?? throw new FileNotFoundException("embedded resource " + ModelResource + " missing");
            return WallModel.Read(s);
        }
    }
}
