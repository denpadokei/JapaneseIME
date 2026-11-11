using System;
using System.Text;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.FloatingScreen;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JapaneseIME
{
    /// <summary>
    /// Draws the Windows IME candidate list inside the game with BSML. The OS
    /// candidate window is a desktop window, so it is invisible in a headset; this
    /// puts the same list on a floating screen in front of the player.
    /// </summary>
    internal static class CandidateWindow
    {
        private const float Distance = 1.4f;
        private const float VerticalOffset = -0.20f;

        // FloatingScreen units (its transform scale is 0.02): 34 x n units is
        // 0.68 x 0.02n metres in world space.
        private const float ScreenWidth = 34f;
        private const float MinScreenHeight = 7f;
        private const float Padding = 4f;
        private const float LineHeight = 2.0f;
        private const int MaxPageSize = 10;

        private const string Layout =
            "<vertical id='root' bg='round-rect-panel' bg-color='#000000' bg-alpha='0.85' " +
            "pad='2' spacing='0' child-align='MiddleCenter' " +
            "horizontal-fit='Unconstrained' vertical-fit='Unconstrained'>" +
            "<text id='candidateText' align='Center' font-size='1.5' color='#FFFFFF' " +
            "outline-color='#000000' outline-width='0.15' " +
            "word-wrapping='false' overflow-mode='Overflow'/>" +
            "</vertical>";

        private static readonly StringBuilder Builder = new StringBuilder(512);

        private static FloatingScreen _screen;
        private static Canvas _canvas;
        private static TextMeshProUGUI _text;
        private static string _lastContent;
        private static float _nextCreateAttemptTime;

        private sealed class View
        {
            [UIComponent("candidateText")]
            internal TextMeshProUGUI Text = null;
        }

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

                float height = Mathf.Max(MinScreenHeight, Padding + (lines * LineHeight));
                _screen.ScreenSize = new Vector2(ScreenWidth, height);
            }

            if (!_screen.gameObject.activeSelf)
            {
                _screen.gameObject.SetActive(true);
            }

            FollowCamera();
        }

        internal static void Hide()
        {
            _lastContent = null;

            if (_screen != null && _screen.gameObject.activeSelf)
            {
                _screen.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Keeps the screen in front of the player. It is not parented to the camera
        /// because DontDestroyOnLoad only protects root objects.
        /// </summary>
        internal static void FollowCamera()
        {
            if (_screen == null || !_screen.gameObject.activeSelf)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Transform view = camera.transform;
            _screen.transform.SetPositionAndRotation(
                view.position + (view.rotation * new Vector3(0f, VerticalOffset, Distance)),
                view.rotation);

            if (_canvas != null && _canvas.worldCamera != camera)
            {
                _canvas.worldCamera = camera;
            }
        }

        internal static void Destroy()
        {
            if (_screen != null)
            {
                UnityEngine.Object.Destroy(_screen.gameObject);
            }

            _screen = null;
            _canvas = null;
            _text = null;
            _lastContent = null;
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
            if (_screen != null)
            {
                return true;
            }

            if (Time.unscaledTime < _nextCreateAttemptTime)
            {
                return false;
            }

            _nextCreateAttemptTime = Time.unscaledTime + 1f;

            BSMLParser parser;
            try
            {
                parser = BSMLParser.Instance;
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            try
            {
                Camera camera = Camera.main;
                Vector3 position = camera != null
                    ? camera.transform.position +
                      (camera.transform.rotation * new Vector3(0f, VerticalOffset, Distance))
                    : Vector3.zero;
                Quaternion rotation = camera != null ? camera.transform.rotation : Quaternion.identity;

                _screen = FloatingScreen.CreateFloatingScreen(
                    new Vector2(ScreenWidth, MinScreenHeight),
                    false,
                    position,
                    rotation);
                _screen.gameObject.name = "JapaneseIME.Candidates";
                UnityEngine.Object.DontDestroyOnLoad(_screen.gameObject);

                _canvas = _screen.GetComponent<Canvas>();
                if (_canvas != null)
                {
                    _canvas.sortingOrder = 2000;
                }

                View view = new View();
                parser.Parse(Layout, _screen.gameObject, view);

                _text = view.Text;
                if (_text == null)
                {
                    throw new InvalidOperationException("candidate text element was not created");
                }

                TMP_FontAsset font = Fonts.Japanese(_text.font);
                if (font != null)
                {
                    _text.font = font;
                }

                // The overlay must never steal the VR pointer from the game's UI.
                foreach (Graphic graphic in _screen.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = false;
                }

                _screen.gameObject.SetActive(false);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("JapaneseIME candidate overlay failed: " + ex);

                if (_screen != null)
                {
                    UnityEngine.Object.Destroy(_screen.gameObject);
                }

                _screen = null;
                _canvas = null;
                _text = null;
                return false;
            }
        }
    }
}
