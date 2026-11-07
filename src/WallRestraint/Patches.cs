using System;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using RestraintHub;

namespace WallRestraint
{
    /// <summary>The load (she starts free), through RestraintHub (OnLoad). The panel, placed restraints, input blocking and
    /// unlock gauge are RestraintHub's.</summary>
    internal static class Patches
    {
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
