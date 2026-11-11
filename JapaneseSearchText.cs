using System;
using System.Text;

namespace JapaneseIME
{
    /// <summary>
    /// Normalizes song search text for Japanese queries.
    ///
    /// Beat Saber's LevelFilter cleans both the query and the song text with an
    /// ASCII-only regex, so every Japanese character is removed before matching.
    /// This keeps Japanese letters and folds the differences that should not
    /// matter for a search: full-width forms, letter case, and the kana script
    /// (hiragana is converted to katakana on both sides, so a hiragana query
    /// matches a katakana title and vice versa).
    /// </summary>
    internal static class JapaneseSearchText
    {
        internal static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            string source = text;
            try
            {
                source = text.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                // Invalid surrogate sequences are passed through unchanged.
            }

            StringBuilder builder = new StringBuilder(source.Length);

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];

                if (char.IsHighSurrogate(c) && i + 1 < source.Length &&
                    char.IsLowSurrogate(source[i + 1]))
                {
                    if (char.IsLetterOrDigit(source, i))
                    {
                        builder.Append(c);
                        builder.Append(source[i + 1]);
                    }

                    i++;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    builder.Append(' ');
                    continue;
                }

                if (!char.IsLetterOrDigit(c) && c != '-')
                {
                    continue;
                }

                builder.Append(ToKatakana(char.ToLowerInvariant(c)));
            }

            return builder.ToString();
        }

        internal static bool ContainsJapanese(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if ((c >= '\u3040' && c <= '\u30FF') ||
                    (c >= '\u3005' && c <= '\u3007') ||
                    (c >= '\u3400' && c <= '\u4DBF') ||
                    (c >= '\u4E00' && c <= '\u9FFF') ||
                    (c >= '\uF900' && c <= '\uFAFF'))
                {
                    return true;
                }

                if (char.IsHighSurrogate(c) && i + 1 < text.Length &&
                    char.IsLowSurrogate(text[i + 1]))
                {
                    int codePoint = char.ConvertToUtf32(c, text[i + 1]);
                    if (codePoint >= 0x20000 && codePoint <= 0x2FA1F)
                    {
                        return true;
                    }

                    i++;
                }
            }

            return false;
        }

        private static char ToKatakana(char c)
        {
            if (c >= '\u3041' && c <= '\u3096')
            {
                return (char)(c + 0x60);
            }

            if (c == '\u309D')
            {
                return '\u30FD';
            }

            if (c == '\u309E')
            {
                return '\u30FE';
            }

            return c;
        }
    }
}
