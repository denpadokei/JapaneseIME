using System;
using System.Runtime.InteropServices;

namespace JapaneseIME
{
    /// <summary>
    /// Reads the Windows IME candidate list so it can be drawn inside the game
    /// instead of on the desktop, where the headset cannot show it.
    /// </summary>
    internal static class ImeCandidates
    {
        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern int ImmGetCandidateListW(IntPtr hIMC, int dwIndex, IntPtr lpCandList, int dwBufLen);

        internal static bool TryRead(IntPtr hwnd, out string[] candidates, out int selection)
        {
            candidates = null;
            selection = -1;

            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr context = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;

            try
            {
                context = ImmGetContext(hwnd);
                if (context == IntPtr.Zero)
                {
                    return false;
                }

                int size = ImmGetCandidateListW(context, 0, IntPtr.Zero, 0);
                if (size <= 20)
                {
                    return false;
                }

                buffer = Marshal.AllocHGlobal(size);
                if (ImmGetCandidateListW(context, 0, buffer, size) <= 0)
                {
                    return false;
                }

                int count = Marshal.ReadInt32(buffer, 4);
                selection = Marshal.ReadInt32(buffer, 8);
                if (count <= 0 || count > 64)
                {
                    return false;
                }

                candidates = new string[count];
                for (int i = 0; i < count; i++)
                {
                    int offset = Marshal.ReadInt32(buffer, 20 + (i * 4));
                    IntPtr text = new IntPtr(buffer.ToInt64() + offset);
                    candidates[i] = Marshal.PtrToStringUni(text) ?? string.Empty;
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }

                if (context != IntPtr.Zero)
                {
                    ImmReleaseContext(hwnd, context);
                }
            }
        }
    }
}
