using TMPro;
using UnityEngine;

namespace JapaneseIME
{
    /// <summary>
    /// The game's own TMP fonts only carry Latin glyphs, so any Japanese we draw
    /// (key labels, IME candidates) renders as empty boxes unless we borrow a font
    /// the user installed. SiraLocalizer ships one and registers it as a TMP
    /// fallback, so look there first, then build one from an installed Windows font.
    /// </summary>
    internal static class Fonts
    {
        private static readonly char[] Probe = { '変', '換', 'あ', '漢' };

        private static readonly string[] SystemFontNames =
        {
            "Yu Gothic UI",
            "Meiryo UI",
            "Yu Gothic",
            "Meiryo",
            "MS Gothic",
            "MS UI Gothic",
            "Noto Sans CJK JP",
            "Source Han Sans JP",
            "BIZ UDGothic",
        };

        private static TMP_FontAsset _cached;
        private static TMP_FontAsset _generated;
        private static Font _generatedSource;
        private static bool _searched;

        internal static TMP_FontAsset Japanese(TMP_FontAsset preferred)
        {
            if (HasJapanese(preferred))
            {
                return preferred;
            }

            if (_cached != null && HasJapanese(_cached))
            {
                return _cached;
            }

            if (!_searched)
            {
                _searched = true;
                _cached = Search();
                if (_cached == null)
                {
                    Plugin.Log?.Warn(
                        "JapaneseIME found no Japanese font in the game. Make sure a Japanese " +
                        "Windows font (Yu Gothic / Meiryo / MS Gothic) is installed.");
                }
            }

            if (_cached != null)
            {
                return _cached;
            }

            // Build a font asset from an installed Windows font; dynamic TMP assets add
            // glyphs on demand, so this covers every character the IME produces.
            if (_generated == null)
            {
                _generated = CreateSystemFont();
            }

            return _generated != null ? _generated : preferred;
        }

        internal static bool HasJapanese(TMP_FontAsset font)
        {
            if (font == null)
            {
                return false;
            }

            for (int i = 0; i < Probe.Length; i++)
            {
                bool found;
                try
                {
                    found = font.HasCharacter(Probe[i]);
                }
                catch
                {
                    return false;
                }

                if (!found)
                {
                    // Dynamic assets report false until the glyph is in the atlas, so
                    // ask TMP to add it; that also tells us whether the source font has it.
                    return TryPopulate(font);
                }
            }

            return true;
        }

        /// <summary>
        /// Adds the probe characters to a dynamic font asset's atlas. Succeeds only when
        /// the underlying font actually carries them.
        /// </summary>
        private static bool TryPopulate(TMP_FontAsset font)
        {
            try
            {
                string missing;
                return font.TryAddCharacters(new string(Probe), out missing) &&
                       string.IsNullOrEmpty(missing);
            }
            catch
            {
                return false;
            }
        }

        private static TMP_FontAsset Search()
        {
            TMP_FontAsset best = null;
            int bestScore = 0;

            TMP_FontAsset fallbacksOwner = null;
            try
            {
                fallbacksOwner = TMP_Settings.defaultFontAsset;
                Consider(fallbacksOwner, ref best, ref bestScore);

                System.Collections.Generic.List<TMP_FontAsset> fallbacks =
                    TMP_Settings.fallbackFontAssets;
                if (fallbacks != null)
                {
                    for (int i = 0; i < fallbacks.Count; i++)
                    {
                        Consider(fallbacks[i], ref best, ref bestScore);
                    }
                }
            }
            catch
            {
                // TMP_Settings is unavailable very early in startup.
            }

            TMP_FontAsset[] all;
            try
            {
                all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            }
            catch
            {
                return best;
            }

            for (int i = 0; i < all.Length; i++)
            {
                Consider(all[i], ref best, ref bestScore);
            }

            if (best != null)
            {
                Plugin.Log?.Info("JapaneseIME Japanese font: " + best.name);
            }

            return bestScore >= 2 ? best : null;
        }

        /// <summary>
        /// Last resort: a dynamic TMP asset backed by an installed Windows font. Dynamic
        /// assets grow their atlas on demand, so every kanji the user converts to works.
        /// </summary>
        private static TMP_FontAsset CreateSystemFont()
        {
            for (int i = 0; i < SystemFontNames.Length; i++)
            {
                try
                {
                    Font font = Font.CreateDynamicFontFromOSFont(SystemFontNames[i], 48);
                    if (font == null)
                    {
                        continue;
                    }

                    TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);
                    if (asset == null)
                    {
                        Object.Destroy(font);
                        continue;
                    }

                    if (!TryPopulate(asset))
                    {
                        Object.Destroy(asset);
                        Object.Destroy(font);
                        continue;
                    }

                    asset.name = "JapaneseIME System Font";
                    _generatedSource = font;
                    Plugin.Log?.Info("JapaneseIME Japanese font: system '" + SystemFontNames[i] + "'");
                    return asset;
                }
                catch (System.Exception ex)
                {
                    Plugin.Log?.Warn(
                        $"JapaneseIME could not load system font '{SystemFontNames[i]}': {ex.Message}");
                }
            }

            Plugin.Log?.Warn("JapaneseIME no installed Windows font could render Japanese.");
            return null;
        }

        private static void Consider(TMP_FontAsset font, ref TMP_FontAsset best, ref int bestScore)
        {
            if (font == null)
            {
                return;
            }

            // Glyphs are mandatory: a name that merely looks Japanese (for example
            // "Mo...noto...n") must never win, or the label renders as a symbol.
            if (!HasJapanese(font))
            {
                return;
            }

            int score = IsJapaneseName(font.name) ? 3 : 2;
            if (score > bestScore)
            {
                best = font;
                bestScore = score;
            }
        }

        private static bool IsJapaneseName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            return lower.Contains("jp") || lower.Contains("jpn") || lower.Contains("japan") ||
                   lower.Contains("noto sans") || lower.Contains("notosans") ||
                   lower.Contains("noto serif") || lower.Contains("gothic") ||
                   lower.Contains("mincho") || lower.Contains("meiryo") || lower.Contains("yugoth") ||
                   lower.Contains("yumin") || lower.Contains("source han") || lower.Contains("sourcehan") ||
                   lower.Contains("kokoro") || lower.Contains("mplus");
        }
    }
}
