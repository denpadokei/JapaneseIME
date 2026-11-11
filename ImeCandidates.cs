using System;
using System.Runtime.InteropServices;

namespace JapaneseIME
{
    /// <summary>A snapshot of the Windows IME candidate list.</summary>
    internal struct ImeCandidateList
    {
        internal string[] Candidates;
        internal int Selection;
        internal int PageStart;
        internal int PageSize;
    }

    /// <summary>
    /// Reads the Windows IME candidate list so it can be drawn inside the game
    /// instead of on the desktop, where the headset cannot show it.
    /// </summary>
    internal static class ImeCandidates
    {
        // CANDIDATELIST layout (imm.h):
        //   DWORD dwSize, dwStyle, dwCount, dwSelection, dwPageStart, dwPageSize;
        //   DWORD dwOffset[dwCount];
        private const int StyleOffset = 4;
        private const int CountOffset = 8;
        private const int SelectionOffset = 12;
        private const int PageStartOffset = 16;
        private const int PageSizeOffset = 20;
        private const int OffsetArrayOffset = 24;

        private const int ImeCandCode = 0x0002;
        private const int MaxCandidates = 4096;

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern int ImmGetCandidateListW(IntPtr hIMC, int dwIndex, IntPtr lpCandList, int dwBufLen);

        internal static bool TryRead(IntPtr hwnd, out ImeCandidateList list)
        {
            list = default(ImeCandidateList);

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
                if (size < OffsetArrayOffset)
                {
                    return false;
                }

                buffer = Marshal.AllocHGlobal(size);
                int written = ImmGetCandidateListW(context, 0, buffer, size);
                return written >= OffsetArrayOffset && TryParse(buffer, written, out list);
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

        /// <summary>
        /// Parses a CANDIDATELIST buffer. Kept separate from the imm32 calls so the
        /// layout handling can be verified without a live IME.
        /// </summary>
        internal static bool TryParse(IntPtr buffer, int size, out ImeCandidateList list)
        {
            list = default(ImeCandidateList);

            if (buffer == IntPtr.Zero || size < OffsetArrayOffset)
            {
                return false;
            }

            try
            {
                int style = Marshal.ReadInt32(buffer, StyleOffset);
                int count = Marshal.ReadInt32(buffer, CountOffset);
                if (count <= 0 || count > MaxCandidates ||
                    size < OffsetArrayOffset + (count * 4))
                {
                    return false;
                }

                string[] candidates = new string[count];

                if (style == ImeCandCode && count == 1)
                {
                    // In the rare IME_CAND_CODE form with a single entry, dwOffset[0]
                    // holds the DBCS character itself instead of a string offset.
                    int code = Marshal.ReadInt32(buffer, OffsetArrayOffset);
                    candidates[0] = ((char)(code & 0xFFFF)).ToString();
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        int offset = Marshal.ReadInt32(buffer, OffsetArrayOffset + (i * 4));
                        if (offset < OffsetArrayOffset || offset >= size)
                        {
                            candidates[i] = string.Empty;
                            continue;
                        }

                        IntPtr text = new IntPtr(buffer.ToInt64() + offset);
                        candidates[i] = Marshal.PtrToStringUni(text) ?? string.Empty;
                    }
                }

                int selection = Marshal.ReadInt32(buffer, SelectionOffset);
                int pageStart = Marshal.ReadInt32(buffer, PageStartOffset);
                int pageSize = Marshal.ReadInt32(buffer, PageSizeOffset);

                if (pageStart < 0 || pageStart >= count)
                {
                    pageStart = 0;
                }

                list.Candidates = candidates;
                list.Selection = selection >= 0 && selection < count ? selection : -1;
                list.PageStart = pageStart;
                list.PageSize = pageSize > 0 ? Math.Min(pageSize, count - pageStart) : 0;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
