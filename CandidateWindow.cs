using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
        private const float VerticalOffset = -0.20f;
        private const float Scale = 0.0011f;
        private const float Width = 620f;
        private const float LineHeight = 34f;
        private const int MaxPageSize = 10;

        private static readonly StringBuilder Builder = new StringBuilder(512);

        private static GameObject _root;
        private static Canvas _canvas;
        private static TextMeshProUGUI _text;
        private static string _lastContent;

        internal static void Show(ImeCandidateList list)
        {
            if (list.Candidates == null || list.Candidates.Length == 0)
            {
                Hide();
                return;
            }

            if (!EnsureCreated())
            {
                return;
            }

            int lines;
            string content = Build(list, out lines);
            if (content != _lastContent)
            {
                _lastContent = content;
                _text.text = content;

                RectTransform rect = _root.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(Width, (lines * LineHeight) + 22f);
            }

            if (!_root.activeSelf)
            {
                _root.SetActive(true);
            }

            FollowCamera();
        }

        internal static void Hide()
        {
            _lastContent = null;

            if (_root != null && _root.activeSelf)
            {
                _root.SetActive(false);
            }
        }

        /// <summary>
        /// Keeps the overlay in front of the player. The window is not parented to the
        /// camera because DontDestroyOnLoad only protects root objects.
        /// </summary>
        internal static void FollowCamera()
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Transform view = camera.transform;
            _root.transform.SetPositionAndRotation(
                view.position + (view.rotation * new Vector3(0f, VerticalOffset, Distance)),
                view.rotation);

            if (_canvas != null && _canvas.worldCamera != camera)
            {
                _canvas.worldCamera = camera;
            }
        }

        internal static void Destroy()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
                _canvas = null;
                _text = null;
                _lastContent = null;
            }
        }

        private static string Build(ImeCandidateList list, out int lines)
        {
            int count = list.Candidates.Length;
            int start = list.PageStart;
            int pageSize = list.PageSize > 0 ? list.PageSize : MaxPageSize;
            if (pageSize > MaxPageSize)
            {
                pageSize = MaxPageSize;
            }

            int end = Mathf.Min(start + pageSize, count);
            int page = pageSize > 0 ? (start / pageSize) + 1 : 1;
            int pages = pageSize > 0 ? (count + pageSize - 1) / pageSize : 1;

            Builder.Length = 0;
            lines = 0;

            if (pages > 1)
            {
                Builder.Append("<color=#B8C7D9>候補 ")
                    .Append(page)
                    .Append('/')
                    .Append(pages)
                    .Append("</color>\n");
                lines++;
            }

            for (int i = start; i < end; i++)
            {
                if (lines > 0)
                {
                    Builder.Append('\n');
                }

                lines++;

                int number = i - start + 1;
                if (i == list.Selection)
                {
                    Builder.Append("<color=#FFD54F><b>【")
                        .Append(number)
                        .Append(' ')
                        .Append("<noparse>")
                        .Append(list.Candidates[i])
                        .Append("</noparse>")
                        .Append("】</b></color>");
                }
                else
                {
                    Builder.Append(number)
                        .Append(' ')
                        .Append("<noparse>")
                        .Append(list.Candidates[i])
                        .Append("</noparse>");
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

                _canvas = _root.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.WorldSpace;
                _canvas.sortingOrder = 2000;

                RectTransform rect = _root.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(Width, 60f);
                _root.transform.localScale = Vector3.one * Scale;

                Image background = _root.AddComponent<Image>();
                background.color = new Color(0.02f, 0.03f, 0.06f, 0.82f);
                background.raycastTarget = false;

                GameObject textObject = new GameObject("Text");
                textObject.transform.SetParent(_root.transform, false);

                _text = textObject.AddComponent<TextMeshProUGUI>();

                TMP_FontAsset font = Fonts.Japanese(_text.font);
                if (font != null)
                {
                    _text.font = font;
                }

                _text.fontSize = 28f;
                _text.alignment = TextAlignmentOptions.Center;
                _text.enableWordWrapping = false;
                _text.overflowMode = TextOverflowModes.Overflow;
                _text.color = Color.white;
                _text.outlineWidth = 0.2f;
                _text.outlineColor = Color.black;
                _text.raycastTarget = false;
                _text.margin = new Vector4(10f, 8f, 10f, 8f);

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
                _canvas = null;
                _text = null;
                return false;
            }
        }
    }
}
