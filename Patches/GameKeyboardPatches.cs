using System;
using HarmonyLib;
using HMUI;

namespace JapaneseIME.Patches
{
    /// <summary>
    /// The on-screen keyboard writes characters straight into the field, so the
    /// Windows IME cannot convert them. When the IME is in a Japanese mode the
    /// character is converted from romaji to kana here instead.
    /// </summary>
    [HarmonyPatch(typeof(InputFieldView), "KeyboardKeyPressed")]
    internal static class InputFieldViewKeyPressedPatch
    {
        private static bool Prefix(InputFieldView __instance, char letter)
        {
            try
            {
                ImeManager manager = ImeManager.Instance;
                return manager == null || !manager.TryHandleGameKeyboardChar(__instance, letter);
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME keyboard key patch failed: " + ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(InputFieldView), "KeyboardDeletePressed")]
    internal static class InputFieldViewDeletePatch
    {
        private static bool Prefix(InputFieldView __instance)
        {
            try
            {
                ImeManager manager = ImeManager.Instance;
                return manager == null || !manager.TryHandleGameKeyboardBackspace(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME keyboard delete patch failed: " + ex);
                return true;
            }
        }
    }
}
