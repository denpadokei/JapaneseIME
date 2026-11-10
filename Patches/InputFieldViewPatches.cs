using System;
using HarmonyLib;
using HMUI;

namespace JapaneseIME.Patches
{
    [HarmonyPatch(typeof(InputFieldView), nameof(InputFieldView.ActivateKeyboard))]
    internal static class InputFieldViewActivatePatch
    {
        private static void Postfix(InputFieldView __instance, UIKeyboard keyboard)
        {
            try
            {
                KeyboardLayout.Apply(keyboard);

                ImeManager manager = ImeManager.Instance;
                if (manager == null)
                {
                    return;
                }

                manager.Activate(new InputFieldTarget(__instance));
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME ActivateKeyboard patch failed: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(InputFieldView), nameof(InputFieldView.DeactivateKeyboard))]
    internal static class InputFieldViewDeactivatePatch
    {
        private static void Postfix(InputFieldView __instance)
        {
            try
            {
                ImeManager manager = ImeManager.Instance;
                if (manager == null)
                {
                    return;
                }

                manager.Deactivate(new InputFieldTarget(__instance));
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME DeactivateKeyboard patch failed: " + ex);
            }
        }
    }
}
