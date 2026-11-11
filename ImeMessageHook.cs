using System;
using System.Runtime.InteropServices;
using System.Text;

namespace JapaneseIME
{
    /// <summary>
    /// Hooks the game window's message procedure and reads the IME candidate list
    /// when the IME announces a candidate change. Polling ImmGetCandidateList outside
    /// the IME notification returns nothing on current Windows IMEs; the candidate
    /// list is only valid while the WM_IME_NOTIFY message is being processed.
    /// </summary>
    internal static class ImeMessageHook
    {
        private const int GwlWndProc = -4;
        private const int WmImeSetContext = 0x0281;
        private const int WmImeNotify = 0x0282;
        private const int ImnChangeCandidate = 0x0003;
        private const int ImnCloseCandidate = 0x0004;
        private const int ImnOpenCandidate = 0x0005;
        private const int GcsCompStr = 0x0008;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr newLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongW(IntPtr hWnd, int nIndex, IntPtr newLong);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CallWindowProcW(
            IntPtr previousWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hWnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmCreateContext();

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern bool ImmDestroyContext(IntPtr hIMC);

        [DllImport("imm32.dll")]
        private static extern int ImmGetCandidateListW(
            IntPtr hIMC, int index, IntPtr candidateList, int bufferSize);

        [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
        private static extern int ImmGetCompositionStringW(
            IntPtr hIMC, int index, byte[] buffer, int bufferSize);

        // The delegate is a static field so the GC never collects the thunk that the
        // game window now calls.
        private static WndProcDelegate _wndProc;
        private static IntPtr _originalWndProc;
        private static IntPtr _window;
        private static IntPtr _context;
        private static IntPtr _createdContext;
        private static bool _installed;
        private static float _nextErrorLogTime;
        private static ImeCandidateList _pendingCandidates;
        private static bool _hasPendingCandidates;
        private static bool _pendingHide;

        internal static bool Installed => _installed;

        internal static bool TryTakePending(out ImeCandidateList list)
        {
            list = _pendingCandidates;
            bool has = _hasPendingCandidates;
            _hasPendingCandidates = false;
            return has;
        }

        internal static bool TryTakePendingHide()
        {
            bool hide = _pendingHide;
            _pendingHide = false;
            return hide;
        }

        private static void SetPending(ImeCandidateList list)
        {
            _pendingCandidates = list;
            _hasPendingCandidates = true;
            _pendingHide = false;
        }

        private static void SetPendingHide()
        {
            _hasPendingCandidates = false;
            _pendingHide = true;
        }

        internal static void EnsureInstalled()
        {
            IntPtr window = ImeSwitcher.OwnWindow();
            if (window == IntPtr.Zero)
            {
                return;
            }

            if (_installed && _window == window)
            {
                return;
            }

            Uninstall();

            try
            {
                _wndProc = WndProc;
                IntPtr hook = Marshal.GetFunctionPointerForDelegate(_wndProc);
                IntPtr previous = IntPtr.Size == 8
                    ? SetWindowLongPtrW(window, GwlWndProc, hook)
                    : SetWindowLongW(window, GwlWndProc, hook);

                if (previous == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    Plugin.Log?.Error(
                        $"JapaneseIME could not hook the game window (error {error}).");
                    return;
                }

                _originalWndProc = previous;
                _window = window;
                _installed = true;
                Plugin.Log?.Info(
                    $"JapaneseIME IME message hook installed on window 0x{window.ToInt64():X}.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME IME message hook failed: " + ex);
            }
        }

        internal static void Uninstall()
        {
            if (!_installed)
            {
                return;
            }

            try
            {
                if (_window != IntPtr.Zero && _originalWndProc != IntPtr.Zero && IsWindow(_window))
                {
                    if (IntPtr.Size == 8)
                    {
                        SetWindowLongPtrW(_window, GwlWndProc, _originalWndProc);
                    }
                    else
                    {
                        SetWindowLongW(_window, GwlWndProc, _originalWndProc);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("JapaneseIME IME message hook removal failed: " + ex.Message);
            }

            if (_createdContext != IntPtr.Zero)
            {
                try
                {
                    ImmDestroyContext(_createdContext);
                }
                catch
                {
                }

                _createdContext = IntPtr.Zero;
            }

            _installed = false;
            _window = IntPtr.Zero;
            _originalWndProc = IntPtr.Zero;
            _context = IntPtr.Zero;
            _wndProc = null;
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WmImeSetContext)
                {
                    if (wParam != IntPtr.Zero)
                    {
                        _context = ImmGetContext(hWnd);
                        if (_context == IntPtr.Zero)
                        {
                            IntPtr created = ImmCreateContext();
                            if (created != IntPtr.Zero)
                            {
                                ImmAssociateContext(hWnd, created);
                                _context = created;
                                _createdContext = created;
                            }
                        }
                    }
                    else
                    {
                        SetPendingHide();
                    }

                    if (Plugin.Config.ShowCandidatesInGame)
                    {
                        // Suppress the OS composition/candidate windows: the plugin
                        // draws the same information inside the game.
                        lParam = IntPtr.Zero;
                    }
                }
                else if (msg == WmImeNotify)
                {
                    int notify = wParam.ToInt32() & 0xFFFF;
                    if (Plugin.Config.VerboseLogging)
                    {
                        Plugin.Log?.Debug($"JapaneseIME WM_IME_NOTIFY notify={notify}.");
                    }

                    if (notify == ImnOpenCandidate || notify == ImnChangeCandidate)
                    {
                        ReadCandidates(hWnd);
                    }
                    else if (notify == ImnCloseCandidate)
                    {
                        SetPendingHide();
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
            }

            return CallWindowProcW(_originalWndProc, hWnd, msg, wParam, lParam);
        }

        private static void ReadCandidates(IntPtr hWnd)
        {
            if (!Plugin.Config.ShowCandidatesInGame)
            {
                return;
            }

            IntPtr context = _context != IntPtr.Zero ? _context : ImmGetContext(hWnd);
            if (context == IntPtr.Zero)
            {
                return;
            }

            IntPtr buffer = IntPtr.Zero;
            try
            {
                int size = ImmGetCandidateListW(context, 0, IntPtr.Zero, 0);
                if (size < 24)
                {
                    if (Plugin.Config.VerboseLogging)
                    {
                        Plugin.Log?.Debug("JapaneseIME IME candidate list: no candidates.");
                    }

                    SetPendingHide();
                    return;
                }

                buffer = Marshal.AllocHGlobal(size);
                int written = ImmGetCandidateListW(context, 0, buffer, size);
                if (written < 24)
                {
                    SetPendingHide();
                    return;
                }

                ImeCandidateList list;
                if (!ImeCandidates.TryParse(buffer, written, out list))
                {
                    SetPendingHide();
                    return;
                }

                int selection = FindSelection(context, list);
                list.Selection = selection;
                SetPending(list);

                if (Plugin.Config.VerboseLogging)
                {
                    Plugin.Log?.Debug(
                        $"JapaneseIME IME candidates: {list.Candidates.Length} entries, " +
                        $"page start {list.PageStart}, page size {list.PageSize}, " +
                        $"selected {list.Selection}.");
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }

                if (_context == IntPtr.Zero)
                {
                    ImmReleaseContext(hWnd, context);
                }
            }
        }

        /// <summary>
        /// The OS candidate list often reports selection 0. The composition string is
        /// the candidate the IME currently has selected, so match it to the list.
        /// </summary>
        private static int FindSelection(IntPtr context, ImeCandidateList list)
        {
            string composition = ReadComposition(context);
            if (!string.IsNullOrEmpty(composition))
            {
                for (int i = 0; i < list.Candidates.Length; i++)
                {
                    if (string.Equals(list.Candidates[i], composition, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            return list.Selection;
        }

        private static string ReadComposition(IntPtr context)
        {
            try
            {
                int bytes = ImmGetCompositionStringW(context, GcsCompStr, null, 0);
                if (bytes <= 0)
                {
                    return string.Empty;
                }

                byte[] buffer = new byte[bytes];
                int written = ImmGetCompositionStringW(context, GcsCompStr, buffer, bytes);
                return written <= 0 ? string.Empty : Encoding.Unicode.GetString(buffer, 0, written);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void LogError(Exception ex)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextErrorLogTime)
            {
                return;
            }

            _nextErrorLogTime = now + 5f;
            Plugin.Log?.Error("JapaneseIME IME message error: " + ex.Message);
        }
    }
}
