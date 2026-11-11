using System;
using System.Collections.Generic;
using System.Text;
using HMUI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Screen = UnityEngine.Screen;

namespace JapaneseIME
{
    /// <summary>
    /// Owns the Windows IME while an in-game text field is active.
    /// Composition is mirrored into the field; committed text is pushed through the
    /// field's normal change notification so the game sees it as if it was typed on
    /// the in-game keyboard.
    /// </summary>
    internal sealed class ImeManager : MonoBehaviour
    {
        private const float ImeKeyGraceSeconds = 0.05f;
        private const float CursorUpdateInterval = 0.5f;
        private const float ImeEnableRetryInterval = 2f;

        private static readonly StringBuilder PendingText = new StringBuilder(64);

        internal static ImeManager Instance { get; private set; }

        /// <summary>True while the OS IME is composing text for the active field.</summary>
        internal bool IsComposing => _composition.Length > 0;

        private IImeTarget _target;
        private Keyboard _keyboard;
        private string _baseText = string.Empty;
        private string _composition = string.Empty;
        private string _keyboardRomaji = string.Empty;
        private string _keyboardReading = string.Empty;
        private string[] _conversionCandidates;
        private int _conversionIndex;
        private char _probeChar;
        private string _probeComposition = string.Empty;
        private int _probeTextFrame = -1;
        private float _probeDeadline;
        private bool _simulationUnavailable;
        private string _lastPreview;
        private bool _sawInputSystemComposition;
        private bool _sawInputSystemText;
        private int _lastTextInputFrame = -1;
        private int _lastCompositionFrame = -1;
        private int _compositionEndFrame = -1000;
        private float _compositionEndTime = float.NegativeInfinity;
        private int _pendingBackspaces;
        private bool _pendingPaste;
        private float _nextCursorUpdateTime;
        private float _nextImeEnableTime;
        private float _nextModeRetryTime;
        private bool _japaneseModeConfirmed;
        private bool _imeEnabledForTarget;
        private float _lastErrorTime = float.NegativeInfinity;

        internal static void EnsureCreated()
        {
            if (Instance != null)
            {
                return;
            }

            GameObject go = new GameObject("JapaneseIME");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<ImeManager>();
        }

        internal static void DestroyInstance()
        {
            if (Instance != null)
            {
                UnityEngine.Object.Destroy(Instance.gameObject);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                UnityEngine.Object.Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            DetachKeyboard();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        internal void Shutdown()
        {
            try
            {
                DeactivateInternal(commitComposition: true);
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        internal void Activate(IImeTarget target)
        {
            if (target == null || !target.IsAlive)
            {
                return;
            }

            PluginConfig config = Plugin.Config;
            if (!config.Enabled)
            {
                return;
            }

            if (target.LooksLikePassword && !config.EnableForPasswordFields)
            {
                if (config.VerboseLogging)
                {
                    Plugin.Log?.Debug("JapaneseIME: ignoring password field '" + target.FieldName + "'.");
                }

                return;
            }

            if (_target != null && _target.Owner == target.Owner)
            {
                return;
            }

            DeactivateInternal(commitComposition: true);

            _target = target;
            _baseText = target.ReadText() ?? string.Empty;
            _composition = string.Empty;
            _keyboardRomaji = string.Empty;
            _keyboardReading = string.Empty;
            _conversionCandidates = null;
            _conversionIndex = 0;
            _probeChar = '\0';
            _lastPreview = null;
            _lastTextInputFrame = -1;
            _lastCompositionFrame = -1;
            _compositionEndFrame = -1000;
            _compositionEndTime = float.NegativeInfinity;
            _sawInputSystemComposition = false;
            _sawInputSystemText = false;
            _pendingBackspaces = 0;
            _pendingPaste = false;
            PendingText.Length = 0;

            _japaneseModeConfirmed = false;
            _nextModeRetryTime = 0f;

            AttachKeyboard();
            ImeMessageHook.EnsureInstalled();
            EnableIme();
            ApplyJapaneseInputMode();
            RefreshDisplay(notify: false, force: true);
            UpdateCandidateWindow(force: true);

            Plugin.Log?.Info(
                $"JapaneseIME activated for '{target.FieldName}' (maxLength={target.MaxLength}).");
        }

        /// <summary>
        /// Handles a character typed on the game's on-screen (VR pointer) keyboard.
        /// Those keys write straight into the field, so the Windows IME never sees
        /// them; while the IME is in a Japanese mode the romaji is converted here.
        /// Returns true when the game must not also insert the raw character.
        /// </summary>
        internal bool TryHandleGameKeyboardChar(InputFieldView field, char letter)
        {
            if (!Plugin.Config.ConvertOnScreenKeyboard || letter == '\0')
            {
                return false;
            }

            if (_target == null || field == null || _target.Owner != field || !_target.IsAlive)
            {
                return false;
            }

            try
            {
                if (!IsJapaneseInputMode())
                {
                    CommitPointerInput(notify: false);
                    return false;
                }

                VerifySyntheticKeyboard();
                DetectCommittedComposition();

                int maxLength = _target.MaxLength;
                if (maxLength > 0 &&
                    _baseText.Length + _composition.Length + PointerText.Length +
                    _keyboardRomaji.Length >= maxLength)
                {
                    // Same as the game's own keyboard: a full field swallows the key.
                    return true;
                }

                if (Plugin.Config.SimulatePhysicalKeyboard && !_simulationUnavailable)
                {
                    return HandleWithSyntheticKey(letter, maxLength);
                }

                return HandleWithRomajiConversion(letter, maxLength);
            }
            catch (Exception ex)
            {
                LogError(ex);
                return false;
            }
        }

        /// <summary>
        /// Handles pointer-keyboard typing without the Windows IME: romaji becomes kana,
        /// and Space converts the accumulated reading to kanji with the Microsoft IME
        /// conversion engine. Further Space taps cycle kanji / katakana / hiragana.
        /// </summary>
        private bool HandleWithRomajiConversion(char letter, int maxLength)
        {
            if (_target == null)
            {
                return false;
            }

            if (letter == ' ' && Plugin.Config.ConvertOnScreenKeyboard && HasPointerInput)
            {
                return StartOrCycleConversion(maxLength);
            }

            // Typing the next character confirms the shown candidate.
            CommitConversion();

            if (RomajiKana.IsRomajiChar(letter))
            {
                _keyboardRomaji += char.ToLowerInvariant(letter);

                string produced;
                string remaining;
                RomajiKana.Convert(_keyboardRomaji, IsKatakanaMode(), false, out produced, out remaining);

                _keyboardRomaji = remaining;
                if (produced.Length > 0)
                {
                    _keyboardReading = AppendClamped(_keyboardReading, produced, maxLength);
                }
            }
            else
            {
                // Space, digits and other symbols end the pending syllable.
                FlushKeyboardRomaji(_target);
                CommitPointerInput(notify: false);
                _baseText = AppendClamped(_baseText, letter.ToString(), maxLength);
            }

            RefreshDisplay(notify: true, force: true);
            return true;
        }

        /// <summary>Text the pointer keyboard is composing, shown before the caret.</summary>
        private string PointerText =>
            _conversionCandidates != null
                ? _conversionCandidates[_conversionIndex]
                : _keyboardReading;

        private bool HasPointerInput =>
            _conversionCandidates != null || _keyboardReading.Length > 0 || _keyboardRomaji.Length > 0;

        /// <summary>
        /// First Space converts the reading; later presses cycle the candidates.
        /// </summary>
        private bool StartOrCycleConversion(int maxLength)
        {
            if (_conversionCandidates == null)
            {
                FlushKeyboardRomaji(_target);
                if (_keyboardReading.Length == 0)
                {
                    return false;
                }

                _conversionCandidates = BuildConversionCandidates(_keyboardReading);
                _conversionIndex = 0;
            }
            else if (_conversionCandidates.Length > 1)
            {
                _conversionIndex = (_conversionIndex + 1) % _conversionCandidates.Length;
            }

            ShowPointerCandidates();
            RefreshDisplay(notify: true, force: true);
            return true;
        }

        private static string[] BuildConversionCandidates(string reading)
        {
            List<string> candidates = new List<string>(10);

            // 1) Google's CGI API first. A cache miss starts a background request and
            //    the list is refreshed as soon as the answer arrives.
            if (Plugin.Config.UseOnlineDictionary)
            {
                string[] online;
                if (OnlineDictionary.TryGet(reading, out online) && online != null)
                {
                    int limit = online.Length < 4 ? online.Length : 4;
                    for (int i = 0; i < limit && candidates.Count < 6; i++)
                    {
                        AddCandidate(candidates, online[i], reading);
                    }
                }
                else
                {
                    OnlineDictionary.Request(reading);
                }
            }

            // 2) The built-in JMdict table fills in the homophones the API does not
            //    return and covers offline sessions (かみ → 神 / 紙 / 髪).
            string[] known;
            if (KanjiDictionary.TryLookup(reading, out known) && known != null)
            {
                int limit = known.Length < 6 ? known.Length : 6;
                for (int i = 0; i < limit && candidates.Count < 6; i++)
                {
                    AddCandidate(candidates, known[i], reading);
                }
            }

            AddCandidate(candidates, RomajiKana.ToKatakana(reading), string.Empty);
            AddCandidate(candidates, RomajiKana.ToHiragana(reading), string.Empty);

            if (candidates.Count == 0)
            {
                candidates.Add(reading);
            }

            return candidates.ToArray();
        }

        private static void AddCandidate(List<string> candidates, string value, string reading)
        {
            if (string.IsNullOrEmpty(value) || value == reading || candidates.Contains(value))
            {
                return;
            }

            candidates.Add(value);
        }

        /// <summary>
        /// The online dictionary or the IME engine answered while a reading is still
        /// being converted: rebuild the list so its candidates can be picked without
        /// another key press.
        /// </summary>
        private void RefreshConversionWithOnline(string reading)
        {
            if (string.IsNullOrEmpty(reading) || _conversionCandidates == null ||
                string.IsNullOrEmpty(_keyboardReading) ||
                !string.Equals(reading, _keyboardReading, StringComparison.Ordinal))
            {
                return;
            }

            string[] refreshed = BuildConversionCandidates(_keyboardReading);
            if (refreshed == null || refreshed.Length == 0)
            {
                return;
            }

            _conversionCandidates = refreshed;
            if (_conversionIndex >= _conversionCandidates.Length)
            {
                _conversionIndex = 0;
            }

            ShowPointerCandidates();
            RefreshDisplay(notify: true, force: true);
        }

        private void ShowPointerCandidates()        {
            if (!Plugin.Config.ShowCandidatesInGame || _conversionCandidates == null)
            {
                return;
            }

            ImeCandidateList list = new ImeCandidateList
            {
                Candidates = _conversionCandidates,
                Selection = _conversionIndex,
                PageStart = 0,
                PageSize = _conversionCandidates.Length,
            };

            CandidateWindow.Show(list);
        }

        /// <summary>Confirms the candidate the player is looking at.</summary>
        private void CommitConversion()
        {
            if (_conversionCandidates == null)
            {
                return;
            }

            string selected = _conversionCandidates[_conversionIndex];
            if (_target != null && _target.IsAlive)
            {
                _baseText = AppendClamped(_baseText, selected, _target.MaxLength);
            }

            // The reading was replaced by the candidate, so it must not be shown again.
            _keyboardReading = string.Empty;
            ClearConversion();
        }

        private void ClearConversion()
        {
            if (_conversionCandidates == null)
            {
                return;
            }

            _conversionCandidates = null;
            _conversionIndex = 0;
            CandidateWindow.Hide();
        }

        /// <summary>
        /// Folds everything the pointer keyboard is composing into the committed text.
        /// </summary>
        private void CommitPointerInput(bool notify)
        {
            FlushKeyboardRomaji(_target);

            if (_conversionCandidates != null)
            {
                CommitConversion();
            }
            else if (_keyboardReading.Length > 0)
            {
                if (_target != null && _target.IsAlive)
                {
                    _baseText = AppendClamped(_baseText, _keyboardReading, _target.MaxLength);
                }

                _keyboardReading = string.Empty;
            }

            if (notify)
            {
                RefreshDisplay(notify: true, force: true);
            }
        }

        /// <summary>
        /// Sends the key a tap stands for so the Windows IME runs its normal romaji to
        /// kana/kanji flow. The key is verified on the next frames: if the IME does not
        /// react, the plugin stops simulating and uses its own converter instead.
        /// </summary>
        private bool HandleWithSyntheticKey(char letter, int maxLength)
        {
            CommitPointerInput(notify: false);

            if (SyntheticKeyboard.TrySend(letter))
            {
                _probeChar = letter;
                _probeComposition = _composition;
                _probeTextFrame = _lastTextInputFrame;
                _probeDeadline = Time.unscaledTime + 0.5f;
                return true;
            }

            // Characters without a virtual key (and non-foreground windows) are
            // inserted directly so a tap never disappears.
            _baseText = AppendClamped(_baseText, letter.ToString(), maxLength);
            RefreshDisplay(notify: true, force: true);
            return true;
        }

        /// <summary>
        /// Confirms the synthetic key reached the IME. A tap that produces neither a
        /// composition nor a committed character means the simulation cannot work in
        /// this game, so the built-in converter takes over.
        /// </summary>
        private void VerifySyntheticKeyboard()
        {
            if (_probeChar == '\0')
            {
                return;
            }

            if (_composition != _probeComposition || _lastTextInputFrame != _probeTextFrame)
            {
                _probeChar = '\0';
                return;
            }

            if (Time.unscaledTime < _probeDeadline)
            {
                return;
            }

            char missed = _probeChar;
            _probeChar = '\0';
            _simulationUnavailable = true;
            Plugin.Log?.Warn(
                "JapaneseIME: the synthetic key produced no IME input. " +
                "The on-screen keyboard now uses built-in romaji conversion.");

            // Do not lose the tap that proved the simulation cannot work.
            if (_target != null && _target.IsAlive)
            {
                HandleWithRomajiConversion(missed, _target.MaxLength);
            }
        }

        /// <summary>
        /// If a tap committed the live composition instead of extending it, the IME can
        /// never build a multi-character reading from pointer input. Falls back to the
        /// built-in converter for the rest of the session.
        /// </summary>
        private void DetectCommittedComposition()
        {
            if (_probeChar == '\0' || !IsRomajiLetter(_probeChar))
            {
                return;
            }

            if (_composition.Length > 0 || _lastTextInputFrame == _probeTextFrame)
            {
                return;
            }

            _probeChar = '\0';
            _simulationUnavailable = true;
            Plugin.Log?.Warn(
                "JapaneseIME: the IME composition is committed between taps. " +
                "The on-screen keyboard now uses built-in romaji conversion.");
        }

        private static bool IsRomajiLetter(char character)
        {
            return (character >= 'a' && character <= 'z') ||
                   (character >= 'A' && character <= 'Z');
        }

        /// <summary>
        /// Removes one pending romaji character. Returns false when there is nothing
        /// pending so the game can delete from the field as usual.
        /// </summary>
        internal bool TryHandleGameKeyboardBackspace(InputFieldView field)
        {
            if (_target == null || field == null || _target.Owner != field)
            {
                return false;
            }

            if (_conversionCandidates != null)
            {
                // Backspace cancels the conversion and returns to the reading.
                ClearConversion();
                RefreshDisplay(notify: true, force: true);
                return true;
            }

            if (_keyboardRomaji.Length > 0)
            {
                try
                {
                    _keyboardRomaji = _keyboardRomaji.Substring(0, _keyboardRomaji.Length - 1);
                    RefreshDisplay(notify: true, force: true);
                    return true;
                }
                catch (Exception ex)
                {
                    LogError(ex);
                    return false;
                }
            }

            if (_keyboardReading.Length > 0)
            {
                _keyboardReading = RemoveLastTextElement(_keyboardReading);
                RefreshDisplay(notify: true, force: true);
                return true;
            }

            if (Plugin.Config.SimulatePhysicalKeyboard &&
                !_simulationUnavailable &&
                SyntheticKeyboard.TrySendBackspace())
            {
                return true;
            }

            return false;
        }

        private bool IsJapaneseInputMode()
        {
            bool open;
            int conversion;
            return ImeSwitcher.TryGetConversionFor(ImeSwitcher.OwnWindow(), out open, out conversion) &&
                   open &&
                   (conversion & ImeSwitcher.ImeCmodeNative) != 0;
        }

        private bool IsKatakanaMode()
        {
            bool open;
            int conversion;
            return ImeSwitcher.TryGetConversionFor(ImeSwitcher.OwnWindow(), out open, out conversion) &&
                   (conversion & ImeSwitcher.ImeCmodeKatakana) != 0;
        }

        /// <summary>
        /// Converts any pending romaji into the reading buffer.
        /// </summary>
        private void FlushKeyboardRomaji(IImeTarget target)
        {
            if (_keyboardRomaji.Length == 0)
            {
                return;
            }

            string pending = _keyboardRomaji;
            _keyboardRomaji = string.Empty;

            string produced;
            string remaining;
            RomajiKana.Convert(pending, IsKatakanaMode(), true, out produced, out remaining);

            if (produced.Length > 0)
            {
                int maxLength = target != null ? target.MaxLength : 0;
                _keyboardReading = AppendClamped(_keyboardReading, produced, maxLength);
            }
        }

        internal void Deactivate(IImeTarget target)
        {
            if (_target == null || target == null || _target.Owner != target.Owner)
            {
                return;
            }

            DeactivateInternal(commitComposition: true);
        }

        /// <summary>
        /// Called before the game handles its own OK action so a pending composition
        /// is folded into the committed text first.
        /// </summary>
        internal void CommitCompositionBeforeExternalAction()
        {
            if (_target == null)
            {
                return;
            }

            try
            {
                HandlePhysicalInput();
                ApplyPendingEdits();
                CommitPointerInput(notify: false);

                if (_composition.Length > 0)
                {
                    _baseText = AppendClamped(_baseText, _composition, _target.MaxLength);
                    _composition = string.Empty;
                    MarkCompositionEnded();
                }

                RefreshDisplay(notify: true, force: true);
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private void DeactivateInternal(bool commitComposition)
        {
            CandidateWindow.Hide();

            IImeTarget target = _target;

            if (target == null)
            {
                _keyboardRomaji = string.Empty;
                _keyboardReading = string.Empty;
                _conversionCandidates = null;
                DetachKeyboard();
                DisableIme();
                return;
            }

            try
            {
                // Update the committed buffer without notifying the game yet. The
                // target is cleared before the final notification so a game callback
                // cannot re-enter this manager.
                ApplyPendingEditsToBase(target);
                bool hadPointerInput = HasPointerInput;
                CommitPointerInput(notify: false);

                bool hadComposition = _composition.Length > 0;
                if (commitComposition && hadComposition)
                {
                    _baseText = AppendClamped(_baseText, _composition, target.MaxLength);
                }

                _composition = string.Empty;
                _lastPreview = null;
                PendingText.Length = 0;
                _pendingBackspaces = 0;
                _pendingPaste = false;
                _target = null;

                if (target.IsAlive)
                {
                    if (commitComposition && (hadComposition || hadPointerInput))
                    {
                        target.Commit(_baseText);
                    }
                    else
                    {
                        target.SetPreview(_baseText);
                    }
                }

                Plugin.Log?.Info($"JapaneseIME deactivated for '{target.FieldName}'.");
            }
            catch (Exception ex)
            {
                _target = null;
                LogError(ex);
            }
            finally
            {
                DetachKeyboard();
                DisableIme();
            }
        }

        private void Update()
        {
            if (Plugin.Config.SelfTestMode)
            {
                UpdateSelfTestHotkeys();
            }

            if (_target == null)
            {
                CandidateWindow.Hide();
                return;
            }

            try
            {
                if (!_target.IsAlive || _target.Owner == null)
                {
                    DeactivateInternal(commitComposition: true);
                    return;
                }

                PollCandidates();
                VerifySyntheticKeyboard();

                ImeCandidateList pendingCandidates;
                if (ImeMessageHook.TryTakePending(out pendingCandidates))
                {
                    CandidateWindow.Show(pendingCandidates);
                }
                else if (ImeMessageHook.TryTakePendingHide())
                {
                    CandidateWindow.Hide();
                }

                OnlineDictionary.Result onlineResult;
                if (OnlineDictionary.TryTakeCompleted(out onlineResult))
                {
                    RefreshConversionWithOnline(onlineResult.Reading);
                }

                // While the on-screen keyboard drove the IME host, the plugin owned
                // the field text and Unity's own IME bridge had to stay out of the way.
                // That path is gone: all input now arrives through the field's own IME.
                RefreshKeyboardAttachment();
                EnsureImeEnabled();
                EnsureJapaneseInputMode();
                PollLegacyComposition();
                PollNativeComposition();
                HandlePhysicalInput();
                ApplyPendingEdits();
                ReassertPreview();
                CandidateWindow.FollowCamera();

                // Moving the IME composition window while a composition is live
                // makes the OS IME commit it, which turns every key press into an
                // unwanted 確定. The position is only refreshed between compositions.
                if (_composition.Length == 0)
                {
                    UpdateCandidateWindow(force: false);
                }

                // Ctrl+Shift+J re-selects Japanese input if the OS IME reset itself.
                if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                    (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) &&
                    Input.GetKeyDown(KeyCode.J))
                {
                    ApplyJapaneseInputMode();
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private float _nextCandidatePollTime;
        private bool _candidateListVisible;

        /// <summary>
        /// Mirrors the Windows IME candidate list into world space. The OS candidate
        /// window is a desktop window and cannot be seen in a headset.
        /// </summary>
        private void PollCandidates()
        {
            if (!Plugin.Config.ShowCandidatesInGame)
            {
                ImeMessageHook.Uninstall();
                CandidateWindow.Hide();
                return;
            }

            ImeMessageHook.EnsureInstalled();
            if (ImeMessageHook.Installed)
            {
                // The window hook receives WM_IME_NOTIFY and updates the overlay
                // exactly when the IME exposes its candidate list. Polling from
                // Update does not return candidates on current Windows IMEs.
                return;
            }

            if (Time.unscaledTime < _nextCandidatePollTime)
            {
                return;
            }

            _nextCandidatePollTime = Time.unscaledTime + 0.05f;

            IntPtr hwnd = ImeSwitcher.OwnWindow();

            ImeCandidateList candidates;
            bool found = ImeCandidates.TryRead(hwnd, out candidates);
            if (found)
            {
                CandidateWindow.Show(candidates);
            }
            else
            {
                CandidateWindow.Hide();
            }

            if (Plugin.Config.VerboseLogging && found != _candidateListVisible)
            {
                if (found)
                {
                    Plugin.Log?.Debug(
                        $"JapaneseIME candidates: {candidates.Candidates.Length} entries, " +
                        $"page start {candidates.PageStart}, page size {candidates.PageSize}, " +
                        $"selected {candidates.Selection}.");
                }
                else
                {
                    Plugin.Log?.Debug("JapaneseIME candidates: list closed.");
                }
            }

            _candidateListVisible = found;
        }

        private void RefreshKeyboardAttachment()        {
            Keyboard current = null;
            try
            {
                current = Keyboard.current;
            }
            catch
            {
                // Input System not ready; the legacy path will be used.
            }

            if (current == _keyboard)
            {
                return;
            }

            DetachKeyboard();
            if (current == null)
            {
                return;
            }

            _keyboard = current;
            try
            {
                _keyboard.onTextInput += OnTextInput;
                _keyboard.onIMECompositionChange += OnImeCompositionChange;
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private void AttachKeyboard()
        {
            RefreshKeyboardAttachment();
        }

        private void UpdateSelfTestHotkeys()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    RunFieldDiagnostic();
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        /// <summary>
        /// Writes a Japanese string into every live InputFieldView using the same
        /// adapter used for IME commits, and logs what the field reports back.
        /// </summary>
        private void RunFieldDiagnostic()
        {
            InputFieldView[] fields = Resources.FindObjectsOfTypeAll<InputFieldView>();
            Plugin.Log.Info($"JapaneseIME diagnostic: {fields.Length} InputFieldView object(s) in memory.");

            int written = 0;
            foreach (InputFieldView field in fields)
            {
                if (field == null)
                {
                    continue;
                }

                bool active = field.gameObject.activeInHierarchy;
                InputFieldTarget target = new InputFieldTarget(field);
                string before = target.ReadText();
                Plugin.Log.Info(
                    $"JapaneseIME diagnostic: '{field.gameObject.name}' active={active} " +
                    $"maxLength={target.MaxLength} text='{before}'");

                if (!active)
                {
                    continue;
                }

                target.Commit("日本語テスト");
                written++;
                Plugin.Log.Info(
                    $"JapaneseIME diagnostic: wrote to '{field.gameObject.name}', now '{target.ReadText()}'");
            }

            Plugin.Log.Info($"JapaneseIME diagnostic finished, wrote to {written} active field(s).");
        }

        private void DetachKeyboard()
        {
            if (_keyboard == null)
            {
                return;
            }

            try
            {
                _keyboard.onTextInput -= OnTextInput;
                _keyboard.onIMECompositionChange -= OnImeCompositionChange;
            }
            catch
            {
                // Device may already be gone.
            }

            _keyboard = null;
        }

        private void EnableIme()
        {
            try
            {
                Input.imeCompositionMode = IMECompositionMode.On;
            }
            catch (Exception ex)
            {
                if (Plugin.Config.VerboseLogging)
                {
                    LogError(ex);
                }
            }

            EnableInputSystemIme();
            _imeEnabledForTarget = true;
            _nextImeEnableTime = Time.unscaledTime + ImeEnableRetryInterval;
        }

        /// <summary>
        /// Selects the Japanese keyboard mode. Unity only enables IME composition,
        /// so without this the OS IME can stay in alphanumeric mode and produce
        /// half-width ASCII.
        /// </summary>
        private void ApplyJapaneseInputMode()
        {
            PluginConfig config = Plugin.Config;

            // Once the player picked a mode with the on-screen key, that choice wins;
            // re-forcing hiragana here is what made the label disagree with the IME.
            if (!config.ForceJapaneseInput || KeyboardLayout.HasUserChosenMode)
            {
                _japaneseModeConfirmed = true;
                return;
            }

            string detail;
            bool ok = ImeSwitcher.ForceJapanese(false, out detail);

            bool open;
            int conversion;
            if (ImeSwitcher.TryGetStatus(out open, out conversion) &&
                open &&
                (conversion & ImeSwitcher.ImeCmodeNative) != 0 &&
                (conversion & ImeSwitcher.ImeCmodeFullShape) != 0)
            {
                _japaneseModeConfirmed = true;
            }

            Plugin.Log?.Info(
                $"JapaneseIME: Japanese input mode {(ok ? "requested" : "request failed")} ({detail}); " +
                $"confirmed={_japaneseModeConfirmed}; ime state: {ImeSwitcher.Describe()}");
        }

        /// <summary>
        /// The IME context is created lazily after composition is enabled, so the
        /// first switch attempt can be dropped. Retry until the mode takes effect.
        /// </summary>
        private void EnsureJapaneseInputMode()
        {
            if (!Plugin.Config.ForceJapaneseInput || _japaneseModeConfirmed)
            {
                return;
            }

            if (Time.unscaledTime < _nextModeRetryTime)
            {
                return;
            }

            _nextModeRetryTime = Time.unscaledTime + 0.75f;
            ApplyJapaneseInputMode();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus || _target == null || !Plugin.Config.ReapplyOnFocus)
            {
                return;
            }

            try
            {
                ApplyJapaneseInputMode();
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private void EnsureImeEnabled()
        {
            try
            {
                if (Input.imeCompositionMode != IMECompositionMode.On)
                {
                    Input.imeCompositionMode = IMECompositionMode.On;
                }
            }
            catch
            {
            }

            if (Time.unscaledTime >= _nextImeEnableTime)
            {
                _nextImeEnableTime = Time.unscaledTime + ImeEnableRetryInterval;

                // Re-enabling IME resets the OS composition, so only do it while no
                // composition is live and the previous attempt did not stick.
                if (!_imeEnabledForTarget && _composition.Length == 0)
                {
                    EnableInputSystemIme();
                    _imeEnabledForTarget = true;
                }
            }
        }

        private void EnableInputSystemIme()
        {
            Keyboard keyboard = _keyboard;
            if (keyboard == null)
            {
                return;
            }

            try
            {
                keyboard.SetIMEEnabled(true);
            }
            catch (Exception ex)
            {
                if (Plugin.Config.VerboseLogging)
                {
                    LogError(ex);
                }
            }
        }

        private void DisableIme()
        {
            _imeEnabledForTarget = false;

            Keyboard keyboard = _keyboard;
            if (keyboard != null)
            {
                try
                {
                    keyboard.SetIMEEnabled(false);
                }
                catch
                {
                }
            }

            try
            {
                Input.imeCompositionMode = IMECompositionMode.Off;
            }
            catch
            {
            }
        }

        private void OnTextInput(char character)
        {
            if (_target == null)
            {
                return;
            }

            try
            {
                if (_conversionCandidates != null || _keyboardReading.Length > 0)
                {
                    CommitPointerInput(notify: false);
                }

                _sawInputSystemText = true;
                _lastTextInputFrame = Time.frameCount;

                if (character == '\b' || character == '\u007f')
                {
                    // The Input System path handles Backspace via the key itself.
                    if (_keyboard == null && _composition.Length == 0)
                    {
                        _pendingBackspaces++;
                    }

                    return;
                }

                if (character == '\0' || char.IsControl(character))
                {
                    return;
                }

                if (_composition.Length > 0)
                {
                    // A text event while a composition is live is the IME committing it.
                    _composition = string.Empty;
                    MarkCompositionEnded();
                }

                PendingText.Append(character);

                if (Plugin.Config.VerboseLogging)
                {
                    Plugin.Log?.Debug(
                    $"JapaneseIME text input: U+{(int)character:X4} (frame {Time.frameCount})");
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private void OnImeCompositionChange(IMECompositionString compositionString)
        {
            if (_target == null)
            {
                return;
            }

            try
            {
                // A physical IME composition takes over from pointer typing.
                CommitPointerInput(notify: false);

                _sawInputSystemComposition = true;
                _lastCompositionFrame = Time.frameCount;
                SetComposition(compositionString.ToString());
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private string _nativeComposition;

        /// <summary>
        /// Diagnostic: reads the IME composition straight from imm32 so the log
        /// shows whether the OS IME still holds a composition when Unity reports a
        /// commit. Temporary instrumentation for tracking down premature 確定.
        /// </summary>
        private void PollNativeComposition()
        {
            if (!Plugin.Config.VerboseLogging)
            {
                return;
            }

            string actual;
            if (!ImeNative.TryReadComposition(out actual))
            {
                return;
            }

            if (actual == _nativeComposition)
            {
                return;
            }

            _nativeComposition = actual;
            Plugin.Log?.Debug(
                $"JapaneseIME imm32 composition: '{actual}' (unity '{_composition}', frame {Time.frameCount})");
        }

        private void PollLegacyComposition()
        {
            if (_keyboard != null && _sawInputSystemComposition)
            {
                return;
            }

            // When the Input System is not delivering composition events, fall back
            // to the legacy Input API. Both ultimately read the same Windows IME.
            if (_keyboard != null && Time.frameCount - _lastCompositionFrame <= 2)
            {
                return;
            }

            string composition = string.Empty;
            try
            {
                composition = Input.compositionString ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (composition != _composition)
            {
                SetComposition(composition);
            }
        }

        private void SetComposition(string composition)
        {
            composition = composition ?? string.Empty;
            if (composition == _composition)
            {
                return;
            }

            bool wasComposing = _composition.Length > 0;
            _composition = composition;

            if (composition.Length > 0)
            {
                // A real composition string proves the IME is in a Japanese mode.
                _japaneseModeConfirmed = true;
            }

            if (wasComposing && composition.Length == 0)
            {
                MarkCompositionEnded();
            }

            if (Plugin.Config.VerboseLogging)
            {
                Plugin.Log?.Debug(
                    $"JapaneseIME composition: '{composition}' (frame {Time.frameCount})");
            }

            // Only preview during composition. Notifying the game for every
            // intermediate composition would trigger a full search refresh per
            // keystroke (and can make the game reset the field mid-composition).
            RefreshDisplay(notify: false, force: true);
        }

        private void MarkCompositionEnded()
        {
            _compositionEndFrame = Time.frameCount;
            _compositionEndTime = Time.unscaledTime;
        }

        private void HandlePhysicalInput()
        {
            bool composing = _composition.Length > 0;
            bool imeOwnsEditingKeys = composing ||
                                      Time.frameCount - _compositionEndFrame <= 1 ||
                                      Time.unscaledTime - _compositionEndTime < ImeKeyGraceSeconds;

            Keyboard keyboard = _keyboard;
            if (keyboard != null)
            {
                try
                {
                    if (!imeOwnsEditingKeys && keyboard.backspaceKey.wasPressedThisFrame)
                    {
                        _pendingBackspaces++;
                    }

                    if (Plugin.Config.EnableClipboardPaste &&
                        (keyboard.ctrlKey.isPressed || keyboard.leftCtrlKey.isPressed ||
                         keyboard.rightCtrlKey.isPressed) &&
                        keyboard.vKey.wasPressedThisFrame)
                    {
                        _pendingPaste = true;
                    }
                }
                catch (Exception ex)
                {
                    if (Plugin.Config.VerboseLogging)
                    {
                        LogError(ex);
                    }
                }
            }
            else
            {
                try
                {
                    if (!imeOwnsEditingKeys && Input.GetKeyDown(KeyCode.Backspace))
                    {
                        _pendingBackspaces++;
                    }

                    if (Plugin.Config.EnableClipboardPaste &&
                        (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                        Input.GetKeyDown(KeyCode.V))
                    {
                        _pendingPaste = true;
                    }

                    if (!composing && Input.GetKeyDown(KeyCode.Space))
                    {
                        PendingText.Append(' ');
                    }

                    if (!_sawInputSystemText)
                    {
                        string input = Input.inputString;
                        if (!string.IsNullOrEmpty(input))
                        {
                            for (int i = 0; i < input.Length; i++)
                            {
                                char character = input[i];
                                if (character == '\b' || character == '\u007f')
                                {
                                    if (!imeOwnsEditingKeys)
                                    {
                                        _pendingBackspaces++;
                                    }
                                }
                                else if (character != '\0' && character != '\n' &&
                                         character != '\r' && character != '\t')
                                {
                                    PendingText.Append(character);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (Plugin.Config.VerboseLogging)
                    {
                        LogError(ex);
                    }
                }
            }
        }

        private void ApplyPendingEdits()
        {
            if (ApplyPendingEditsToBase(_target))
            {
                RefreshDisplay(notify: true, force: true);
            }
        }

        private bool ApplyPendingEditsToBase(IImeTarget target)
        {
            if (target == null)
            {
                PendingText.Length = 0;
                _pendingBackspaces = 0;
                _pendingPaste = false;
                return false;
            }

            bool changed = false;

            if (PendingText.Length > 0)
            {
                string addition = PendingText.ToString();
                PendingText.Length = 0;
                _baseText = AppendClamped(_baseText, addition, target.MaxLength);
                changed = true;
            }

            if (_pendingBackspaces > 0)
            {
                int count = _pendingBackspaces;
                _pendingBackspaces = 0;
                for (int i = 0; i < count; i++)
                {
                    _baseText = RemoveLastTextElement(_baseText);
                }

                changed = true;
            }

            if (_pendingPaste)
            {
                _pendingPaste = false;
                string clipboard = null;
                try
                {
                    clipboard = GUIUtility.systemCopyBuffer;
                }
                catch (Exception ex)
                {
                    if (Plugin.Config.VerboseLogging)
                    {
                        LogError(ex);
                    }
                }

                if (!string.IsNullOrEmpty(clipboard))
                {
                    clipboard = SanitizeClipboard(clipboard);
                    if (clipboard.Length > 0)
                    {
                        _baseText = AppendClamped(_baseText, clipboard, target.MaxLength);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private void ReassertPreview()
        {
            if (_target == null)
            {
                return;
            }

            string preview = _baseText + _composition + PointerText + _keyboardRomaji;
            string actual;
            try
            {
                actual = _target.ReadText() ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (actual == preview)
            {
                return;
            }

            if (_composition.Length > 0)
            {
                if (actual.Length > preview.Length &&
                    actual.StartsWith(preview, StringComparison.Ordinal))
                {
                    // An on-screen key or another writer appended text while composing.
                    string suffix = actual.Substring(preview.Length);
                    _baseText = AppendClamped(_baseText, suffix, _target.MaxLength);
                }

                // Keep the composition visible even if a game refresh overwrote it.
                RefreshDisplay(notify: false, force: true);
            }
            else
            {
                // The game changed the field (clear button, SetText).
                if (Plugin.Config.VerboseLogging && actual != _baseText)
                {
                    Plugin.Log?.Debug($"JapaneseIME field changed by game: '{_baseText}' -> '{actual}'");
                }

                _baseText = actual;
                _keyboardRomaji = string.Empty;
                _keyboardReading = string.Empty;
                ClearConversion();
                _lastPreview = actual;
            }
        }

        private void RefreshDisplay(bool notify, bool force)
        {
            if (_target == null || !_target.IsAlive)
            {
                return;
            }

            string preview = _baseText + _composition + PointerText + _keyboardRomaji;
            if (!force && preview == _lastPreview)
            {
                if (notify)
                {
                    _target.NotifyChanged();
                }

                return;
            }

            _lastPreview = preview;
            _target.SetPreview(preview);

            if (notify)
            {
                _target.NotifyChanged();

                if (Plugin.Config.VerboseLogging)
                {
                    Plugin.Log?.Debug(
                        $"JapaneseIME field after notify: '{_target.ReadText()}' (wanted '{preview}')");
                }
            }
        }

        private void UpdateCandidateWindow(bool force)
        {
            if (!Plugin.Config.PlaceCandidateWindowNearField)
            {
                return;
            }

            // Repositioning the composition/candidate window finalises a live
            // composition on some IMEs, so it must never run mid-conversion.
            if (_composition.Length > 0)
            {
                return;
            }

            if (!force && Time.unscaledTime < _nextCursorUpdateTime)
            {
                return;
            }

            _nextCursorUpdateTime = Time.unscaledTime + CursorUpdateInterval;

            Vector2 position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            try
            {
                Transform targetTransform = _target != null ? _target.Transform : null;
                Camera camera = Camera.main;
                if (targetTransform != null && camera != null)
                {
                    Vector3 screenPoint = camera.WorldToScreenPoint(targetTransform.position);
                    if (!float.IsNaN(screenPoint.x) && !float.IsNaN(screenPoint.y) &&
                        screenPoint.x >= 0f && screenPoint.x <= Screen.width &&
                        screenPoint.y >= 0f && screenPoint.y <= Screen.height)
                    {
                        position = new Vector2(screenPoint.x, screenPoint.y);
                    }
                }
            }
            catch
            {
                // Keep the centre fallback.
            }

            try
            {
                Input.compositionCursorPos = position;
            }
            catch
            {
            }

            Keyboard keyboard = _keyboard;
            if (keyboard != null)
            {
                try
                {
                    keyboard.SetIMECursorPosition(position);
                }
                catch
                {
                }
            }
        }

        private static string AppendClamped(string current, string addition, int maxLength)
        {
            current = current ?? string.Empty;
            addition = addition ?? string.Empty;

            if (addition.Length == 0)
            {
                return current;
            }

            if (maxLength <= 0)
            {
                return current + addition;
            }

            int room = maxLength - current.Length;
            if (room <= 0)
            {
                return current;
            }

            if (addition.Length <= room)
            {
                return current + addition;
            }

            string truncated = addition.Substring(0, room);
            if (truncated.Length > 0 && char.IsHighSurrogate(truncated[truncated.Length - 1]))
            {
                truncated = truncated.Substring(0, truncated.Length - 1);
            }

            return current + truncated;
        }

        private static string RemoveLastTextElement(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            int index = text.Length - 1;
            if (char.IsLowSurrogate(text[index]) && index > 0 && char.IsHighSurrogate(text[index - 1]))
            {
                index--;
            }

            return text.Substring(0, index);
        }

        private static string SanitizeClipboard(string text)
        {
            StringBuilder builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '\r' || character == '\n' || character == '\t')
                {
                    builder.Append(' ');
                }
                else if (!char.IsControl(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString().Trim();
        }

        private void LogError(Exception ex)
        {
            float now = Time.unscaledTime;
            if (now - _lastErrorTime < 1f)
            {
                return;
            }

            _lastErrorTime = now;
            Plugin.Log?.Error("JapaneseIME: " + ex);
        }
    }
}
