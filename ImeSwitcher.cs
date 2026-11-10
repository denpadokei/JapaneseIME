using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JapaneseIME
{
    /// <summary>
    /// Switches the Windows IME into Japanese (hiragana / katakana) input mode.
    ///
    /// Unity only asks Windows to accept IME composition (Input.imeCompositionMode);
    /// it never selects the Japanese keyboard mode. When the IME is left in
    /// alphanumeric mode every keystroke arrives as a half-width ASCII character,
    /// which is what makes "a" produce "a" instead of "あ".
    /// </summary>
    internal static class ImeSwitcher
    {
        private const int WmImeControl = 0x0283;
        private const int ImcSetConversionMode = 0x0002;
        private const int ImcSetOpenStatus = 0x0006;

        internal const int ImeCmodeRoman = 0x0010;
        internal const int ImeCmodeNative = 0x0001;
        internal const int ImeCmodeKatakana = 0x0002;
        internal const int ImeCmodeFullShape = 0x0008;

        private const byte VkDbeHiragana = 0xF2;
        private const byte VkDbeKatakana = 0xF1;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool open);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetOpenStatus(IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmSetConversionStatus(IntPtr hIMC, int conversion, int sentence);

        [DllImport("imm32.dll")]
        private static extern bool ImmGetConversionStatus(IntPtr hIMC, out int conversion, out int sentence);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        internal static int DesiredConversionMode(bool katakana)
        {
            int mode = ImeCmodeNative | ImeCmodeFullShape | ImeCmodeRoman;
            if (katakana)
            {
                mode |= ImeCmodeKatakana;
            }

            return mode;
        }

        internal static bool TryGetStatus(out bool open, out int conversion)
        {
            return TryGetStatusFor(GetOwnWindow(), out open, out conversion);
        }

        internal static bool TryGetStatusFor(IntPtr hwnd, out bool open, out int conversion)
        {
            open = false;
            conversion = 0;
            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr imc = ImmGetContext(hwnd);
                if (imc == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    open = ImmGetOpenStatus(imc);
                    int sentence;
                    return ImmGetConversionStatus(imc, out conversion, out sentence) && open;
                }
                finally
                {
                    ImmReleaseContext(hwnd, imc);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Current conversion mode of a window's IME context, regardless of whether the
        /// IME is open ("open" is reported separately).
        /// </summary>
        internal static bool TryGetConversionFor(IntPtr hwnd, out bool open, out int conversion)
        {
            open = false;
            conversion = 0;
            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr imc = ImmGetContext(hwnd);
                if (imc == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    open = ImmGetOpenStatus(imc);
                    int sentence;
                    return ImmGetConversionStatus(imc, out conversion, out sentence);
                }
                finally
                {
                    ImmReleaseContext(hwnd, imc);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Puts a window's IME into a specific conversion mode (as the language bar does).</summary>
        internal static bool SetConversionFor(IntPtr hwnd, int conversion)
        {
            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr imeWindow = ImmGetDefaultIMEWnd(hwnd);
                if (imeWindow != IntPtr.Zero)
                {
                    SendMessage(imeWindow, WmImeControl, (IntPtr)ImcSetOpenStatus, (IntPtr)1);
                    SendMessage(imeWindow, WmImeControl, (IntPtr)ImcSetConversionMode, (IntPtr)conversion);
                    return true;
                }

                IntPtr imc = ImmGetContext(hwnd);
                if (imc != IntPtr.Zero)
                {
                    try
                    {
                        ImmSetOpenStatus(imc, true);
                        ImmSetConversionStatus(imc, conversion, 0);
                        return true;
                    }
                    finally
                    {
                        ImmReleaseContext(hwnd, imc);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("JapaneseIME IME mode switch failed: " + ex.GetType().Name);
            }

            return false;
        }

        /// <summary>Returns a short description of the current IME state for logging.</summary>
        internal static string Describe()
        {
            bool open;
            int conversion;
            if (!TryGetStatus(out open, out conversion))
            {
                return "no ime context";
            }

            return $"open={open} conversion=0x{conversion:X2}";
        }

        /// <summary>
        /// Forces the IME into Japanese input mode. Returns true when at least one
        /// of the attempted mechanisms succeeded.
        /// </summary>
        internal static bool ForceJapanese(bool katakana, out string detail)
        {
            detail = "not attempted";

            try
            {
                IntPtr hwnd = GetOwnWindow();
                if (hwnd == IntPtr.Zero)
                {
                    detail = "window not found";
                    return false;
                }

                return ForceJapaneseFor(hwnd, katakana, out detail);
            }
            catch (Exception ex)
            {
                detail = "imm32 failed: " + ex.GetType().Name;
                return false;
            }
        }

        /// <summary>
        /// Forces the IME of a specific window into Japanese input mode. Used for the
        /// plugin's own IME host control, which owns its own IME context.
        /// </summary>
        internal static bool ForceJapaneseFor(IntPtr hwnd, bool katakana)
        {
            string detail;
            return ForceJapaneseFor(hwnd, katakana, out detail);
        }

        internal static bool ForceJapaneseFor(IntPtr hwnd, bool katakana, out string detail)
        {
            detail = "not attempted";
            int mode = DesiredConversionMode(katakana);

            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    detail = "window not found";
                    return false;
                }

                // Preferred: talk to the IME window directly. This works even when
                // ImmGetContext is unavailable for a given window.
                IntPtr imeWindow = ImmGetDefaultIMEWnd(hwnd);
                if (imeWindow != IntPtr.Zero)
                {
                    SendMessage(imeWindow, WmImeControl, (IntPtr)ImcSetOpenStatus, (IntPtr)1);
                    SendMessage(imeWindow, WmImeControl, (IntPtr)ImcSetConversionMode, (IntPtr)mode);
                    detail = $"ime window -> mode 0x{mode:X2}";
                    return true;
                }

                IntPtr imc = ImmGetContext(hwnd);
                if (imc != IntPtr.Zero)
                {
                    try
                    {
                        ImmSetOpenStatus(imc, true);
                        ImmSetConversionStatus(imc, mode, 0);
                        detail = $"ime context -> mode 0x{mode:X2}";
                        return true;
                    }
                    finally
                    {
                        ImmReleaseContext(hwnd, imc);
                    }
                }
            }
            catch (Exception ex)
            {
                detail = "imm32 failed: " + ex.GetType().Name;
            }

            if (hwnd == GetOwnWindow() && TrySendImeKey(katakana))
            {
                detail = katakana ? "sent VK_DBE_KATAKANA" : "sent VK_DBE_HIRAGANA";
                return true;
            }

            detail = "all ime switch mechanisms failed";
            return false;
        }

        /// <summary>
        /// Acts like the Japanese "全角/半角" key: turns the IME off when it is in a
        /// Japanese mode and back to hiragana when it is off.
        /// </summary>
        internal static bool ToggleJapanese(bool katakana)
        {
            return ToggleJapaneseFor(GetOwnWindow(), katakana);
        }

        internal static bool ToggleJapaneseFor(IntPtr hwnd, bool katakana)
        {
            bool open;
            int conversion;
            bool japanese = TryGetStatusFor(hwnd, out open, out conversion) &&
                            (conversion & ImeCmodeNative) != 0;

            if (japanese)
            {
                string detail;
                return ForceAlphanumericFor(hwnd, out detail);
            }

            string ignored;
            return ForceJapaneseFor(hwnd, katakana, out ignored);
        }

        internal static bool ForceAlphanumeric(out string detail)
        {
            return ForceAlphanumericFor(GetOwnWindow(), out detail);
        }

        internal static bool ForceAlphanumericFor(IntPtr hwnd, out string detail)
        {
            detail = "not attempted";

            try
            {
                if (hwnd == IntPtr.Zero)
                {
                    detail = "window not found";
                    return false;
                }

                IntPtr imeWindow = ImmGetDefaultIMEWnd(hwnd);
                if (imeWindow != IntPtr.Zero)
                {
                    SendMessage(imeWindow, WmImeControl, (IntPtr)ImcSetConversionMode, IntPtr.Zero);
                    SendMessage(imeWindow, WmImeControl, (IntPtr)ImcSetOpenStatus, IntPtr.Zero);
                    detail = "ime window -> alphanumeric";
                    return true;
                }

                IntPtr imc = ImmGetContext(hwnd);
                if (imc != IntPtr.Zero)
                {
                    try
                    {
                        ImmSetConversionStatus(imc, 0, 0);
                        ImmSetOpenStatus(imc, false);
                        detail = "ime context -> alphanumeric";
                        return true;
                    }
                    finally
                    {
                        ImmReleaseContext(hwnd, imc);
                    }
                }
            }
            catch (Exception ex)
            {
                detail = "imm32 failed: " + ex.GetType().Name;
            }

            detail = "all ime switch mechanisms failed";
            return false;
        }

        private static bool TrySendImeKey(bool katakana)
        {
            try
            {
                byte vk = katakana ? VkDbeKatakana : VkDbeHiragana;
                byte scan = (byte)MapVirtualKey(vk, 0);
                keybd_event(vk, scan, 0, UIntPtr.Zero);
                keybd_event(vk, scan, 2, UIntPtr.Zero);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static IntPtr OwnWindow()
        {
            return GetOwnWindow();
        }

        /// <summary>
        /// True when the game owns the foreground window. Synthetic keystrokes are
        /// delivered to whatever window is in front, so they must never be sent
        /// while another application has focus.
        /// </summary>
        internal static bool IsForeground()
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();
                return foreground != IntPtr.Zero &&
                       BelongsTo(foreground, (uint)Process.GetCurrentProcess().Id);
            }
            catch
            {
                return false;
            }
        }

        private static IntPtr GetOwnWindow()
        {
            uint self = (uint)Process.GetCurrentProcess().Id;

            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && BelongsTo(foreground, self))
            {
                return foreground;
            }

            IntPtr active = GetActiveWindow();
            if (active != IntPtr.Zero && BelongsTo(active, self))
            {
                return active;
            }

            return IntPtr.Zero;
        }

        private static bool BelongsTo(IntPtr hwnd, uint processId)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return pid == processId;
        }
    }
}
