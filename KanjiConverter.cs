using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace JapaneseIME
{
    /// <summary>
    /// Converts a kana reading to kanji with the Microsoft IME conversion engine
    /// (MSIME.Japan / IFELanguage).
    ///
    /// Pointer taps cannot use the Windows IME: Beat Saber commits the IME composition
    /// on every UI click, so a reading can never be accumulated for conversion. Calling
    /// the IME's conversion engine directly gives the same dictionary without a
    /// composition. The object is apartment-bound, so all calls run on one STA thread.
    /// </summary>
    internal static class KanjiConverter
    {
        private const int TimeoutMilliseconds = 300;

        private static readonly object Sync = new object();
        private static readonly System.Collections.Generic.Queue<Job> Jobs =
            new System.Collections.Generic.Queue<Job>();

        private static Thread _thread;
        private static bool _unavailable;

        private sealed class Job
        {
            internal string Reading;
            internal string Result;
            internal readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        internal static bool TryConvert(string reading, out string result)
        {
            result = null;

            if (string.IsNullOrEmpty(reading) || _unavailable)
            {
                return false;
            }

            EnsureThread();
            if (_unavailable)
            {
                return false;
            }

            Job job = new Job { Reading = reading };

            lock (Sync)
            {
                Jobs.Enqueue(job);
                Monitor.Pulse(Sync);
            }

            bool finished;
            try
            {
                finished = job.Done.Wait(TimeoutMilliseconds);
            }
            catch (Exception)
            {
                finished = false;
            }

            if (!finished)
            {
                Plugin.Log?.Warn("JapaneseIME: the kanji conversion engine did not answer in time.");
                return false;
            }

            result = job.Result;
            return !string.IsNullOrEmpty(result);
        }

        private static void EnsureThread()
        {
            if (_thread != null)
            {
                return;
            }

            lock (Sync)
            {
                if (_thread != null)
                {
                    return;
                }

                Thread thread = new Thread(Worker)
                {
                    IsBackground = true,
                    Name = "JapaneseIME.KanjiConverter",
                };

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                _thread = thread;
            }
        }

        private static void Worker()
        {
            IFELanguage language = null;

            while (true)
            {
                Job job;

                lock (Sync)
                {
                    while (Jobs.Count == 0)
                    {
                        Monitor.Wait(Sync);
                    }

                    job = Jobs.Dequeue();
                }

                try
                {
                    if (language == null)
                    {
                        Type type = Type.GetTypeFromProgID("MSIME.Japan", true);
                        language = (IFELanguage)Activator.CreateInstance(type);
                        int hr = language.Open();
                        if (hr < 0)
                        {
                            Marshal.ThrowExceptionForHR(hr);
                        }
                    }

                    string converted;
                    int result = language.GetConversion(job.Reading, 1, -1, out converted);
                    job.Result = result >= 0 ? converted : null;
                }
                catch (Exception ex)
                {
                    job.Result = null;
                    lock (Sync)
                    {
                        _unavailable = true;
                    }

                    Plugin.Log?.Warn(
                        "JapaneseIME: the Microsoft IME conversion engine is unavailable (" +
                        ex.GetType().Name + "). Pointer input stays kana only.");
                }
                finally
                {
                    job.Done.Set();
                }
            }
        }

        [ComImport, Guid("019F7152-E6DB-11D0-83C3-00C04FDDB82E"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFELanguage
        {
            [PreserveSig]
            int Open();

            [PreserveSig]
            int Close();

            [PreserveSig]
            int GetJMorphResult(
                uint request,
                uint conversionMode,
                int length,
                [MarshalAs(UnmanagedType.LPWStr)] string input,
                IntPtr info,
                out IntPtr result);

            [PreserveSig]
            int GetConversionModeCaps(out uint caps);

            [PreserveSig]
            int GetPhonetic(
                [MarshalAs(UnmanagedType.BStr)] string text,
                int start,
                int length,
                [MarshalAs(UnmanagedType.BStr)] out string result);

            [PreserveSig]
            int GetConversion(
                [MarshalAs(UnmanagedType.BStr)] string text,
                int start,
                int length,
                [MarshalAs(UnmanagedType.BStr)] out string result);
        }
    }
}
