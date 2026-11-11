using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JapaneseIME
{
    /// <summary>
    /// Loads <c>JapaneseIME.KanjiNative.dll</c> and calls the Microsoft IME engine
    /// through it. Mono cannot create COM objects, but it can call native code, so
    /// this is the preferred backend; <see cref="KanjiConverter"/> falls back to the
    /// helper process when the DLL is missing or fails.
    /// </summary>
    internal static class NativeKanjiConverter
    {
        private const string FileName = "JapaneseIME.KanjiNative.dll";
        private const int BufferLength = 512;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);

        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        private delegate int ConvertDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string reading,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer,
            int bufferLength);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void ShutdownDelegate();

        private static readonly StringBuilder Buffer = new StringBuilder(BufferLength);
        private static IntPtr _module;
        private static ConvertDelegate _convert;
        private static ShutdownDelegate _shutdown;
        private static bool _unavailable;

        /// <summary>
        /// Loads the DLL on first use. Returns false when the native backend cannot
        /// be used, which is not an error: the helper process takes over.
        /// </summary>
        internal static bool EnsureAvailable()
        {
            if (_convert != null)
            {
                return true;
            }

            if (_unavailable)
            {
                return false;
            }

            try
            {
                string pluginDirectory =
                    Path.GetDirectoryName(typeof(NativeKanjiConverter).Assembly.Location) ?? string.Empty;
                string gameDirectory = Path.GetFullPath(Path.Combine(pluginDirectory, ".."));

                // Beat Saber keeps native libraries in Libs\Natives (so BSIPA does
                // not try to load them as plugins); Plugins is accepted for
                // hand-copied installs.
                string path = null;
                string[] candidates =
                {
                    Path.Combine(gameDirectory, "Libs", "Natives", FileName),
                    Path.Combine(pluginDirectory, FileName),
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    if (File.Exists(candidates[i]))
                    {
                        path = candidates[i];
                        break;
                    }
                }

                if (path == null)
                {
                    _unavailable = true;
                    return false;
                }

                IntPtr module = LoadLibraryW(path);
                if (module == IntPtr.Zero)
                {
                    _unavailable = true;
                    Plugin.Log?.Warn(
                        "JapaneseIME: the native conversion DLL could not be loaded (" +
                        Marshal.GetLastWin32Error() + "), using the helper process instead.");
                    return false;
                }

                IntPtr address = GetProcAddress(module, "JimeConvert");
                if (address == IntPtr.Zero)
                {
                    FreeLibrary(module);
                    _unavailable = true;
                    return false;
                }

                IntPtr shutdownAddress = GetProcAddress(module, "JimeShutdown");
                _shutdown = shutdownAddress == IntPtr.Zero
                    ? null
                    : (ShutdownDelegate)Marshal.GetDelegateForFunctionPointer(shutdownAddress, typeof(ShutdownDelegate));
                _convert = (ConvertDelegate)Marshal.GetDelegateForFunctionPointer(address, typeof(ConvertDelegate));
                _module = module;

                Plugin.Log?.Info("JapaneseIME: native conversion DLL loaded (" + FileName + ").");
                return true;
            }
            catch (Exception ex)
            {
                _unavailable = true;
                Plugin.Log?.Warn(
                    "JapaneseIME: the native conversion DLL failed (" + ex.GetType().Name +
                    "), using the helper process instead.");
                return false;
            }
        }

        internal static bool TryConvert(string reading, out string[] candidates)
        {
            candidates = null;
            ConvertDelegate convert = _convert;
            if (convert == null)
            {
                return false;
            }

            string payload;
            try
            {
                Buffer.Length = 0;
                if (convert(reading, Buffer, Buffer.Capacity) <= 0)
                {
                    return false;
                }

                payload = Buffer.ToString();
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("JapaneseIME: native conversion failed (" + ex.GetType().Name + ").");
                return false;
            }

            if (payload.Length == 0)
            {
                return false;
            }

            candidates = payload.Split('\t');
            return true;
        }

        internal static void Shutdown()
        {
            try
            {
                _shutdown?.Invoke();
            }
            catch
            {
            }

            try
            {
                if (_module != IntPtr.Zero)
                {
                    FreeLibrary(_module);
                }
            }
            catch
            {
            }
            finally
            {
                _module = IntPtr.Zero;
                _convert = null;
                _shutdown = null;
            }
        }
    }
}
