using System;
using System.Collections.Generic;
using System.Text;

namespace JapaneseIME
{
    /// <summary>
    /// Converts romaji into hiragana / katakana for the in-game keyboard.
    ///
    /// Keys pressed with the VR pointer are written straight into the field by the
    /// game, so the Windows IME never sees them and they stay half-width ASCII.
    /// This converter turns those taps into kana, which is what the IME would have
    /// produced.
    /// </summary>
    internal static class RomajiKana
    {
        private static readonly Dictionary<string, string> Tokens = BuildTokens();
        private static readonly HashSet<string> Prefixes = BuildPrefixes();

        /// <summary>True when the character participates in romaji input.</summary>
        internal static bool IsRomajiChar(char character)
        {
            char lower = char.ToLowerInvariant(character);
            if (lower >= 'a' && lower <= 'z')
            {
                return true;
            }

            switch (character)
            {
                case '-':
                case '.':
                case ',':
                case '?':
                case '!':
                case '/':
                case '~':
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Converts as much of <paramref name="input"/> as possible. Any trailing
        /// romaji that could still become a longer syllable is returned in
        /// <paramref name="remaining"/>. When <paramref name="flush"/> is set the
        /// converter gives up waiting and emits everything it has.
        /// </summary>
        internal static void Convert(
            string input,
            bool katakana,
            bool flush,
            out string output,
            out string remaining)
        {
            StringBuilder produced = new StringBuilder(input != null ? input.Length : 0);
            string pending = input ?? string.Empty;

            while (pending.Length > 0)
            {
                if (pending[0] == 'n')
                {
                    if (pending.Length == 1)
                    {
                        if (flush)
                        {
                            produced.Append('ん');
                            pending = string.Empty;
                        }

                        break;
                    }

                    char next = pending[1];
                    if (next == 'n')
                    {
                        produced.Append('ん');
                        pending = pending.Substring(2);
                        continue;
                    }

                    if (next == '\'')
                    {
                        produced.Append('ん');
                        pending = pending.Substring(2);
                        continue;
                    }

                    if (!IsVowel(next) && next != 'y')
                    {
                        // "nk" -> んk, "n." -> ん。
                        produced.Append('ん');
                        pending = pending.Substring(1);
                        continue;
                    }
                }
                else if (pending.Length >= 2 && pending[0] == pending[1] && IsDoublingConsonant(pending[0]))
                {
                    // "kka" -> っか
                    produced.Append('っ');
                    pending = pending.Substring(1);
                    continue;
                }

                string token = Match(pending);
                if (token != null)
                {
                    produced.Append(Tokens[token]);
                    pending = pending.Substring(token.Length);
                    continue;
                }

                if (Prefixes.Contains(pending))
                {
                    break;
                }

                // Not romaji we know: emit it untouched so nothing is swallowed.
                produced.Append(pending[0]);
                pending = pending.Substring(1);
            }

            if (flush && pending.Length > 0)
            {
                produced.Append(pending);
                pending = string.Empty;
            }

            output = katakana ? ToKatakana(produced.ToString()) : produced.ToString();
            remaining = pending;
        }

        private static string Match(string pending)
        {
            for (int length = Math.Min(4, pending.Length); length >= 1; length--)
            {
                string candidate = pending.Substring(0, length);
                if (Tokens.ContainsKey(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsVowel(char character)
        {
            return character == 'a' || character == 'i' || character == 'u' ||
                   character == 'e' || character == 'o';
        }

        private static bool IsDoublingConsonant(char character)
        {
            return character >= 'a' && character <= 'z' && !IsVowel(character) && character != 'n';
        }

        internal static string ToKatakana(string value)
        {
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= '\u3041' && c <= '\u3096')
                {
                    chars[i] = (char)(c + 0x60);
                }
                else if (c == '\u3094')
                {
                    chars[i] = '\u30F4';
                }
            }

            return new string(chars);
        }

        /// <summary>Converts katakana to hiragana so candidates can be offered in both.</summary>
        internal static string ToHiragana(string value)
        {
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= '\u30A1' && c <= '\u30F6')
                {
                    chars[i] = (char)(c - 0x60);
                }
            }

            return new string(chars);
        }

        private static Dictionary<string, string> BuildTokens()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);

            AddRow(map, string.Empty, "aiueo", "あいうえお");
            AddRow(map, "k", "aiueo", "かきくけこ");
            AddRow(map, "s", "aiueo", "さしすせそ");
            AddRow(map, "t", "aiueo", "たちつてと");
            AddRow(map, "n", "aiueo", "なにぬねの");
            AddRow(map, "h", "aiueo", "はひふへほ");
            AddRow(map, "m", "aiueo", "まみむめも");
            AddRow(map, "y", "auo", "やゆよ");
            AddRow(map, "r", "aiueo", "らりるれろ");
            AddRow(map, "w", "ao", "わを");
            AddRow(map, "g", "aiueo", "がぎぐげご");
            AddRow(map, "z", "aiueo", "ざじずぜぞ");
            AddRow(map, "d", "aiueo", "だぢづでど");
            AddRow(map, "b", "aiueo", "ばびぶべぼ");
            AddRow(map, "p", "aiueo", "ぱぴぷぺぽ");

            AddYoon(map, "ky", "き");
            AddYoon(map, "gy", "ぎ");
            AddYoon(map, "sh", "し");
            AddYoon(map, "sy", "し");
            AddYoon(map, "ch", "ち");
            AddYoon(map, "cy", "ち");
            AddYoon(map, "ty", "ち");
            AddYoon(map, "ny", "に");
            AddYoon(map, "hy", "ひ");
            AddYoon(map, "by", "び");
            AddYoon(map, "py", "ぴ");
            AddYoon(map, "my", "み");
            AddYoon(map, "ry", "り");
            AddYoon(map, "j", "じ");
            AddYoon(map, "jy", "じ");
            AddYoon(map, "zy", "じ");
            AddYoon(map, "dy", "ぢ");

            map["shi"] = "し";
            map["chi"] = "ち";
            map["tsu"] = "つ";
            map["fu"] = "ふ";
            map["ji"] = "じ";
            map["she"] = "しぇ";
            map["che"] = "ちぇ";
            map["je"] = "じぇ";
            map["thi"] = "てぃ";
            map["dhi"] = "でぃ";
            map["fa"] = "ふぁ";
            map["fi"] = "ふぃ";
            map["fe"] = "ふぇ";
            map["fo"] = "ふぉ";
            map["va"] = "ゔぁ";
            map["vi"] = "ゔぃ";
            map["vu"] = "ゔ";
            map["ve"] = "ゔぇ";
            map["vo"] = "ゔぉ";
            map["wi"] = "うぃ";
            map["we"] = "うぇ";
            map["ca"] = "か";
            map["cu"] = "く";
            map["co"] = "こ";
            map["n'"] = "ん";
            map["xn"] = "ん";

            AddSmall(map, "l", "ぁぃぅぇぉ");
            AddSmall(map, "x", "ぁぃぅぇぉ");
            AddSmallYoon(map, "ly", "ゃゅょ");
            AddSmallYoon(map, "xy", "ゃゅょ");
            map["ltu"] = "っ";
            map["xtu"] = "っ";
            map["ltsu"] = "っ";
            map["xtsu"] = "っ";
            map["lwa"] = "ゎ";
            map["xwa"] = "ゎ";

            map["-"] = "ー";
            map["."] = "。";
            map[","] = "、";
            map["?"] = "？";
            map["!"] = "！";
            map["/"] = "・";
            map["~"] = "〜";

            return map;
        }

        private static HashSet<string> BuildPrefixes()
        {
            HashSet<string> prefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (string token in Tokens.Keys)
            {
                for (int length = 1; length < token.Length; length++)
                {
                    prefixes.Add(token.Substring(0, length));
                }
            }

            return prefixes;
        }

        private static void AddRow(
            Dictionary<string, string> map,
            string consonant,
            string vowels,
            string kana)
        {
            for (int i = 0; i < vowels.Length && i < kana.Length; i++)
            {
                map[consonant + vowels[i]] = kana[i].ToString();
            }
        }

        private static void AddYoon(Dictionary<string, string> map, string consonant, string baseKana)
        {
            map[consonant + "a"] = baseKana + "ゃ";
            map[consonant + "u"] = baseKana + "ゅ";
            map[consonant + "o"] = baseKana + "ょ";
        }

        private static void AddSmall(Dictionary<string, string> map, string consonant, string kana)
        {
            AddRow(map, consonant, "aiueo", kana);
        }

        private static void AddSmallYoon(Dictionary<string, string> map, string consonant, string kana)
        {
            map[consonant + "a"] = kana[0].ToString();
            map[consonant + "u"] = kana[1].ToString();
            map[consonant + "o"] = kana[2].ToString();
        }
    }
}
