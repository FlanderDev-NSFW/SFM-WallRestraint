using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using WallRestraint.Logic;

namespace WallRestraint
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "com.local.wallrestraint";
        public const string PluginName = "WallRestraint";
        public const string PluginVersion = "1.0.0";
        private const string ModelResource = "WallRestraint.assets.wallrestraint.wrm";

        internal static ManualLogSource Logger;
        public static ConfigEntry<float> Reach, MetalShade, KneeHeight, ThighLean, ArchBulge;
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
                "Knee height above the floor, in her shin lengths (the shins go level into the wall).", new AcceptableValueRange<float>(1.2f, 4f)));
            ThighLean = Config.Bind("Pose", "ThighLean", 20f, new ConfigDescription(
                "How far the thighs lean out of the wall (degrees from vertical).", new AcceptableValueRange<float>(0f, 60f)));
            ArchBulge = Config.Bind("Pose", "ArchBulge", 0.04f, new ConfigDescription(
                "How far the back rest pushes her chest out (m).", new AcceptableValueRange<float>(0f, 0.15f)));
            DebugDisable = Config.Bind("Debug", "Disable", 0, "Bit mask for tests: 1 = do not pose the body.");
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
            Patches.Apply(new HarmonyLib.Harmony(PluginGuid));
            ClassInjector.RegisterTypeInIl2Cpp<WallController>();
            AddComponent<WallController>();
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
