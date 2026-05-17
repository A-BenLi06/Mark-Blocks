using System;
using System.ComponentModel;
using Windows.Storage;

namespace MetroMarkdownEditor.Services
{
    public enum EditorDefaultLineEnding
    {
        LF = 0,
        CRLF = 1
    }

    public enum EditorSpellCheckMode
    {
        AutoDetectLanguage = 0,
        Disabled = 1
    }

    public sealed class EditorSettingsService : INotifyPropertyChanged
    {
        private const string Prefix = "Editor.";

        private int _indentSizeOnSave = 2;
        private bool _prettyIndentation;
        private bool _autoPairBracketsAndQuotes = true;
        private bool _autoPairCommonMarkdownSyntax;
        private bool _enableEmojiAutocomplete = true;
        private bool _displaySourceForSimpleBlocksOnFocus;
        private bool _copyMarkdownSourceAsPlainText = true;
        private bool _copyCutWholeLinesWhenNoSelection;
        private EditorDefaultLineEnding _defaultLineEnding = EditorDefaultLineEnding.CRLF;
        private EditorSpellCheckMode _spellCheckMode = EditorSpellCheckMode.AutoDetectLanguage;
        private bool _typewriterFocusModeEnabled;
        private bool _keepCaretInMiddleWhenTypewriterModeEnabled = true;

        public static EditorSettingsService Instance { get; } = new EditorSettingsService();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler SettingsChanged;

        private EditorSettingsService()
        {
            Load();
        }

        public int IndentSizeOnSave
        {
            get { return _indentSizeOnSave; }
            set { SetValue(ref _indentSizeOnSave, Math.Max(2, Math.Min(8, value)), "IndentSizeOnSave"); }
        }

        public bool PrettyIndentation
        {
            get { return _prettyIndentation; }
            set { SetValue(ref _prettyIndentation, value, "PrettyIndentation"); }
        }

        public bool AutoPairBracketsAndQuotes
        {
            get { return _autoPairBracketsAndQuotes; }
            set { SetValue(ref _autoPairBracketsAndQuotes, value, "AutoPairBracketsAndQuotes"); }
        }

        public bool AutoPairCommonMarkdownSyntax
        {
            get { return _autoPairCommonMarkdownSyntax; }
            set { SetValue(ref _autoPairCommonMarkdownSyntax, value, "AutoPairCommonMarkdownSyntax"); }
        }

        public bool EnableEmojiAutocomplete
        {
            get { return _enableEmojiAutocomplete; }
            set { SetValue(ref _enableEmojiAutocomplete, value, "EnableEmojiAutocomplete"); }
        }

        public bool DisplaySourceForSimpleBlocksOnFocus
        {
            get { return _displaySourceForSimpleBlocksOnFocus; }
            set { SetValue(ref _displaySourceForSimpleBlocksOnFocus, value, "DisplaySourceForSimpleBlocksOnFocus"); }
        }

        public bool CopyMarkdownSourceAsPlainText
        {
            get { return _copyMarkdownSourceAsPlainText; }
            set { SetValue(ref _copyMarkdownSourceAsPlainText, value, "CopyMarkdownSourceAsPlainText"); }
        }

        public bool CopyCutWholeLinesWhenNoSelection
        {
            get { return _copyCutWholeLinesWhenNoSelection; }
            set { SetValue(ref _copyCutWholeLinesWhenNoSelection, value, "CopyCutWholeLinesWhenNoSelection"); }
        }

        public EditorDefaultLineEnding DefaultLineEnding
        {
            get { return _defaultLineEnding; }
            set { SetValue(ref _defaultLineEnding, value, "DefaultLineEnding"); }
        }

        public bool IsDefaultLineEndingLF
        {
            get { return _defaultLineEnding == EditorDefaultLineEnding.LF; }
            set { if (value) DefaultLineEnding = EditorDefaultLineEnding.LF; }
        }

        public bool IsDefaultLineEndingCRLF
        {
            get { return _defaultLineEnding == EditorDefaultLineEnding.CRLF; }
            set { if (value) DefaultLineEnding = EditorDefaultLineEnding.CRLF; }
        }

        public EditorSpellCheckMode SpellCheckMode
        {
            get { return _spellCheckMode; }
            set { SetValue(ref _spellCheckMode, value, "SpellCheckMode"); }
        }

        public int SpellCheckModeIndex
        {
            get { return (int)_spellCheckMode; }
            set { SpellCheckMode = (EditorSpellCheckMode)Math.Max(0, Math.Min(1, value)); }
        }

        public bool IsSpellCheckEnabled
        {
            get { return _spellCheckMode != EditorSpellCheckMode.Disabled; }
        }

        public bool TypewriterFocusModeEnabled
        {
            get { return _typewriterFocusModeEnabled; }
            set { SetValue(ref _typewriterFocusModeEnabled, value, "TypewriterFocusModeEnabled"); }
        }

        public bool KeepCaretInMiddleWhenTypewriterModeEnabled
        {
            get { return _keepCaretInMiddleWhenTypewriterModeEnabled; }
            set { SetValue(ref _keepCaretInMiddleWhenTypewriterModeEnabled, value, "KeepCaretInMiddleWhenTypewriterModeEnabled"); }
        }

        public string NormalizeLineEndings(string content)
        {
            var normalized = (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            return _defaultLineEnding == EditorDefaultLineEnding.CRLF
                ? normalized.Replace("\n", "\r\n")
                : normalized;
        }

        public string NormalizeContentForSave(string content)
        {
            var normalized = (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            if (_prettyIndentation)
            {
                normalized = NormalizeMarkdownIndentation(normalized);
            }

            return NormalizeLineEndings(normalized);
        }

        public void TurnOffTypewriterFocusMode()
        {
            TypewriterFocusModeEnabled = false;
        }

        private void Load()
        {
            _indentSizeOnSave = ReadInt("IndentSizeOnSave", _indentSizeOnSave, 2, 8);
            _prettyIndentation = ReadBool("PrettyIndentation", _prettyIndentation);
            _autoPairBracketsAndQuotes = ReadBool("AutoPairBracketsAndQuotes", _autoPairBracketsAndQuotes);
            _autoPairCommonMarkdownSyntax = ReadBool("AutoPairCommonMarkdownSyntax", _autoPairCommonMarkdownSyntax);
            _enableEmojiAutocomplete = ReadBool("EnableEmojiAutocomplete", _enableEmojiAutocomplete);
            _displaySourceForSimpleBlocksOnFocus = ReadBool("DisplaySourceForSimpleBlocksOnFocus", _displaySourceForSimpleBlocksOnFocus);
            _copyMarkdownSourceAsPlainText = ReadBool("CopyMarkdownSourceAsPlainText", _copyMarkdownSourceAsPlainText);
            _copyCutWholeLinesWhenNoSelection = ReadBool("CopyCutWholeLinesWhenNoSelection", _copyCutWholeLinesWhenNoSelection);
            _defaultLineEnding = (EditorDefaultLineEnding)ReadInt("DefaultLineEnding", (int)_defaultLineEnding, 0, 1);
            _spellCheckMode = (EditorSpellCheckMode)ReadInt("SpellCheckMode", (int)_spellCheckMode, 0, 1);
            _typewriterFocusModeEnabled = ReadBool("TypewriterFocusModeEnabled", _typewriterFocusModeEnabled);
            _keepCaretInMiddleWhenTypewriterModeEnabled = ReadBool("KeepCaretInMiddleWhenTypewriterModeEnabled", _keepCaretInMiddleWhenTypewriterModeEnabled);
        }

        private string NormalizeMarkdownIndentation(string content)
        {
            var lines = content.Split('\n');
            var unit = new string(' ', _indentSizeOnSave);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                var tabCount = 0;
                while (tabCount < line.Length && line[tabCount] == '\t') tabCount++;
                if (tabCount == 0) continue;

                lines[i] = new string(' ', tabCount * unit.Length) + line.Substring(tabCount);
            }

            return string.Join("\n", lines);
        }

        private void Save()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            settings[Prefix + "IndentSizeOnSave"] = _indentSizeOnSave;
            settings[Prefix + "PrettyIndentation"] = _prettyIndentation;
            settings[Prefix + "AutoPairBracketsAndQuotes"] = _autoPairBracketsAndQuotes;
            settings[Prefix + "AutoPairCommonMarkdownSyntax"] = _autoPairCommonMarkdownSyntax;
            settings[Prefix + "EnableEmojiAutocomplete"] = _enableEmojiAutocomplete;
            settings[Prefix + "DisplaySourceForSimpleBlocksOnFocus"] = _displaySourceForSimpleBlocksOnFocus;
            settings[Prefix + "CopyMarkdownSourceAsPlainText"] = _copyMarkdownSourceAsPlainText;
            settings[Prefix + "CopyCutWholeLinesWhenNoSelection"] = _copyCutWholeLinesWhenNoSelection;
            settings[Prefix + "DefaultLineEnding"] = (int)_defaultLineEnding;
            settings[Prefix + "SpellCheckMode"] = (int)_spellCheckMode;
            settings[Prefix + "TypewriterFocusModeEnabled"] = _typewriterFocusModeEnabled;
            settings[Prefix + "KeepCaretInMiddleWhenTypewriterModeEnabled"] = _keepCaretInMiddleWhenTypewriterModeEnabled;
        }

        private bool ReadBool(string key, bool defaultValue)
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            object raw;
            if (!settings.TryGetValue(Prefix + key, out raw) || raw == null) return defaultValue;
            bool parsed;
            return bool.TryParse(raw.ToString(), out parsed) ? parsed : defaultValue;
        }

        private int ReadInt(string key, int defaultValue, int min, int max)
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            object raw;
            if (!settings.TryGetValue(Prefix + key, out raw) || raw == null) return defaultValue;
            int parsed;
            return int.TryParse(raw.ToString(), out parsed) ? Math.Max(min, Math.Min(max, parsed)) : defaultValue;
        }

        private void SetValue<T>(ref T field, T value, string propertyName)
        {
            if (object.Equals(field, value)) return;
            field = value;
            Save();
            OnPropertyChanged(propertyName);
            RaiseCompanions(propertyName);
            OnSettingsChanged();
        }

        private void RaiseCompanions(string propertyName)
        {
            if (propertyName == "DefaultLineEnding")
            {
                OnPropertyChanged("IsDefaultLineEndingLF");
                OnPropertyChanged("IsDefaultLineEndingCRLF");
            }
            else if (propertyName == "SpellCheckMode")
            {
                OnPropertyChanged("SpellCheckModeIndex");
                OnPropertyChanged("IsSpellCheckEnabled");
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnSettingsChanged()
        {
            var handler = SettingsChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
