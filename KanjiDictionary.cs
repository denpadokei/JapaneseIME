using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace JapaneseIME
{
    /// <summary>
    /// Kana to kanji candidates taken from JMdict.
    ///
    /// The IME engine only answers with the best conversion for a sentence, so this
    /// fills in the other homophones (かみ → 神 / 紙 / 髪). The table is a gzip of
    /// "reading TAB candidate TAB candidate..." lines built by Tools/build_dictionary.py.
    /// </summary>
    internal static class KanjiDictionary
    {
        private const string ResourceSuffix = "kanji-dictionary.txt.gz";

        private static Dictionary<string, string[]> _entries;
        private static bool _loaded;

        internal static bool TryLookup(string reading, out string[] candidates)
        {
            candidates = null;

            if (string.IsNullOrEmpty(reading))
            {
                return false;
            }

            EnsureLoaded();
            return _entries != null && _entries.TryGetValue(reading, out candidates)
                && candidates != null && candidates.Length > 0;
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;

            try
            {
                Assembly assembly = typeof(KanjiDictionary).Assembly;
                string resourceName = null;

                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                    {
                        resourceName = name;
                        break;
                    }
                }

                if (resourceName == null)
                {
                    return;
                }

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        return;
                    }

                    using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
                    using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
                    {
                        Dictionary<string, string[]> entries =
                            new Dictionary<string, string[]>(24000, StringComparer.Ordinal);

                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string[] parts = line.Split('\t');
                            if (parts.Length < 2 || parts[0].Length == 0)
                            {
                                continue;
                            }

                            string[] values = new string[parts.Length - 1];
                            Array.Copy(parts, 1, values, 0, values.Length);
                            entries[parts[0]] = values;
                        }

                        _entries = entries;
                    }
                }
            }
            catch (Exception)
            {
                _entries = null;
            }
        }
    }
}
