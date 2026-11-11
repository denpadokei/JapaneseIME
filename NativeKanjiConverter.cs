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

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void WarmUpDelegate();

        private static readonly StringBuilder Buffer = new StringBuilder(BufferLength);
        private static readonly object Sync = new object();
        private static IntPtr _module;
        private static ConvertDelegate _convert;
        private static ShutdownDelegate _shutdown;
        private static WarmUpDelegate _warmUp;
        private static bool _unavailable;

        /// <summary>
        /// Loads the DLL on first use. Returns false when the native backend cannot
        /// be used, which is not an error: the helper process takes over.
        /// </summary>
        internal static bool EnsureAvailable()
        {
            lock (Sync)
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

                    IntPtr warmUpAddress = GetProcAddress(module, "JimeWarmUp");
                    _warmUp = warmUpAddress == IntPtr.Zero
                        ? null
                        : (WarmUpDelegate)Marshal.GetDelegateForFunctionPointer(warmUpAddress, typeof(WarmUpDelegate));

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
        }

        /// <summary>
        /// Starts the native conversion thread (and with it the IME engine) without
        /// waiting for a conversion. Call this off the main thread at startup.
        /// </summary>
        internal static void WarmUp()
        {
            try
            {
                _warmUp?.Invoke();
            }
            catch (Exception ex)
            {
                Plugin.Log?.Warn("JapaneseIME: native warm-up failed (" + ex.GetType().Name + ").");
            }
        }

        internal static bool TryConvert(string reading, out string[] candidates)
        {
            candidates = null;

            lock (Sync)
            {
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
        }

        internal static void Shutdown()
        {
            lock (Sync)
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
                    _warmUp = null;
                }
            }
        }
    }
}
