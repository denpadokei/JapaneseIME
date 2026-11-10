using System;
using System.Collections.Generic;
using System.Text;
using HMUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JapaneseIME
{
    /// <summary>
    /// Adds a single extra key to the game's on-screen keyboard that switches the
    /// Windows IME conversion mode, and labels itself with the mode the IME is
    /// actually in (ひらがな / カタカナ / 半角カナ / 半角英数).
    ///
    /// Nothing existing is repurposed: an existing key is copied into its own parent
    /// and shifted sideways with anchoredPosition (the trick extra-button mods use),
    /// so it lands in the free strip right of OK without covering any real button.
    ///
    /// Typing stays on the keyboard's own keys; conversion is a space, exactly like a
    /// physical Japanese keyboard.
    /// </summary>
    internal static class KeyboardLayout
    {
        private const string KeyName = "JapaneseIME.ExtraKey";

        private const int HiraganaMode =
            ImeSwitcher.ImeCmodeNative | ImeSwitcher.ImeCmodeFullShape | ImeSwitcher.ImeCmodeRoman;

        private const int AlphanumericMode = 0;

        private static readonly int[] ConversionModes =
        {
            HiraganaMode,
            AlphanumericMode,
        };

        private static GameObject _key;
        private static ExtraKeyButton _button;
        private static TextMeshProUGUI _label;
        private static UIKeyboard _owner;

        /// <summary>Last conversion mode we set or read, used when the IME is unreachable.</summary>
        private static int _conversion = HiraganaMode;

        /// <summary>
        /// True once the player switched modes with this key. From then on
        /// <see cref="ImeManager"/> stops forcing hiragana, so the label keeps telling
        /// the truth about the IME.
        /// </summary>
        internal static bool HasUserChosenMode { get; private set; }

        internal static void Apply(UIKeyboard keyboard)
        {
            if (keyboard == null || !Plugin.Config.ShowImeModeKey)
            {
                return;
            }

            if (_owner == keyboard && _key != null)
            {
                return;
            }

            _key = null;
            _button = null;
            _label = null;
            _owner = null;

            try
            {
                Extend(keyboard);
                _owner = keyboard;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME keyboard layout failed: " + ex);
            }
        }

        private static void Extend(UIKeyboard keyboard)
        {
            UIKeyboardKey[] keys = keyboard.GetComponentsInChildren<UIKeyboardKey>(true);
            if (keys.Length < 8)
            {
                Plugin.Log?.Warn($"JapaneseIME keyboard has only {keys.Length} keys; layout not extended.");
                return;
            }

            UIKeyboardKey template = Find(keys, KeyCode.A) ?? FindLetter(keys);
            RectTransform templateRect = template != null ? template.transform as RectTransform : null;
            RectTransform row = templateRect != null ? templateRect.parent as RectTransform : null;
            if (row == null)
            {
                Plugin.Log?.Warn("JapaneseIME keyboard key has no RectTransform parent; layout not extended.");
                return;
            }

            float keyWidth = templateRect.rect.width;
            float keyHeight = templateRect.rect.height;
            if (keyWidth < 0.5f || keyHeight < 0.5f)
            {
                Plugin.Log?.Warn(
                    $"JapaneseIME template key rect is {keyWidth:F2}x{keyHeight:F2}; layout not extended.");
                return;
            }

            // Clone a real key, keep it in that key's own parent and move it with
            // anchoredPosition - the same trick BeatSaber-EnhancedTwitchChat uses for
            // its extra buttons. Sharing the parent makes the clone inherit anchors,
            // pivot, rotation and scale, so the offset cannot drift.
            RemoveExisting(keyboard.transform as RectTransform);

            UIKeyboardKey spaceKey = Find(keys, KeyCode.Space);
            UIKeyboardKey zeroKey = Find(keys, KeyCode.Alpha0) ?? Find(keys, KeyCode.Keypad0);

            UIKeyboardKey reference = zeroKey ?? spaceKey ?? template;
            RectTransform referenceRect = reference.transform as RectTransform;
            RectTransform parent = referenceRect != null ? referenceRect.parent as RectTransform : null;
            if (parent == null)
            {
                Plugin.Log?.Warn("JapaneseIME reference key has no RectTransform parent; layout not extended.");
                return;
            }

            float width = keyWidth * Mathf.Clamp(Plugin.Config.ExtraKeyWidthScale, 1.0f, 3.0f);
            float gap = keyWidth * 0.15f;

            // Sit just left of the numpad 0, which is the free strip right of the wide
            // OK button. Without a numpad the key goes to the right of the clone source.
            bool toLeft = zeroKey != null;
            float step = (referenceRect.rect.width * 0.5f) + gap + (width * 0.5f);
            float offsetX = toLeft ? -step : step;

            _key = AddKey(parent, reference, referenceRect, offsetX, width, keyHeight);
            if (_key == null)
            {
                return;
            }

            RectTransform keyRect = _key.transform as RectTransform;
            Plugin.Log?.Info(
                $"JapaneseIME keyboard layout: parent={parent.name} reference={reference.name} " +
                $"ref={referenceRect.rect.width:F2}x{referenceRect.rect.height:F2} " +
                $"key={keyWidth:F2}x{keyHeight:F2} offset={offsetX:F2} " +
                $"anchored={(keyRect != null ? keyRect.anchoredPosition.ToString() : "?")} " +
                $"size={(keyRect != null ? keyRect.sizeDelta.ToString() : "?")}");

            if (Plugin.Config.VerboseLogging)
            {
                DumpStructure(keyboard.transform as RectTransform);
            }

            Plugin.Log?.Info(
                $"JapaneseIME keyboard extended with one cycling key at {_key.transform.position}");
        }

        private static void RemoveExisting(RectTransform panel)
        {
            Transform[] all = panel.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == KeyName)
                {
                    UnityEngine.Object.Destroy(all[i].gameObject);
                }
            }
        }


        private static UIKeyboardKey FindLetter(UIKeyboardKey[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].keyCode >= KeyCode.A && keys[i].keyCode <= KeyCode.Z)
                {
                    return keys[i];
                }
            }

            return null;
        }

        private static UIKeyboardKey Find(UIKeyboardKey[] keys, KeyCode code)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].keyCode == code)
                {
                    return keys[i];
                }
            }

            return null;
        }


        /// <summary>
        /// Same recipe BeatSaberMarkupLanguage uses for its own on-screen keyboard
        /// (Components.KEYBOARD+KEY): clone a real key into the parent with
        /// worldPositionStays=false, drop the UIKeyboardKey immediately, then place it
        /// with anchoredPosition and sizeDelta.
        /// </summary>
        private static GameObject AddKey(
            RectTransform parent,
            UIKeyboardKey reference,
            RectTransform referenceRect,
            float offsetX,
            float width,
            float height)
        {
            Button baseButton = reference.GetComponent<Button>();
            GameObject clone;
            if (baseButton != null)
            {
                clone = UnityEngine.Object.Instantiate(baseButton, parent, false).gameObject;
            }
            else
            {
                clone = UnityEngine.Object.Instantiate(reference.gameObject, parent, false);
            }

            clone.name = KeyName;

            // Must not survive this frame: the game writes the key label from
            // UIKeyboardKey during Start/OnEnable, which would replace our text.
            UIKeyboardKey stray = clone.GetComponent<UIKeyboardKey>();
            if (stray != null)
            {
                UnityEngine.Object.DestroyImmediate(stray);
            }

            clone.SetActive(true);

            RectTransform rect = clone.transform as RectTransform;
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(width, height);
                rect.anchoredPosition =
                    referenceRect.anchoredPosition + new Vector2(offsetX, 0f);
            }

            _label = clone.GetComponentInChildren<TextMeshProUGUI>(true);

            TextMeshProUGUI[] labels = clone.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (_label != null)
            {
                TMP_FontAsset font = Fonts.Japanese(_label.font);
                if (font != null)
                {
                    _label.font = font;
                }

                for (int i = 0; i < labels.Length; i++)
                {
                    TextMeshProUGUI label = labels[i];
                    if (label == null)
                    {
                        continue;
                    }

                    if (font != null && label != _label)
                    {
                        label.font = font;
                    }

                    label.richText = true;
                    label.enableWordWrapping = false;
                    label.overflowMode = TextOverflowModes.Overflow;
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 1.0f;
                    label.fontSizeMax = 5.5f;
                    label.margin = Vector4.zero;
                    label.alignment = TextAlignmentOptions.Center;
                }

                if (Plugin.Config.VerboseLogging)
                {
                    Plugin.Log?.Info(
                        $"JapaneseIME label='{_label.name}' at {Path(_label.transform)} " +
                        $"labels={labels.Length} font={(_label.font != null ? _label.font.name : "null")} " +
                        $"text='{_label.text}'");
                }
            }
            else
            {
                Plugin.Log?.Warn("JapaneseIME extra key has no TextMeshProUGUI label.");
            }

            Button button = clone.GetComponentInChildren<Button>(true);
            if (button == null)
            {
                Plugin.Log?.Warn("JapaneseIME extra key has no Button; it will not respond.");
                return clone;
            }

            button.onClick = new Button.ButtonClickedEvent();

            _button = clone.GetComponent<ExtraKeyButton>();
            if (_button == null)
            {
                _button = clone.AddComponent<ExtraKeyButton>();
            }

            _button.Press = CycleConversion;
            _button.Refresh = RefreshLabel;
            _button.Label = _label;
            button.onClick.AddListener(_button.InvokePress);

            RefreshLabel();
            return clone;
        }

        private static string Path(Transform transform)
        {
            StringBuilder builder = new StringBuilder(transform.name);
            Transform current = transform.parent;
            for (int depth = 0; current != null && depth < 8; depth++)
            {
                builder.Insert(0, current.name + "/");
                current = current.parent;
            }

            return builder.ToString();
        }

        private static void RefreshLabel()
        {
            int current = CurrentConversion();
            int next = NextConversion(current);
            string text = Describe(current) +
                          "\n<size=45%><color=#5C6672>次:" + Describe(next) + "</color></size>";

            if (_button != null)
            {
                _button.ExpectedText = text;
            }

            if (_label != null)
            {
                _label.text = text;
            }
        }

        private static int IndexOfConversion(int conversion)
        {
            // Two modes only: anything IME-native is ひらがな, everything else 半角英数.
            bool native = (conversion & ImeSwitcher.ImeCmodeNative) != 0;
            return native ? 0 : 1;
        }

        private static int NextConversion(int conversion)
        {
            return ConversionModes[(IndexOfConversion(conversion) + 1) % ConversionModes.Length];
        }

        private static string Describe(int conversion)
        {
            return IndexOfConversion(conversion) == 0 ? "ひらがな" : "半角英数";
        }

        /// <summary>
        /// The conversion mode the IME is really in. Reading it back keeps the label
        /// honest even when the user changes mode with 全角/半角 or the IME's own UI.
        /// </summary>
        private static int CurrentConversion()
        {
            IntPtr target = ImeSwitcher.OwnWindow();
            bool open;
            int conversion;
            if (target != IntPtr.Zero && ImeSwitcher.TryGetConversionFor(target, out open, out conversion))
            {
                _conversion = open ? conversion : AlphanumericMode;
            }

            return _conversion;
        }

        private static void CycleConversion()
        {
            IntPtr target = ImeSwitcher.OwnWindow();
            int next = NextConversion(CurrentConversion());

            if (target == IntPtr.Zero || !ImeSwitcher.SetConversionFor(target, next))
            {
                Plugin.Log?.Warn("JapaneseIME conversion switch failed: no IME window.");
                return;
            }

            _conversion = next;
            HasUserChosenMode = true;
            RefreshLabel();
            Plugin.Log?.Info(
                $"JapaneseIME conversion mode -> {Describe(next)} (0x{next:X2}); " +
                ImeSwitcher.Describe());
        }

        private static void DumpStructure(RectTransform parent)
        {
            StringBuilder builder = new StringBuilder("buttons:");
            Button[] buttons = parent.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length && i < 80; i++)
            {
                RectTransform rect = buttons[i].transform as RectTransform;
                if (rect == null)
                {
                    continue;
                }

                Vector3 p = parent.InverseTransformPoint(rect.position);
                builder.Append(
                    $" {buttons[i].name}@({p.x:F1},{p.y:F1},{rect.rect.width:F1}x{rect.rect.height:F1})");
            }

            Plugin.Log?.Info(builder.ToString());
        }
    }

    /// <summary>
    /// One tap, one action. A hold gesture is impossible here: Beat Saber's pointer
    /// sends pointer down and pointer up in the same frame, so press duration is
    /// always zero.
    /// </summary>
    internal sealed class ExtraKeyButton : MonoBehaviour
    {
        internal Action Press;

        /// <summary>Re-reads the IME state so the label always shows the real mode.</summary>
        internal Action Refresh;

        /// <summary>The label to keep showing, and the text it must show.</summary>
        internal TextMeshProUGUI Label;
        internal string ExpectedText;

        private const float RefreshSeconds = 0.5f;

        private float _nextRefresh;

        /// <summary>
        /// The game writes key labels from the cloned UIKeyboardKey during the same
        /// frame we set ours, so restore it after everything else has run.
        /// </summary>
        private void LateUpdate()
        {
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshSeconds;

                try
                {
                    if (Refresh != null)
                    {
                        Refresh();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log?.Error("JapaneseIME label refresh failed: " + ex);
                }
            }

            if (Label != null && ExpectedText != null && Label.text != ExpectedText)
            {
                Label.text = ExpectedText;
            }
        }

        internal void InvokePress()
        {
            try
            {
                if (Press != null)
                {
                    Press();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME extra key failed: " + ex);
            }
        }
    }
}
