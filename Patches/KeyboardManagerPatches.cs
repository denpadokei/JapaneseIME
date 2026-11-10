using System;
using HarmonyLib;

namespace JapaneseIME.Patches
{
    [HarmonyPatch(typeof(UIKeyboardManager), "HandleKeyboardOkButton")]
    internal static class UIKeyboardManagerOkPatch
    {
        private static void Prefix()
        {
            try
            {
                ImeManager.Instance?.CommitCompositionBeforeExternalAction();
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME OK patch failed: " + ex);
            }
        }
    }
}
