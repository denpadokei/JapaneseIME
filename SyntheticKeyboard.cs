using System;
using System.Runtime.InteropServices;

namespace JapaneseIME
{
    /// <summary>
    /// Sends the keystroke a pressed on-screen key stands for, so the Windows IME
    /// handles it exactly like a physical keyboard. This is what lets pointer input
    /// reach the same romaji to kana/kanji conversion as typing on a keyboard.
    /// </summary>
    internal static class SyntheticKeyboard
    {
        private const byte VkShift = 0x10;
        private const byte VkBack = 0x08;
        private const uint KeyEventKeyUp = 0x0002;

        private const byte VkOem1 = 0xBA;
        private const byte VkOemPlus = 0xBB;
        private const byte VkOemComma = 0xBC;
        private const byte VkOemMinus = 0xBD;
        private const byte VkOemPeriod = 0xBE;
        private const byte VkOem2 = 0xBF;
        private const byte VkOem3 = 0xC0;
        private const byte VkOem4 = 0xDB;
        private const byte VkOem5 = 0xDD;
        private const byte VkOem6 = 0xDC;
        private const byte VkOem7 = 0xDE;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        /// <summary>
        /// Sends the key for a character the in-game keyboard produced. Returns false
        /// when the key cannot be represented or the game is not in the foreground.
        /// </summary>
        internal static bool TrySend(char character)
        {
            byte virtualKey;
            bool shift;
            if (!TryMap(character, out virtualKey, out shift))
            {
                return false;
            }

            if (!ImeSwitcher.IsForeground())
            {
                return false;
            }

            try
            {
                if (shift)
                {
                    KeyDown(VkShift);
                }

                KeyDown(virtualKey);
                KeyUp(virtualKey);

                if (shift)
                {
                    KeyUp(VkShift);
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME synthetic key failed: " + ex.Message);
                return false;
            }
        }

        internal static bool TrySendBackspace()
        {
            if (!ImeSwitcher.IsForeground())
            {
                return false;
            }

            try
            {
                KeyDown(VkBack);
                KeyUp(VkBack);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME synthetic backspace failed: " + ex.Message);
                return false;
            }
        }

        private static void KeyDown(byte virtualKey)
        {
            keybd_event(virtualKey, (byte)MapVirtualKey(virtualKey, 0), 0, UIntPtr.Zero);
        }

        private static void KeyUp(byte virtualKey)
        {
            keybd_event(virtualKey, (byte)MapVirtualKey(virtualKey, 0), KeyEventKeyUp, UIntPtr.Zero);
        }

        private static bool TryMap(char character, out byte virtualKey, out bool shift)
        {
            shift = false;

            if (character >= 'a' && character <= 'z')
            {
                virtualKey = (byte)(0x41 + (character - 'a'));
                return true;
            }

            if (character >= 'A' && character <= 'Z')
            {
                virtualKey = (byte)(0x41 + (character - 'A'));
                return true;
            }

            if (character >= '0' && character <= '9')
            {
                virtualKey = (byte)(0x30 + (character - '0'));
                return true;
            }

            switch (character)
            {
                case ' ': virtualKey = 0x20; return true;
                case '-': virtualKey = VkOemMinus; return true;
                case ',': virtualKey = VkOemComma; return true;
                case '.': virtualKey = VkOemPeriod; return true;
                case '/': virtualKey = VkOem2; return true;
                case ';': virtualKey = VkOem1; return true;
                case '[': virtualKey = VkOem4; return true;
                case ']': virtualKey = VkOem5; return true;
                case '\\': virtualKey = VkOem6; return true;
                case '=': virtualKey = VkOemPlus; return true;
                case '\'': virtualKey = VkOem7; return true;
                case '`': virtualKey = VkOem3; return true;
                case '!': virtualKey = 0x31; shift = true; return true;
                case '?': virtualKey = VkOem2; shift = true; return true;
                case ':': virtualKey = VkOem1; shift = true; return true;
                case '+': virtualKey = VkOemPlus; shift = true; return true;
                case '(': virtualKey = 0x39; shift = true; return true;
                case ')': virtualKey = 0x30; shift = true; return true;
                case '_': virtualKey = VkOemMinus; shift = true; return true;
                case '*': virtualKey = 0x38; shift = true; return true;
                default:
                    virtualKey = 0;
                    return false;
            }
        }
    }
}
