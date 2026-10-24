using System;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.ObjectUI.InGame.TempUnlockPanel;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;
using UnityEngine;

namespace WallRestraint
{
    /// <summary>Harmony hooks into the game. Every hook fails open: on an exception the game behaves as without the mod.</summary>
    internal static class Patches
    {
        public const string LabelKey = "wallrestraint_ring_label";
        public const string SaveKeyShortcuts = "WallRestraint.Shortcuts";

        public static void Apply(Harmony harmony)
        {
            var me = typeof(Patches);
            void Hook(System.Reflection.MethodBase target, string postfix) => harmony.Patch(target, postfix: new HarmonyMethod(me, postfix));
            Hook(AccessTools.Method(typeof(InputResolver), nameof(InputResolver.Resolve)), nameof(ResolvePostfix));
            Hook(AccessTools.Method(typeof(Localizer), nameof(Localizer.Trans), new[] { typeof(string) }), nameof(TransPostfix));
            Hook(AccessTools.Method(typeof(TempUnlockPanelView), nameof(TempUnlockPanelView.OnLateUpdate)), nameof(UnlockPanelPostfix));
            Hook(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Load)), nameof(LoadPostfix));
            Hook(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Save)), nameof(SavePostfix));
        }

        // --- input: remember the interact key (struggling); while on the wall nothing may move her or start another pose ---

        private static void ResolvePostfix(InputData __result)
        {
            try
            {
                var c = WallController.Instance;
                if (c == null || __result == null) return;
                c.InteractHeld = __result.Interact;
                if (!c.Machine.Worn) return;
                __result.Move = Vector3.zero;
                __result.JustMove = __result.Dash = __result.Jump = __result.MoveForwardOneHand = false;
                __result.JustAutoRun = __result.JustAutoChase = __result.JustCrouch = __result.JustPee = false;
                __result.JustOnaniNormal = __result.JustRingHandcuff = __result.JustSlowSkill = __result.JustDrop = false;
            }
            catch (Exception e) { WallController.Fail("input hook", e); }
        }

        private static void TransPostfix(string key, ref string __result)
        {
            if (key == LabelKey) __result = Plugin.RingLabel.Value;
        }

        // --- the game's "Unlocking..." gauge shows our attempt too ----------------------------------------------------------

        private static void UnlockPanelPostfix(TempUnlockPanelView __instance)
        {
            try
            {
                var c = WallController.Instance;
                if (c == null || __instance == null) return;
                float attempt = c.Machine.Attempt;
                if (attempt <= 0f) return;
                if (__instance.contents != null && !__instance.contents.activeSelf) __instance.contents.SetActive(true);
                if (__instance.circleGauge != null) __instance.circleGauge.fillAmount = attempt;
            }
            catch (Exception e) { WallController.Fail("unlock gauge hook", e); }
        }

        // --- save / load: only the number shortcuts are saved; a load always starts free (the wall may not be there) --------
        // Written right after SaveManager.Save into the slot it just wrote, like SpreaderBar [observed 2026-10-11].

        private static void SavePostfix()
        {
            try
            {
                var c = WallController.Instance;
                if (c == null || SaveRapper.IsSystemData || SaveRapper.currentDataPath == SaveRapper.SystemDataPath) return;
                SaveRapper.Save(SaveKeyShortcuts, c.Ring.ShortcutMask);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] save: shortcuts=" + c.Ring.ShortcutMask + " path=" + SaveRapper.currentDataPath);
            }
            catch (Exception e) { WallController.Fail("save hook", e); }
        }

        private static void LoadPostfix()
        {
            try
            {
                var c = WallController.Instance;
                if (c == null || SaveRapper.IsSystemData || SaveRapper.currentDataPath == SaveRapper.SystemDataPath) return;
                int shortcuts = SaveRapper.Load(SaveKeyShortcuts, 0);
                c.OnSaveLoaded(shortcuts);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] load: shortcuts=" + shortcuts + " path=" + SaveRapper.currentDataPath);
            }
            catch (Exception e) { WallController.Fail("load hook", e); }
        }
    }
}
