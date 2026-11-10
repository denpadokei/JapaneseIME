using System;
using HMUI;
using UnityEngine;

namespace JapaneseIME
{
    internal interface IImeTarget
    {
        bool IsAlive { get; }
        UnityEngine.Object Owner { get; }
        Transform Transform { get; }
        string FieldName { get; }
        int MaxLength { get; }
        bool LooksLikePassword { get; }
        string ReadText();
        void SetPreview(string text);
        void NotifyChanged();
        void Commit(string text);
    }

    /// <summary>Adapter for HMUI.InputFieldView (the keyboard used by the new UI).</summary>
    internal sealed class InputFieldTarget : IImeTarget
    {
        private readonly InputFieldView _field;
        private readonly int _maxLength;
        private readonly bool _looksLikePassword;

        internal InputFieldTarget(InputFieldView field)
        {
            _field = field;
            _maxLength = ReflectionCache.GetField(field, "_textLengthLimit", 0);
            _looksLikePassword = NameChainContains(field.transform, "password");
        }

        public bool IsAlive => _field != null;
        public UnityEngine.Object Owner => _field;
        public Transform Transform => _field != null ? _field.transform : null;
        public string FieldName => _field != null ? _field.gameObject.name : "InputFieldView";
        public int MaxLength => _maxLength;
        public bool LooksLikePassword => _looksLikePassword;

        public string ReadText()
        {
            return _field != null ? (_field.text ?? string.Empty) : string.Empty;
        }

        public void SetPreview(string text)
        {
            if (_field != null)
            {
                _field.SetText(text ?? string.Empty);
            }
        }

        public void NotifyChanged()
        {
            if (_field == null)
            {
                return;
            }

            try
            {
                _field.onValueChanged?.Invoke(_field);
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME onValueChanged failed: " + ex);
            }
        }

        public void Commit(string text)
        {
            SetPreview(text);
            NotifyChanged();
        }

        private static bool NameChainContains(Transform transform, string needle)
        {
            for (int i = 0; transform != null && i < 8; i++, transform = transform.parent)
            {
                if (transform.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Diagnostic target used by SelfTestMode; never writes to game UI.</summary>
    internal sealed class FakeTextTarget : IImeTarget
    {
        private readonly UnityEngine.Object _owner;
        private string _display = string.Empty;

        internal FakeTextTarget(UnityEngine.Object owner)
        {
            _owner = owner;
        }

        public bool IsAlive => _owner != null;
        public UnityEngine.Object Owner => _owner;
        public Transform Transform => null;
        public string FieldName => "JapaneseIME-SelfTest";
        public int MaxLength => 128;
        public bool LooksLikePassword => false;

        public string ReadText()
        {
            return _display;
        }

        public void SetPreview(string text)
        {
            _display = text ?? string.Empty;
            Plugin.Log?.Info($"JapaneseIME self-test preview: '{_display}'");
        }

        public void NotifyChanged()
        {
            Plugin.Log?.Info($"JapaneseIME self-test changed: '{_display}'");
        }

        public void Commit(string text)
        {
            _display = text ?? string.Empty;
            Plugin.Log?.Info($"JapaneseIME self-test committed: '{_display}'");
        }
    }

    /// <summary>Adapter for the older VRTextEntryController + global UIKeyboard.</summary>
    internal sealed class LegacyTextTarget : IImeTarget
    {
        private readonly VRTextEntryController _controller;
        private readonly int _maxLength;
        private readonly bool _looksLikePassword;

        internal LegacyTextTarget(VRTextEntryController controller)
        {
            _controller = controller;
            _maxLength = ReflectionCache.GetField(controller, "_maxLength", 0);
            _looksLikePassword = NameChainContains(controller.transform, "password");
        }

        public bool IsAlive => _controller != null;
        public UnityEngine.Object Owner => _controller;
        public Transform Transform => _controller != null ? _controller.transform : null;
        public string FieldName => _controller != null ? _controller.gameObject.name : "VRTextEntryController";
        public int MaxLength => _maxLength;
        public bool LooksLikePassword => _looksLikePassword;

        public string ReadText()
        {
            return _controller != null ? (_controller.text ?? string.Empty) : string.Empty;
        }

        public void SetPreview(string text)
        {
            if (_controller != null)
            {
                try
                {
                    _controller.text = text ?? string.Empty;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.Error("JapaneseIME legacy preview failed: " + ex);
                }
            }
        }

        public void NotifyChanged()
        {
            // The text setter already raises textDidChangeEvent.
        }

        public void Commit(string text)
        {
            SetPreview(text);
        }

        private static bool NameChainContains(Transform transform, string needle)
        {
            for (int i = 0; transform != null && i < 8; i++, transform = transform.parent)
            {
                if (transform.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
