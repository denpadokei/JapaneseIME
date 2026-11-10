using System;
using System.Runtime.InteropServices;
using System.Text;

namespace JapaneseIME
{
    /// <summary>Direct imm32 access to the IME state of the game window.</summary>
    internal static class ImeNative
    {
        private const int GcsCompStr = 0x0008;

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
        private static extern int ImmGetCompositionStringW(IntPtr hIMC, int dwIndex, byte[] lpBuf, int dwBufLen);

        internal static bool TryReadComposition(out string value)
        {
            value = string.Empty;

            try
            {
                IntPtr hwnd = ImeSwitcher.OwnWindow();
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
                    int bytes = ImmGetCompositionStringW(imc, GcsCompStr, null, 0);
                    if (bytes <= 0)
                    {
                        return true;
                    }

                    byte[] buffer = new byte[bytes];
                    int written = ImmGetCompositionStringW(imc, GcsCompStr, buffer, bytes);
                    if (written <= 0)
                    {
                        return true;
                    }

                    value = Encoding.Unicode.GetString(buffer, 0, written);
                    return true;
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
    }
}
