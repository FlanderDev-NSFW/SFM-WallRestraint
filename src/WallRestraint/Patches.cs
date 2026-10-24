using System;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;

namespace WallRestraint
{
    /// <summary>Harmony hooks into the game: the load (she starts free). The ring entry, input blocking, unlock gauge and
    /// number shortcuts are RestraintHub's. Every hook fails open.</summary>
    internal static class Patches
    {
        public const string SaveKeyShortcuts = "WallRestraint.Shortcuts"; // written by the hub (kept from 1.1.0)

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Load)), postfix: new HarmonyMethod(typeof(Patches), nameof(LoadPostfix)));
        }

        // --- load: being restrained is not saved; a load always starts free (the wall may not be there) ----------------------

        private static void LoadPostfix()
        {
            try
            {
                var c = WallController.Instance;
                if (c == null || SaveRapper.IsSystemData || SaveRapper.currentDataPath == SaveRapper.SystemDataPath) return;
                c.OnSaveLoaded();
            }
            catch (Exception e) { WallController.Fail("load hook", e); }
        }
    }
}
