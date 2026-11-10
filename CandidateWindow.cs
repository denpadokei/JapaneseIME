using System.Text;
using TMPro;
using UnityEngine;

namespace JapaneseIME
{
    /// <summary>
    /// Draws the Windows IME candidate list inside the game. The OS candidate window
    /// is a desktop window, so it is invisible in a headset; this puts the same list
    /// in world space in front of the player.
    /// </summary>
    internal static class CandidateWindow
    {
        private const float Distance = 1.4f;
        private const float VerticalOffset = -0.22f;
        private const float Scale = 0.0012f;

        private static readonly StringBuilder Builder = new StringBuilder(256);

        private static GameObject _root;
        private static TextMeshProUGUI _text;
        private static string _lastContent;

        internal static void Show(string[] candidates, int selection)
        {
            if (candidates == null || candidates.Length == 0)
            {
                Hide();
                return;
            }

            if (!EnsureCreated())
            {
                return;
            }

            string content = Build(candidates, selection);
            if (content == _lastContent && _root.activeSelf)
            {
                return;
            }

            _lastContent = content;
            _text.text = content;
            Place();
            _root.SetActive(true);
        }

        internal static void Hide()
        {
            _lastContent = null;

            if (_root != null && _root.activeSelf)
            {
                _root.SetActive(false);
            }
        }

        internal static void Destroy()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
                _text = null;
                _lastContent = null;
            }
        }

        private static string Build(string[] candidates, int selection)
        {
            Builder.Length = 0;

            for (int i = 0; i < candidates.Length; i++)
            {
                if (i > 0)
                {
                    Builder.Append("   ");
                }

                if (i == selection)
                {
                    Builder.Append('【').Append(candidates[i]).Append('】');
                }
                else
                {
                    Builder.Append(candidates[i]);
                }
            }

            return Builder.ToString();
        }

        private static bool EnsureCreated()
        {
            if (_root != null)
            {
                return true;
            }

            try
            {
                _root = new GameObject("JapaneseIME.Candidates");
                Object.DontDestroyOnLoad(_root);

                Canvas canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 100;

                RectTransform rect = _root.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(700f, 90f);
                _root.transform.localScale = Vector3.one * Scale;

                GameObject textObject = new GameObject("Text");
                textObject.transform.SetParent(_root.transform, false);

                _text = textObject.AddComponent<TextMeshProUGUI>();

                TMP_FontAsset font = Fonts.Japanese(_text.font);
                if (font != null)
                {
                    _text.font = font;
                }

                _text.fontSize = 42f;
                _text.alignment = TextAlignmentOptions.Center;
                _text.enableWordWrapping = false;
                _text.color = Color.white;
                _text.outlineWidth = 0.25f;
                _text.outlineColor = Color.black;

                RectTransform textRect = textObject.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;

                _root.SetActive(false);
                return true;
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME candidate overlay failed: " + ex.Message);
                _root = null;
                return false;
            }
        }

        private static void Place()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Transform view = camera.transform;
            _root.transform.position =
                view.position + (view.forward * Distance) + (view.up * VerticalOffset);
            _root.transform.rotation = view.rotation;
        }
    }
}
