using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace JapaneseIME.KanjiHost
{
    /// <summary>
    /// Converts kana readings to kanji with the Microsoft IME conversion engine.
    ///
    /// Unity's Mono cannot use COM, so the plugin runs this helper as a separate
    /// process and talks to it over stdin/stdout: one reading per line in, one
    /// tab-separated candidate line out.
    /// </summary>
    internal static class Program
    {
        private const uint FelangReqConv = 0x00010000;
        private const uint FelangCmodeNoInvisibleChar = 0x40000000;
        private const uint FelangClmnFixr = 0x00000010;
        private const string Context = "これは";

        [STAThread]
        private static int Main()
        {
            IFELanguage language = null;
            try
            {
                Type type = Type.GetTypeFromProgID("MSIME.Japan", true);
                language = (IFELanguage)Activator.CreateInstance(type);
                if (language.Open() < 0)
                {
                    return 2;
                }
            }
            catch
            {
                return 3;
            }

            try
            {
                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    string reading = Encoding.UTF8.GetString(Convert.FromBase64String(line));
                    List<string> candidates = BuildCandidates(language, reading);
                    string payload = string.Join("\t", candidates);
                    Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)));
                    Console.Out.Flush();
                }
            }
            finally
            {
                try
                {
                    language.Close();
                    Marshal.ReleaseComObject(language);
                }
                catch
                {
                }
            }

            return 0;
        }

        private static List<string> BuildCandidates(IFELanguage language, string reading)
        {
            List<string> candidates = new List<string>(3);

            if (string.IsNullOrEmpty(reading))
            {
                return candidates;
            }

            AddCandidate(candidates, ConvertWithContext(language, reading), reading);

            string direct;
            if (language.GetConversion(reading, 1, -1, out direct) >= 0)
            {
                AddCandidate(candidates, direct, reading);
            }

            return candidates;
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
                    Marshal.WriteInt32(info, i * 4, i < Context.Length ? (int)FelangClmnFixr : 0);
                }

                if (language.GetJMorphResult(
                        FelangReqConv,
                        FelangCmodeNoInvisibleChar,
                        input.Length,
                        input,
                        info,
                        out pointer) < 0 || pointer == IntPtr.Zero)
                {
                    return null;
                }

                MORRSLT result = Marshal.PtrToStructure<MORRSLT>(pointer);
                if (result.pwchOutput == IntPtr.Zero || result.cchOutput <= 0 ||
                    result.cchOutput > input.Length * 4)
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
