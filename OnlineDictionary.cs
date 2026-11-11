using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace JapaneseIME
{
    /// <summary>
    /// Fetches kana to kanji candidates from Google's CGI API for Japanese input
    /// (the "transliterate" endpoint). The reading is sent over HTTPS and the answer
    /// is cached, so repeated conversions and offline sessions fall back to the
    /// built-in JMdict table without delay.
    ///
    /// The request runs on a background thread: the game must not stall while a
    /// candidate list is being fetched.
    /// </summary>
    internal static class OnlineDictionary
    {
        private const string Endpoint = "https://www.google.com/transliterate?langpair=ja-Hira|ja&text=";
        private const int RequestTimeoutMilliseconds = 3000;
        private const int SuccessLifetimeSeconds = 1800;
        private const int FailureLifetimeSeconds = 60;
        private const int MaxCacheEntries = 512;

        private sealed class CacheEntry
        {
            internal string[] Candidates;
            internal DateTime CreatedUtc;
            internal bool Failed;
        }

        internal sealed class Result
        {
            internal string Reading;
            internal string[] Candidates;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Queue<string> Requests = new Queue<string>();
        private static readonly Queue<Result> Completed = new Queue<Result>();

        private static Thread _worker;

        /// <summary>
        /// Returns cached candidates for a reading. A false result only means "not
        /// known yet"; call <see cref="Request"/> to fetch it.
        /// </summary>
        internal static bool TryGet(string reading, out string[] candidates)
        {
            candidates = null;

            if (string.IsNullOrEmpty(reading))
            {
                return false;
            }

            lock (Sync)
            {
                CacheEntry entry;
                if (!Cache.TryGetValue(reading, out entry))
                {
                    return false;
                }

                TimeSpan lifetime = entry.Failed
                    ? TimeSpan.FromSeconds(FailureLifetimeSeconds)
                    : TimeSpan.FromSeconds(SuccessLifetimeSeconds);

                if (DateTime.UtcNow - entry.CreatedUtc > lifetime)
                {
                    Cache.Remove(reading);
                    return false;
                }

                if (entry.Failed || entry.Candidates == null)
                {
                    return false;
                }

                candidates = entry.Candidates;
                return candidates.Length > 0;
            }
        }

        /// <summary>Queues a background lookup. Duplicate requests are ignored.</summary>
        internal static void Request(string reading)
        {
            if (string.IsNullOrEmpty(reading))
            {
                return;
            }

            lock (Sync)
            {
                if (Pending.Contains(reading))
                {
                    return;
                }

                CacheEntry entry;
                if (Cache.TryGetValue(reading, out entry) &&
                    !entry.Failed &&
                    DateTime.UtcNow - entry.CreatedUtc <= TimeSpan.FromSeconds(SuccessLifetimeSeconds))
                {
                    return;
                }

                Pending.Add(reading);
                Requests.Enqueue(reading);

                if (_worker == null)
                {
                    _worker = new Thread(Worker)
                    {
                        IsBackground = true,
                        Name = "JapaneseIME.OnlineDictionary",
                    };

                    _worker.Start();
                }

                Monitor.Pulse(Sync);
            }
        }

        /// <summary>
        /// Takes one freshly fetched result, if any. The caller refreshes the visible
        /// candidate list so the online candidates appear without another key press.
        /// </summary>
        internal static bool TryTakeCompleted(out Result result)
        {
            lock (Sync)
            {
                if (Completed.Count == 0)
                {
                    result = null;
                    return false;
                }

                result = Completed.Dequeue();
                return true;
            }
        }

        private static void Worker()
        {
            while (true)
            {
                string reading;

                lock (Sync)
                {
                    while (Requests.Count == 0)
                    {
                        Monitor.Wait(Sync);
                    }

                    reading = Requests.Dequeue();
                }

                string[] candidates = null;
                bool failed;

                try
                {
                    candidates = Fetch(reading);
                    failed = candidates == null || candidates.Length == 0;
                }
                catch (Exception)
                {
                    failed = true;
                }

                lock (Sync)
                {
                    Pending.Remove(reading);

                    if (Cache.Count >= MaxCacheEntries)
                    {
                        Cache.Clear();
                    }

                    Cache[reading] = new CacheEntry
                    {
                        Candidates = candidates,
                        CreatedUtc = DateTime.UtcNow,
                        Failed = failed,
                    };

                    if (!failed)
                    {
                        Completed.Enqueue(new Result { Reading = reading, Candidates = candidates });
                    }
                }
            }
        }

        private static string[] Fetch(string reading)
        {
            string url = Endpoint + Uri.EscapeDataString(reading);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = RequestTimeoutMilliseconds;
            request.ReadWriteTimeout = RequestTimeoutMilliseconds;
            request.UserAgent = "Mozilla/5.0 JapaneseIME";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            string json;
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            {
                if (stream == null)
                {
                    return null;
                }

                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }
            }

            List<string> candidates;
            if (!TryParseResponse(json, out candidates))
            {
                return null;
            }

            // Kana-only entries (カンジ / にほんご / ﾆﾎﾝｺﾞ) are added by the plugin
            // itself, so only kanji forms are kept here.
            candidates.RemoveAll(value => !HasKanji(value));
            return candidates.Count > 0 ? candidates.ToArray() : null;
        }

        private static bool HasKanji(string value)
        {
            foreach (char c in value)
            {
                if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || c == 0x3005)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads [[ "reading", [ "candidate", ... ] ], ...] and flattens the
        /// candidates of the whole input.
        /// </summary>
        private static bool TryParseResponse(string json, out List<string> candidates)
        {
            candidates = new List<string>();

            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            int index = 0;

            if (!SkipWhitespace(json, ref index) || index >= json.Length || json[index] != '[')
            {
                return false;
            }

            index++;
            List<string> firstChoices = new List<string>();

            while (true)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length)
                {
                    break;
                }

                if (json[index] == ']')
                {
                    index++;
                    break;
                }

                if (json[index] != '[')
                {
                    return false;
                }

                index++;

                string reading;
                if (!ReadString(json, ref index, out reading))
                {
                    return false;
                }

                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ',')
                {
                    return false;
                }

                index++;
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != '[')
                {
                    return false;
                }

                index++;

                List<string> segment = new List<string>();
                while (true)
                {
                    SkipWhitespace(json, ref index);
                    if (index >= json.Length)
                    {
                        break;
                    }

                    if (json[index] == ']')
                    {
                        index++;
                        break;
                    }

                    string value;
                    if (!ReadString(json, ref index, out value))
                    {
                        return false;
                    }

                    if (value.Length > 0)
                    {
                        segment.Add(value);
                    }

                    SkipWhitespace(json, ref index);
                    if (index < json.Length && json[index] == ',')
                    {
                        index++;
                    }
                }

                if (segment.Count > 0)
                {
                    firstChoices.Add(segment[0]);

                    for (int i = 0; i < segment.Count; i++)
                    {
                        if (!candidates.Contains(segment[i]))
                        {
                            candidates.Add(segment[i]);
                        }
                    }
                }

                // Close of the segment's candidate array.
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ']')
                {
                    index++;
                }

                // Close of the segment itself.
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ']')
                {
                    index++;
                }

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    continue;
                }

                if (index < json.Length && json[index] == ']')
                {
                    index++;
                    break;
                }

                break;
            }

            // Multi-segment answers (a long reading split into words) also offer the
            // combined first choice.
            if (firstChoices.Count > 1)
            {
                StringBuilder combined = new StringBuilder();
                for (int i = 0; i < firstChoices.Count; i++)
                {
                    combined.Append(firstChoices[i]);
                }

                string value = combined.ToString();
                if (value.Length > 0 && !candidates.Contains(value))
                {
                    candidates.Insert(0, value);
                }
            }

            return candidates.Count > 0;
        }

        private static bool SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            return index < text.Length;
        }

        private static bool ReadString(string text, ref int index, out string value)
        {
            value = null;

            if (index >= text.Length || text[index] != '"')
            {
                return false;
            }

            index++;
            StringBuilder builder = new StringBuilder();

            while (index < text.Length)
            {
                char c = text[index++];

                if (c == '"')
                {
                    value = builder.ToString();
                    return true;
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length)
                {
                    return false;
                }

                char escape = text[index++];
                switch (escape)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escape);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (index + 4 > text.Length)
                        {
                            return false;
                        }

                        int code;
                        if (!int.TryParse(text.Substring(index, 4), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out code))
                        {
                            return false;
                        }

                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        return false;
                }
            }

            return false;
        }
    }
}
