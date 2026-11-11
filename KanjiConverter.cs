using System;
using System.Collections.Generic;
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
        private const uint FelangReqConv = 0x00010000;
        private const uint FelangCmodeNoInvisibleChar = 0x40000000;
        private const uint FelangClmnFixr = 0x00000010;

        /// <summary>
        /// Fixed prefix. Some entries (にほんご -> 日本語) are only in the engine's
        /// dictionary as part of a sentence, and the context also picks better
        /// homophones (てんき -> 天気 instead of 転記).
        /// </summary>
        private const string Context = "これは";

        private const int TimeoutMilliseconds = 300;

        private static readonly object Sync = new object();
        private static readonly Queue<Job> Jobs = new Queue<Job>();

        private static Thread _thread;
        private static bool _unavailable;

        private sealed class Job
        {
            internal string Reading;
            internal string[] Result;
            internal readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        /// <summary>
        /// Returns the kanji candidates for a reading, best first. The array can be
        /// empty when the engine knows neither form.
        /// </summary>
        internal static bool TryConvert(string reading, out string[] candidates)
        {
            candidates = null;

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

            candidates = job.Result;
            return candidates != null && candidates.Length > 0;
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

                    job.Result = BuildCandidates(language, job.Reading);
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

        private static string[] BuildCandidates(IFELanguage language, string reading)
        {
            List<string> candidates = new List<string>(3);

            string contextual = ConvertWithContext(language, reading);
            AddCandidate(candidates, contextual, reading);

            string direct;
            if (language.GetConversion(reading, 1, -1, out direct) >= 0)
            {
                AddCandidate(candidates, direct, reading);
            }

            return candidates.ToArray();
        }

        private static void AddCandidate(List<string> candidates, string value, string reading)
        {
            if (string.IsNullOrEmpty(value) || value == reading || candidates.Contains(value))
            {
                return;
            }

            candidates.Add(value);
        }

        private static string ConvertWithContext(IFELanguage language, string reading)
        {
            string input = Context + reading;
            IntPtr info = Marshal.AllocHGlobal(input.Length * 4);
            IntPtr pointer = IntPtr.Zero;

            try
            {
                for (int i = 0; i < input.Length; i++)
                {
                    // FIXR: keep the context as written and use it only as a hint.
                    Marshal.WriteInt32(info, i * 4, i < Context.Length ? (int)FelangClmnFixr : 0);
                }

                int hr = language.GetJMorphResult(
                    FelangReqConv,
                    FelangCmodeNoInvisibleChar,
                    input.Length,
                    input,
                    info,
                    out pointer);

                if (hr < 0 || pointer == IntPtr.Zero)
                {
                    return null;
                }

                MORRSLT result = Marshal.PtrToStructure<MORRSLT>(pointer);
                if (result.pwchOutput == IntPtr.Zero || result.cchOutput <= 0 || result.cchOutput > input.Length * 4)
                {
                    return null;
                }

                string output = Marshal.PtrToStringUni(result.pwchOutput, result.cchOutput);
                if (output == null || !output.StartsWith(Context, StringComparison.Ordinal))
                {
                    return null;
                }

                return output.Substring(Context.Length);
            }
            catch
            {
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(info);

                if (pointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MORRSLT
        {
            public uint dwSize;
            public IntPtr pwchOutput;
            public ushort cchOutput;
            public IntPtr pwchRead;
            public ushort cchRead;
            public IntPtr pchInputPos;
            public IntPtr pchOutputIdxWDD;
            public IntPtr pchReadIdxWDD;
            public IntPtr paMonoRubyPos;
            public IntPtr pWDD;
            public int cWDD;
            public IntPtr pPrivate;
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
