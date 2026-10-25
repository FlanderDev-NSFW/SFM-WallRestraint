using System;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using RestraintHub;

namespace WallRestraint
{
    /// <summary>Harmony hooks into the game: the load (she starts free). The ring entry, input blocking, unlock gauge and
    /// number shortcuts are RestraintHub's. The load comes through RestraintHub (OnLoad).</summary>
    internal static class Patches
    {
        public const string SaveKeyShortcuts = "WallRestraint.Shortcuts"; // written by the hub (kept from 1.1.0)

        // --- load: being restrained is not saved; a load always starts free (the wall may not be there) ----------------------

        internal static void Load(SaveSlot slot)
        {
            try
            {
                var c = WallController.Instance;
                if (c == null) return;
                c.OnSaveLoaded();
            }
            catch (Exception e) { WallController.Fail("load hook", e); }
        }
    }
}
