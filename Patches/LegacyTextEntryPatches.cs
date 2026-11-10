using System;
using HarmonyLib;

namespace JapaneseIME.Patches
{
    [HarmonyPatch(typeof(VRTextEntryController), "OnEnable")]
    internal static class LegacyTextEntryEnablePatch
    {
        private static void Postfix(VRTextEntryController __instance)
        {
            try
            {
                ImeManager manager = ImeManager.Instance;
                if (manager == null)
                {
                    return;
                }

                manager.Activate(new LegacyTextTarget(__instance));
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME legacy OnEnable patch failed: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(VRTextEntryController), "OnDisable")]
    internal static class LegacyTextEntryDisablePatch
    {
        private static void Postfix(VRTextEntryController __instance)
        {
            try
            {
                ImeManager manager = ImeManager.Instance;
                if (manager == null)
                {
                    return;
                }

                manager.Deactivate(new LegacyTextTarget(__instance));
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME legacy OnDisable patch failed: " + ex);
            }
        }
    }
}
