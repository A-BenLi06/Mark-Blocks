using System;
using System.ComponentModel;
using Windows.Storage;

namespace MetroMarkdownEditor.Services
{
    public enum MarkdownHeadingStyle
    {
        Atx = 0,
        Setext = 1
    }

    public enum MarkdownUnorderedListStyle
    {
        Dash = 0,
        Asterisk = 1,
        Plus = 2
    }

    public enum MarkdownOrderedListStyle
    {
        Sequential = 0,
        One = 1
    }

    public enum SmartPunctuationMode
    {
        Disabled = 0,
        ConvertOnInput = 1,
        ConvertOnRender = 2
    }

    public enum DefaultCodeLanguageApplyMode
    {
        WhenAddCodeFencesViaMarkdown = 0,
        Always = 1,
        Never = 2
    }

    public enum MathEquationNumberingMode
    {
        None = 0,
        All = 1
    }

    public enum MathHtmlExportMode
    {
        RenderedSvg = 0,
        TeXSource = 1
    }

    public enum MarkdownWhitespaceMode
    {
        PreserveSequentialWhitespaceAndSingleLineBreak = 0,
        PreserveSequentialWhitespace = 1,
        CollapseWhitespace = 2
    }

    public sealed class MarkdownSettingsService : INotifyPropertyChanged
    {
        private const string Prefix = "Markdown.";

        private bool _strictMode = true;
        private MarkdownHeadingStyle _headingStyle = MarkdownHeadingStyle.Atx;
        private MarkdownUnorderedListStyle _unorderedListStyle = MarkdownUnorderedListStyle.Dash;
        private MarkdownOrderedListStyle _orderedListStyle = MarkdownOrderedListStyle.Sequential;

        private bool _autoLinks = true;
        private bool _inlineMath;
        private bool _subscript;
        private bool _superscript;
        private bool _highlight;
        private bool _githubStyleAlert = true;
        private bool _diagrams = true;

        private SmartPunctuationMode _smartPunctuationMode = SmartPunctuationMode.ConvertOnInput;
        private bool _smartQuotes;
        private bool _smartDashes;
        private bool _remapUnicodePunctuationOnParse;

        private bool _displayLineNumbersForCodeFences;
        private bool _autoWrapLongLines = true;
        private bool _useShiftTabToAutoIndentSelectedCode;
        private int _codeIndentSize = 4;
        private string _defaultCodeLanguage = string.Empty;
        private DefaultCodeLanguageApplyMode _applyDefaultCodeLanguageWhen = DefaultCodeLanguageApplyMode.WhenAddCodeFencesViaMarkdown;

        private bool _latexMathDelimiters = true;
        private bool _codeBlockMath = true;
        private bool _physicsPackageEnabled;
        private MathEquationNumberingMode _autoNumberingMathEquations = MathEquationNumberingMode.None;
        private MathHtmlExportMode _mathHtmlExportMode = MathHtmlExportMode.RenderedSvg;

        private bool _indentFirstLineOfParagraphs;
        private bool _visibleLineBreaks = true;
        private MarkdownWhitespaceMode _writingWhitespaceMode = MarkdownWhitespaceMode.PreserveSequentialWhitespaceAndSingleLineBreak;
        private MarkdownWhitespaceMode _exportPrintWhitespaceMode = MarkdownWhitespaceMode.PreserveSequentialWhitespaceAndSingleLineBreak;

        public static MarkdownSettingsService Instance { get; } = new MarkdownSettingsService();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler SettingsChanged;

        private MarkdownSettingsService()
        {
            Load();
        }

        public bool StrictMode
        {
            get { return _strictMode; }
            set { SetValue(ref _strictMode, value, "StrictMode"); }
        }

        public MarkdownHeadingStyle HeadingStyle
        {
            get { return _headingStyle; }
            set { SetValue(ref _headingStyle, value, "HeadingStyle"); }
        }

        public int HeadingStyleIndex
        {
            get { return (int)_headingStyle; }
            set { HeadingStyle = (MarkdownHeadingStyle)NormalizeIndex(value, 0, 1); }
        }

        public MarkdownUnorderedListStyle UnorderedListStyle
        {
            get { return _unorderedListStyle; }
            set { SetValue(ref _unorderedListStyle, value, "UnorderedListStyle"); }
        }

        public int UnorderedListStyleIndex
        {
            get { return (int)_unorderedListStyle; }
            set { UnorderedListStyle = (MarkdownUnorderedListStyle)NormalizeIndex(value, 0, 2); }
        }

        public MarkdownOrderedListStyle OrderedListStyle
        {
            get { return _orderedListStyle; }
            set { SetValue(ref _orderedListStyle, value, "OrderedListStyle"); }
        }

        public int OrderedListStyleIndex
        {
            get { return (int)_orderedListStyle; }
            set { OrderedListStyle = (MarkdownOrderedListStyle)NormalizeIndex(value, 0, 1); }
        }

        public bool AutoLinks
        {
            get { return _autoLinks; }
            set { SetValue(ref _autoLinks, value, "AutoLinks"); }
        }

        public bool InlineMath
        {
            get { return _inlineMath; }
            set { SetValue(ref _inlineMath, value, "InlineMath"); }
        }

        public bool Subscript
        {
            get { return _subscript; }
            set { SetValue(ref _subscript, value, "Subscript"); }
        }

        public bool Superscript
        {
            get { return _superscript; }
            set { SetValue(ref _superscript, value, "Superscript"); }
        }

        public bool Highlight
        {
            get { return _highlight; }
            set { SetValue(ref _highlight, value, "Highlight"); }
        }

        public bool GithubStyleAlert
        {
            get { return _githubStyleAlert; }
            set { SetValue(ref _githubStyleAlert, value, "GithubStyleAlert"); }
        }

        public bool Diagrams
        {
            get { return _diagrams; }
            set { SetValue(ref _diagrams, value, "Diagrams"); }
        }

        public SmartPunctuationMode SmartPunctuationMode
        {
            get { return _smartPunctuationMode; }
            set { SetValue(ref _smartPunctuationMode, value, "SmartPunctuationMode"); }
        }

        public int SmartPunctuationModeIndex
        {
            get { return (int)_smartPunctuationMode; }
            set { SmartPunctuationMode = (SmartPunctuationMode)NormalizeIndex(value, 0, 2); }
        }

        public bool SmartQuotes
        {
            get { return _smartQuotes; }
            set { SetValue(ref _smartQuotes, value, "SmartQuotes"); }
        }

        public bool SmartDashes
        {
            get { return _smartDashes; }
            set { SetValue(ref _smartDashes, value, "SmartDashes"); }
        }

        public bool RemapUnicodePunctuationOnParse
        {
            get { return _remapUnicodePunctuationOnParse; }
            set { SetValue(ref _remapUnicodePunctuationOnParse, value, "RemapUnicodePunctuationOnParse"); }
        }

        public bool DisplayLineNumbersForCodeFences
        {
            get { return _displayLineNumbersForCodeFences; }
            set { SetValue(ref _displayLineNumbersForCodeFences, value, "DisplayLineNumbersForCodeFences"); }
        }

        public bool AutoWrapLongLines
        {
            get { return _autoWrapLongLines; }
            set { SetValue(ref _autoWrapLongLines, value, "AutoWrapLongLines"); }
        }

        public bool UseShiftTabToAutoIndentSelectedCode
        {
            get { return _useShiftTabToAutoIndentSelectedCode; }
            set { SetValue(ref _useShiftTabToAutoIndentSelectedCode, value, "UseShiftTabToAutoIndentSelectedCode"); }
        }

        public int CodeIndentSize
        {
            get { return _codeIndentSize; }
            set { SetValue(ref _codeIndentSize, Math.Max(1, Math.Min(8, value)), "CodeIndentSize"); }
        }

        public string DefaultCodeLanguage
        {
            get { return _defaultCodeLanguage; }
            set { SetValue(ref _defaultCodeLanguage, value ?? string.Empty, "DefaultCodeLanguage"); }
        }

        public DefaultCodeLanguageApplyMode ApplyDefaultCodeLanguageWhen
        {
            get { return _applyDefaultCodeLanguageWhen; }
            set { SetValue(ref _applyDefaultCodeLanguageWhen, value, "ApplyDefaultCodeLanguageWhen"); }
        }

        public int ApplyDefaultCodeLanguageWhenIndex
        {
            get { return (int)_applyDefaultCodeLanguageWhen; }
            set { ApplyDefaultCodeLanguageWhen = (DefaultCodeLanguageApplyMode)NormalizeIndex(value, 0, 2); }
        }

        public bool LatexMathDelimiters
        {
            get { return _latexMathDelimiters; }
            set { SetValue(ref _latexMathDelimiters, value, "LatexMathDelimiters"); }
        }

        public bool CodeBlockMath
        {
            get { return _codeBlockMath; }
            set { SetValue(ref _codeBlockMath, value, "CodeBlockMath"); }
        }

        public bool PhysicsPackageEnabled
        {
            get { return _physicsPackageEnabled; }
            set { SetValue(ref _physicsPackageEnabled, value, "PhysicsPackageEnabled"); }
        }

        public MathEquationNumberingMode AutoNumberingMathEquations
        {
            get { return _autoNumberingMathEquations; }
            set { SetValue(ref _autoNumberingMathEquations, value, "AutoNumberingMathEquations"); }
        }

        public int AutoNumberingMathEquationsIndex
        {
            get { return (int)_autoNumberingMathEquations; }
            set { AutoNumberingMathEquations = (MathEquationNumberingMode)NormalizeIndex(value, 0, 1); }
        }

        public MathHtmlExportMode MathHtmlExportMode
        {
            get { return _mathHtmlExportMode; }
            set { SetValue(ref _mathHtmlExportMode, value, "MathHtmlExportMode"); }
        }

        public int MathHtmlExportModeIndex
        {
            get { return (int)_mathHtmlExportMode; }
            set { MathHtmlExportMode = (MathHtmlExportMode)NormalizeIndex(value, 0, 1); }
        }

        public bool IndentFirstLineOfParagraphs
        {
            get { return _indentFirstLineOfParagraphs; }
            set { SetValue(ref _indentFirstLineOfParagraphs, value, "IndentFirstLineOfParagraphs"); }
        }

        public bool VisibleLineBreaks
        {
            get { return _visibleLineBreaks; }
            set { SetValue(ref _visibleLineBreaks, value, "VisibleLineBreaks"); }
        }

        public MarkdownWhitespaceMode WritingWhitespaceMode
        {
            get { return _writingWhitespaceMode; }
            set { SetValue(ref _writingWhitespaceMode, value, "WritingWhitespaceMode"); }
        }

        public int WritingWhitespaceModeIndex
        {
            get { return (int)_writingWhitespaceMode; }
            set { WritingWhitespaceMode = (MarkdownWhitespaceMode)NormalizeIndex(value, 0, 2); }
        }

        public MarkdownWhitespaceMode ExportPrintWhitespaceMode
        {
            get { return _exportPrintWhitespaceMode; }
            set { SetValue(ref _exportPrintWhitespaceMode, value, "ExportPrintWhitespaceMode"); }
        }

        public int ExportPrintWhitespaceModeIndex
        {
            get { return (int)_exportPrintWhitespaceMode; }
            set { ExportPrintWhitespaceMode = (MarkdownWhitespaceMode)NormalizeIndex(value, 0, 2); }
        }

        public bool ShouldUseSoftlineBreakAsHardlineBreak
        {
            get { return _writingWhitespaceMode == MarkdownWhitespaceMode.PreserveSequentialWhitespaceAndSingleLineBreak; }
        }

        public bool ShouldUseSmartyPants
        {
            get
            {
                return _smartPunctuationMode != SmartPunctuationMode.Disabled
                    && (_smartQuotes || _smartDashes || _remapUnicodePunctuationOnParse);
            }
        }

        public bool ShouldUseMathematics
        {
            get { return _inlineMath || _latexMathDelimiters || _codeBlockMath; }
        }

        public string BuildSignature()
        {
            return string.Join("|", new[]
            {
                _strictMode.ToString(),
                _autoLinks.ToString(),
                _inlineMath.ToString(),
                _subscript.ToString(),
                _superscript.ToString(),
                _highlight.ToString(),
                _githubStyleAlert.ToString(),
                _diagrams.ToString(),
                _smartPunctuationMode.ToString(),
                _smartQuotes.ToString(),
                _smartDashes.ToString(),
                _remapUnicodePunctuationOnParse.ToString(),
                _autoWrapLongLines.ToString(),
                _displayLineNumbersForCodeFences.ToString(),
                _codeIndentSize.ToString(),
                _defaultCodeLanguage,
                _applyDefaultCodeLanguageWhen.ToString(),
                _latexMathDelimiters.ToString(),
                _codeBlockMath.ToString(),
                _physicsPackageEnabled.ToString(),
                _indentFirstLineOfParagraphs.ToString(),
                _visibleLineBreaks.ToString(),
                _writingWhitespaceMode.ToString(),
                _exportPrintWhitespaceMode.ToString()
            });
        }

        private void Load()
        {
            _strictMode = ReadBool("StrictMode", _strictMode);
            _headingStyle = (MarkdownHeadingStyle)ReadInt("HeadingStyle", (int)_headingStyle, 0, 1);
            _unorderedListStyle = (MarkdownUnorderedListStyle)ReadInt("UnorderedListStyle", (int)_unorderedListStyle, 0, 2);
            _orderedListStyle = (MarkdownOrderedListStyle)ReadInt("OrderedListStyle", (int)_orderedListStyle, 0, 1);
            _autoLinks = ReadBool("AutoLinks", _autoLinks);
            _inlineMath = ReadBool("InlineMath", _inlineMath);
            _subscript = ReadBool("Subscript", _subscript);
            _superscript = ReadBool("Superscript", _superscript);
            _highlight = ReadBool("Highlight", _highlight);
            _githubStyleAlert = ReadBool("GithubStyleAlert", _githubStyleAlert);
            _diagrams = ReadBool("Diagrams", _diagrams);
            _smartPunctuationMode = (SmartPunctuationMode)ReadInt("SmartPunctuationMode", (int)_smartPunctuationMode, 0, 2);
            _smartQuotes = ReadBool("SmartQuotes", _smartQuotes);
            _smartDashes = ReadBool("SmartDashes", _smartDashes);
            _remapUnicodePunctuationOnParse = ReadBool("RemapUnicodePunctuationOnParse", _remapUnicodePunctuationOnParse);
            _displayLineNumbersForCodeFences = ReadBool("DisplayLineNumbersForCodeFences", _displayLineNumbersForCodeFences);
            _autoWrapLongLines = ReadBool("AutoWrapLongLines", _autoWrapLongLines);
            _useShiftTabToAutoIndentSelectedCode = ReadBool("UseShiftTabToAutoIndentSelectedCode", _useShiftTabToAutoIndentSelectedCode);
            _codeIndentSize = ReadInt("CodeIndentSize", _codeIndentSize, 1, 8);
            _defaultCodeLanguage = ReadString("DefaultCodeLanguage", _defaultCodeLanguage);
            _applyDefaultCodeLanguageWhen = (DefaultCodeLanguageApplyMode)ReadInt("ApplyDefaultCodeLanguageWhen", (int)_applyDefaultCodeLanguageWhen, 0, 2);
            _latexMathDelimiters = ReadBool("LatexMathDelimiters", _latexMathDelimiters);
            _codeBlockMath = ReadBool("CodeBlockMath", _codeBlockMath);
            _physicsPackageEnabled = ReadBool("PhysicsPackageEnabled", _physicsPackageEnabled);
            _autoNumberingMathEquations = (MathEquationNumberingMode)ReadInt("AutoNumberingMathEquations", (int)_autoNumberingMathEquations, 0, 1);
            _mathHtmlExportMode = (MathHtmlExportMode)ReadInt("MathHtmlExportMode", (int)_mathHtmlExportMode, 0, 1);
            _indentFirstLineOfParagraphs = ReadBool("IndentFirstLineOfParagraphs", _indentFirstLineOfParagraphs);
            _visibleLineBreaks = ReadBool("VisibleLineBreaks", _visibleLineBreaks);
            _writingWhitespaceMode = (MarkdownWhitespaceMode)ReadInt("WritingWhitespaceMode", (int)_writingWhitespaceMode, 0, 2);
            _exportPrintWhitespaceMode = (MarkdownWhitespaceMode)ReadInt("ExportPrintWhitespaceMode", (int)_exportPrintWhitespaceMode, 0, 2);
        }

        private void Save()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            settings[Prefix + "StrictMode"] = _strictMode;
            settings[Prefix + "HeadingStyle"] = (int)_headingStyle;
            settings[Prefix + "UnorderedListStyle"] = (int)_unorderedListStyle;
            settings[Prefix + "OrderedListStyle"] = (int)_orderedListStyle;
            settings[Prefix + "AutoLinks"] = _autoLinks;
            settings[Prefix + "InlineMath"] = _inlineMath;
            settings[Prefix + "Subscript"] = _subscript;
            settings[Prefix + "Superscript"] = _superscript;
            settings[Prefix + "Highlight"] = _highlight;
            settings[Prefix + "GithubStyleAlert"] = _githubStyleAlert;
            settings[Prefix + "Diagrams"] = _diagrams;
            settings[Prefix + "SmartPunctuationMode"] = (int)_smartPunctuationMode;
            settings[Prefix + "SmartQuotes"] = _smartQuotes;
            settings[Prefix + "SmartDashes"] = _smartDashes;
            settings[Prefix + "RemapUnicodePunctuationOnParse"] = _remapUnicodePunctuationOnParse;
            settings[Prefix + "DisplayLineNumbersForCodeFences"] = _displayLineNumbersForCodeFences;
            settings[Prefix + "AutoWrapLongLines"] = _autoWrapLongLines;
            settings[Prefix + "UseShiftTabToAutoIndentSelectedCode"] = _useShiftTabToAutoIndentSelectedCode;
            settings[Prefix + "CodeIndentSize"] = _codeIndentSize;
            settings[Prefix + "DefaultCodeLanguage"] = _defaultCodeLanguage ?? string.Empty;
            settings[Prefix + "ApplyDefaultCodeLanguageWhen"] = (int)_applyDefaultCodeLanguageWhen;
            settings[Prefix + "LatexMathDelimiters"] = _latexMathDelimiters;
            settings[Prefix + "CodeBlockMath"] = _codeBlockMath;
            settings[Prefix + "PhysicsPackageEnabled"] = _physicsPackageEnabled;
            settings[Prefix + "AutoNumberingMathEquations"] = (int)_autoNumberingMathEquations;
            settings[Prefix + "MathHtmlExportMode"] = (int)_mathHtmlExportMode;
            settings[Prefix + "IndentFirstLineOfParagraphs"] = _indentFirstLineOfParagraphs;
            settings[Prefix + "VisibleLineBreaks"] = _visibleLineBreaks;
            settings[Prefix + "WritingWhitespaceMode"] = (int)_writingWhitespaceMode;
            settings[Prefix + "ExportPrintWhitespaceMode"] = (int)_exportPrintWhitespaceMode;
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
            RaiseIndexCompanion(propertyName);
            OnSettingsChanged();
        }

        private void RaiseIndexCompanion(string propertyName)
        {
            if (propertyName == "HeadingStyle") OnPropertyChanged("HeadingStyleIndex");
            else if (propertyName == "UnorderedListStyle") OnPropertyChanged("UnorderedListStyleIndex");
            else if (propertyName == "OrderedListStyle") OnPropertyChanged("OrderedListStyleIndex");
            else if (propertyName == "SmartPunctuationMode") OnPropertyChanged("SmartPunctuationModeIndex");
            else if (propertyName == "ApplyDefaultCodeLanguageWhen") OnPropertyChanged("ApplyDefaultCodeLanguageWhenIndex");
            else if (propertyName == "AutoNumberingMathEquations") OnPropertyChanged("AutoNumberingMathEquationsIndex");
            else if (propertyName == "MathHtmlExportMode") OnPropertyChanged("MathHtmlExportModeIndex");
            else if (propertyName == "WritingWhitespaceMode") OnPropertyChanged("WritingWhitespaceModeIndex");
            else if (propertyName == "ExportPrintWhitespaceMode") OnPropertyChanged("ExportPrintWhitespaceModeIndex");

            if (propertyName == "WritingWhitespaceMode") OnPropertyChanged("ShouldUseSoftlineBreakAsHardlineBreak");
            if (propertyName == "InlineMath" || propertyName == "LatexMathDelimiters" || propertyName == "CodeBlockMath")
            {
                OnPropertyChanged("ShouldUseMathematics");
            }
            if (propertyName == "SmartPunctuationMode" || propertyName == "SmartQuotes" || propertyName == "SmartDashes" || propertyName == "RemapUnicodePunctuationOnParse")
            {
                OnPropertyChanged("ShouldUseSmartyPants");
            }
        }

        private static int NormalizeIndex(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
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
