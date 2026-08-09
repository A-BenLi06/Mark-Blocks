using System;
using System.ComponentModel;
using System.Text;
using Windows.Storage;

namespace MetroMarkdownEditor.Services
{
    public enum EditorIndentSizeOnSave
    {
        Auto = 0,
        Two = 1,
        Three = 2,
        Four = 3,
        Five = 4,
        Tab = 5
    }

    public enum EditorDefaultLineEnding
    {
        LF = 0,
        CRLF = 1
    }

    public enum EditorSpellCheckMode
    {
        AutoDetectLanguage = 0,
        Disabled = 1,
        EnglishUS = 2,
        EnglishUK = 3,
        Czech = 4,
        Danish = 5,
        German = 6,
        Spanish = 7,
        Greek = 8,
        French = 9,
        Galego = 10,
        Croatian = 11,
        Italian = 12,
        Romanian = 13,
        Dutch = 14,
        Hungarian = 15,
        Malay = 16,
        Polish = 17,
        PortugueseBrazil = 18,
        PortuguesePortugal = 19,
        Russian = 20,
        SwissGerman = 21,
        Slovak = 22,
        Slovenian = 23,
        Swedish = 24,
        Vietnamese = 25,
        Turkish = 26,
        Ukrainian = 27,
        Persian = 28,
        Arabic = 29,
        Korean = 30,
        Chinese = 31,
        Japanese = 32,
        Hebrew = 33
    }

    public sealed class EditorSettingsService : INotifyPropertyChanged
    {
        private const string Prefix = "Editor.";

        private EditorIndentSizeOnSave _indentSizeOnSaveMode = EditorIndentSizeOnSave.Two;
        private bool _prettyIndentation;
        private bool _autoPairBracketsAndQuotes = true;
        private bool _autoPairCommonMarkdownSyntax;
        private bool _enableEmojiAutocomplete = true;
        private bool _displaySourceForSimpleBlocksOnFocus;
        private bool _copyMarkdownSourceAsPlainText = true;
        private bool _copyCutWholeLinesWhenNoSelection;
        private bool _uploadPastedImagesWithPicGo;
        private string _picGoServerUrl = "http://127.0.0.1:36677";
        private string _picGoServerSecret = string.Empty;
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

        public EditorIndentSizeOnSave IndentSizeOnSaveMode
        {
            get { return _indentSizeOnSaveMode; }
            set { SetValue(ref _indentSizeOnSaveMode, value, "IndentSizeOnSaveMode"); }
        }

        public int IndentSizeOnSaveIndex
        {
            get { return (int)_indentSizeOnSaveMode; }
            set { IndentSizeOnSaveMode = (EditorIndentSizeOnSave)Math.Max(0, Math.Min(5, value)); }
        }

        public int IndentSizeOnSave
        {
            get { return GetIndentSizeValue(_indentSizeOnSaveMode); }
            set { IndentSizeOnSaveMode = MapIndentSizeValue(value); }
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

        public bool UploadPastedImagesWithPicGo
        {
            get { return _uploadPastedImagesWithPicGo; }
            set { SetValue(ref _uploadPastedImagesWithPicGo, value, "UploadPastedImagesWithPicGo"); }
        }

        public string PicGoServerUrl
        {
            get { return _picGoServerUrl; }
            set { SetValue(ref _picGoServerUrl, value ?? string.Empty, "PicGoServerUrl"); }
        }

        public string PicGoServerSecret
        {
            get { return _picGoServerSecret; }
            set { SetValue(ref _picGoServerSecret, value ?? string.Empty, "PicGoServerSecret"); }
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
            set { SpellCheckMode = (EditorSpellCheckMode)Math.Max(0, Math.Min(33, value)); }
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
            var legacyIndentSize = ReadInt("IndentSizeOnSave", 2, 2, 8);
            _indentSizeOnSaveMode = (EditorIndentSizeOnSave)ReadInt("IndentSizeOnSaveMode", (int)MapIndentSizeValue(legacyIndentSize), 0, 5);
            _prettyIndentation = ReadBool("PrettyIndentation", _prettyIndentation);
            _autoPairBracketsAndQuotes = ReadBool("AutoPairBracketsAndQuotes", _autoPairBracketsAndQuotes);
            _autoPairCommonMarkdownSyntax = ReadBool("AutoPairCommonMarkdownSyntax", _autoPairCommonMarkdownSyntax);
            _enableEmojiAutocomplete = ReadBool("EnableEmojiAutocomplete", _enableEmojiAutocomplete);
            _displaySourceForSimpleBlocksOnFocus = ReadBool("DisplaySourceForSimpleBlocksOnFocus", _displaySourceForSimpleBlocksOnFocus);
            _copyMarkdownSourceAsPlainText = ReadBool("CopyMarkdownSourceAsPlainText", _copyMarkdownSourceAsPlainText);
            _copyCutWholeLinesWhenNoSelection = ReadBool("CopyCutWholeLinesWhenNoSelection", _copyCutWholeLinesWhenNoSelection);
            _uploadPastedImagesWithPicGo = ReadBool("UploadPastedImagesWithPicGo", _uploadPastedImagesWithPicGo);
            _picGoServerUrl = ReadString("PicGoServerUrl", _picGoServerUrl);
            _picGoServerSecret = ReadString("PicGoServerSecret", _picGoServerSecret);
            _defaultLineEnding = (EditorDefaultLineEnding)ReadInt("DefaultLineEnding", (int)_defaultLineEnding, 0, 1);
            _spellCheckMode = (EditorSpellCheckMode)ReadInt("SpellCheckMode", (int)_spellCheckMode, 0, 33);
            _typewriterFocusModeEnabled = ReadBool("TypewriterFocusModeEnabled", _typewriterFocusModeEnabled);
            _keepCaretInMiddleWhenTypewriterModeEnabled = ReadBool("KeepCaretInMiddleWhenTypewriterModeEnabled", _keepCaretInMiddleWhenTypewriterModeEnabled);
        }

        private string NormalizeMarkdownIndentation(string content)
        {
            var lines = content.Split('\n');
            var unit = GetIndentUnit();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                var tabCount = 0;
                while (tabCount < line.Length && line[tabCount] == '\t') tabCount++;
                if (tabCount == 0) continue;

                var prefix = new StringBuilder();
                for (var j = 0; j < tabCount; j++)
                {
                    prefix.Append(unit);
                }
                lines[i] = prefix.ToString() + line.Substring(tabCount);
            }

            return string.Join("\n", lines);
        }

        private string GetIndentUnit()
        {
            return _indentSizeOnSaveMode == EditorIndentSizeOnSave.Tab
                ? "\t"
                : new string(' ', IndentSizeOnSave);
        }

        private static int GetIndentSizeValue(EditorIndentSizeOnSave mode)
        {
            switch (mode)
            {
                case EditorIndentSizeOnSave.Three:
                    return 3;
                case EditorIndentSizeOnSave.Four:
                    return 4;
                case EditorIndentSizeOnSave.Five:
                    return 5;
                case EditorIndentSizeOnSave.Auto:
                case EditorIndentSizeOnSave.Two:
                case EditorIndentSizeOnSave.Tab:
                default:
                    return 2;
            }
        }

        private static EditorIndentSizeOnSave MapIndentSizeValue(int value)
        {
            switch (value)
            {
                case 3:
                    return EditorIndentSizeOnSave.Three;
                case 4:
                    return EditorIndentSizeOnSave.Four;
                case 5:
                    return EditorIndentSizeOnSave.Five;
                case 2:
                    return EditorIndentSizeOnSave.Two;
                default:
                    return EditorIndentSizeOnSave.Auto;
            }
        }

        private void Save()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            settings[Prefix + "IndentSizeOnSaveMode"] = (int)_indentSizeOnSaveMode;
            settings[Prefix + "IndentSizeOnSave"] = IndentSizeOnSave;
            settings[Prefix + "PrettyIndentation"] = _prettyIndentation;
            settings[Prefix + "AutoPairBracketsAndQuotes"] = _autoPairBracketsAndQuotes;
            settings[Prefix + "AutoPairCommonMarkdownSyntax"] = _autoPairCommonMarkdownSyntax;
            settings[Prefix + "EnableEmojiAutocomplete"] = _enableEmojiAutocomplete;
            settings[Prefix + "DisplaySourceForSimpleBlocksOnFocus"] = _displaySourceForSimpleBlocksOnFocus;
            settings[Prefix + "CopyMarkdownSourceAsPlainText"] = _copyMarkdownSourceAsPlainText;
            settings[Prefix + "CopyCutWholeLinesWhenNoSelection"] = _copyCutWholeLinesWhenNoSelection;
            settings[Prefix + "UploadPastedImagesWithPicGo"] = _uploadPastedImagesWithPicGo;
            settings[Prefix + "PicGoServerUrl"] = _picGoServerUrl ?? string.Empty;
            settings[Prefix + "PicGoServerSecret"] = _picGoServerSecret ?? string.Empty;
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

        private string ReadString(string key, string defaultValue)
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            object raw;
            return settings.TryGetValue(Prefix + key, out raw) && raw != null ? raw.ToString() : defaultValue;
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
            if (propertyName == "IndentSizeOnSaveMode")
            {
                OnPropertyChanged("IndentSizeOnSaveIndex");
                OnPropertyChanged("IndentSizeOnSave");
            }
            else if (propertyName == "DefaultLineEnding")
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
