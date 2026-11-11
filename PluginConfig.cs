namespace JapaneseIME
{
    public class PluginConfig
    {
        public static PluginConfig Instance { get; set; }

        /// <summary>Master switch. When false the plugin leaves the game's keyboard untouched.</summary>
        public virtual bool Enabled { get; set; } = true;

        /// <summary>Enable IME on password-like fields. Off by default.</summary>
        public virtual bool EnableForPasswordFields { get; set; } = false;

        /// <summary>Allow Ctrl+V to paste text from the Windows clipboard.</summary>
        public virtual bool EnableClipboardPaste { get; set; } = true;

        /// <summary>Place the Windows IME candidate window near the focused field.</summary>
        public virtual bool PlaceCandidateWindowNearField { get; set; } = true;

        /// <summary>Log composition strings and other verbose details.</summary>
        public virtual bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// Diagnostic mode: log IME composition/commit events without requiring an
        /// in-game text field. Intended for troubleshooting only.
        /// </summary>
        public virtual bool SelfTestMode { get; set; } = false;

        /// <summary>
        /// Switch Windows IME to Japanese (hiragana) input when a text field opens.
        /// Without this the IME often stays in alphanumeric mode and every key
        /// arrives as a half-width ASCII character.
        /// </summary>
        public virtual bool ForceJapaneseInput { get; set; } = true;

        /// <summary>
        /// Re-apply the Japanese input mode when the game window regains focus.
        /// </summary>
        public virtual bool ReapplyOnFocus { get; set; } = true;

        /// <summary>
        /// Show the extra key that switches the Windows IME conversion mode
        /// (ひらがな / カタカナ / 半角カナ / 半角英数) from the on-screen keyboard.
        /// The label always mirrors the IME's real state.
        /// </summary>
        public virtual bool ShowImeModeKey { get; set; } = true;

        /// <summary>
        /// Draw the Windows IME candidate list inside the game. The OS candidate
        /// window is a desktop window and is invisible in a headset.
        /// </summary>
        public virtual bool ShowCandidatesInGame { get; set; } = true;

        /// <summary>
        /// Convert romaji typed with the in-game (VR pointer) keyboard into kana while
        /// the Windows IME is in a Japanese mode. Those keys write characters directly
        /// into the field, so without this every tap stays half-width ASCII.
        /// </summary>
        public virtual bool ConvertOnScreenKeyboard { get; set; } = true;

        /// <summary>
        /// Ask Google's CGI API for Japanese input for the conversion candidates and
        /// fall back to the built-in JMdict table when it is unavailable. The reading
        /// is sent to google.com over HTTPS.
        /// </summary>
        public virtual bool UseOnlineDictionary { get; set; } = true;

        /// <summary>
        /// Send the keystroke an on-screen key stands for so the Windows IME converts
        /// it like physical typing. Off by default: the game commits the IME
        /// composition on every UI click, so a tap can only produce one character and
        /// never a convertible reading. The built-in romaji converter is used instead.
        /// </summary>
        public virtual bool SimulatePhysicalKeyboard { get; set; } = false;

        /// <summary>
        /// Width of the extra IME mode key, as a multiple of a normal letter key. It
        /// sits in the free strip right of OK. Clamped to 1.0 - 3.0.
        /// </summary>
        public virtual float ExtraKeyWidthScale { get; set; } = 1.8f;
    }
}
