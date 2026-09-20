using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.ViewModels;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Phone.UI.Input;

namespace MetroMarkdownEditor.WindowsPhone
{
    public sealed partial class EditorPage : Page
    {
        #region Fields

        private readonly DispatcherTimer _typingTimer;
        private readonly DispatcherTimer _syntaxTimer;
        private readonly DispatcherTimer _autoSaveTimer;
        private readonly DispatcherTimer _caretTimer;
        private readonly MarkdownRenderService _renderService = new MarkdownRenderService();
        private readonly MarkdownRenderService _backgroundRenderService = new MarkdownRenderService();
        private readonly AutoSaveService _autoSave = AutoSaveService.Instance;

        private bool _isUndoRedoLocked;
        private bool _isAutoPairEdit;
        private bool _suppressTextChanged;
        private bool _editorTextDirty;
        private DocumentViewModel _dirtyEditorDocument;
        private string _lastEditorText = string.Empty;
        private bool _skeletonLoaded;
        private bool _isWebViewReady;
        private bool _pendingRender;
        private bool _isImmersiveMode;
        private EditorViewMode _currentViewMode = EditorViewMode.Write;
        private ElementTheme _lastTheme = ElementTheme.Light;
        private PreviewThemeType _lastPreviewTheme = PreviewThemeType.Grey;
        private INotifyPropertyChanged _themeViewModel;
        private char? _pendingAutoPairCharacter;
        private int _editorRevision;
        private int _previewRequestVersion;
        private System.Threading.CancellationTokenSource _previewCancellation;
        private DateTime _lastInputUtc = DateTime.MinValue;
        private Windows.System.VirtualKey? _lastEditorKey;
        private double _lastPreviewWorkMilliseconds;
        private bool _isPreviewParseRunning;
        private bool _isPageActive;
        private DocumentViewModel _loadedEditorDocument;
        private bool _pendingPreviewRefresh;
        private bool _pendingPreviewCssRefresh;
        private bool _pendingHiddenPreviewRefresh;
        private bool _pendingSyntaxRefresh;
        private ScrollViewer _editorScrollViewer;
        private MarkdownHighlightRange _highlightRange;
        private bool _isPreviewOperationRunning;
        private bool _pendingPreviewDomUpdate;
        private IReadOnlyList<MarkdownBlock> _lastBlocks;

        // 搜索功能状态变量
        private int _lastSearchIndex = -1;
        private string _lastSearchText = string.Empty;
        private int _lastHighlightStart = -1;
        private int _lastHighlightLength = 0;
        private Windows.UI.Xaml.Controls.Primitives.Popup _outlinePopup;

        #endregion

        #region Properties

        private EditorViewModel ViewModel => DataContext as EditorViewModel;

        private ThemeService ThemeSvc =>
            ((ViewModelLocator)App.Current.Resources["Locator"]).Theme;

        #endregion

        #region Constructor

        public EditorPage()
        {
            InitializeComponent();

            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            _typingTimer.Tick += TypingTimer_Tick;

            _syntaxTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _syntaxTimer.Tick += SyntaxTimer_Tick;

            _caretTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _caretTimer.Tick += CaretTimer_Tick;

            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;

            // Register EditorBox Loaded for robust font handling after navigation
            EditorBox.Loaded += EditorBox_Loaded;

            ConfigureAutoSaveTimer();
            ApplyRuntimeEditorSettings();
        }

        #endregion

        #region Navigation

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _isPageActive = true;
            _autoSave.SettingsChanged += OnAutoSaveSettingsChanged;
            MarkdownSettingsService.Instance.SettingsChanged += OnMarkdownOrEditorSettingsChanged;
            EditorSettingsService.Instance.SettingsChanged += OnMarkdownOrEditorSettingsChanged;
            Window.Current.CoreWindow.CharacterReceived += CoreWindow_CharacterReceived;
            if (_editorScrollViewer != null)
            {
                _editorScrollViewer.ViewChanged += OnEditorScrollViewerViewChanged;
            }

            // Register hardware back button
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            // Subscribe to theme changes
            var locator = App.Current.Resources["Locator"] as ViewModelLocator;
            if (locator != null)
            {
                _themeViewModel = locator.Theme;
                if (_themeViewModel != null)
                {
                    _themeViewModel.PropertyChanged += OnThemePropertyChanged;
                }
            }

            if (ViewModel != null)
            {
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;

                var request = e.Parameter as EditorNavigationRequest;

                // Handle navigation request
                if (request != null)
                {
                    switch (request.Mode)
                    {
                        case EditorLaunchMode.New:
                            await ViewModel.CreateNewAsync();
                            break;
                        case EditorLaunchMode.OpenPicker:
                            await ViewModel.OpenFromPickerAsync();
                            break;
                        case EditorLaunchMode.Recent:
                            await ViewModel.OpenRecentAsync(request.RecentFile);
                            break;
                    }
                }

                // Fallback: create untitled if no documents open
                if (!ViewModel.OpenDocuments.Any())
                {
                    await ViewModel.CreateNewAsync();
                }

                // Sync editor text
                SyncEditorText();
                ViewModel.RefreshPreview();
            }

            // Reset view mode
            _currentViewMode = EditorViewMode.Write;
            UpdateViewMode();

            // Render preview
            await RenderPreviewAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            FlushPendingEditorText(refreshPreview: false);
            _isPageActive = false;
            if (_previewCancellation != null) _previewCancellation.Cancel();
            _editorRevision++;
            base.OnNavigatedFrom(e);

            // Unregister hardware back button
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;

            // Unsubscribe from events
            if (_themeViewModel != null)
            {
                _themeViewModel.PropertyChanged -= OnThemePropertyChanged;
            }

            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _autoSave.SettingsChanged -= OnAutoSaveSettingsChanged;
            MarkdownSettingsService.Instance.SettingsChanged -= OnMarkdownOrEditorSettingsChanged;
            EditorSettingsService.Instance.SettingsChanged -= OnMarkdownOrEditorSettingsChanged;
            Window.Current.CoreWindow.CharacterReceived -= CoreWindow_CharacterReceived;
            if (_editorScrollViewer != null)
            {
                _editorScrollViewer.ViewChanged -= OnEditorScrollViewerViewChanged;
            }

            // Stop timers
            _typingTimer.Stop();
            _syntaxTimer.Stop();
            _autoSaveTimer.Stop();
            _caretTimer.Stop();
        }

        public void FlushEditorBufferForSuspension()
        {
            FlushPendingEditorText(refreshPreview: false);
        }

        private async void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;

            // Preserve unsaved untitled files
            FlushPendingEditorText(refreshPreview: false);
            await PreserveUntitledFilesAsync();

            // Navigate to MainPage
            Frame.Navigate(typeof(MainPage));
            Frame.BackStack.Clear();
        }

        private Task PreserveUntitledFilesAsync()
        {
            if (ViewModel == null) return Task.FromResult(0);
            FlushPendingEditorText(refreshPreview: false);

            var recentService = ((ViewModelLocator)App.Current.Resources["Locator"]).Main?.RecentFilesService;
            if (recentService == null) return Task.FromResult(0);

            foreach (var doc in ViewModel.OpenDocuments.ToList())
            {
                // Check if it's unsaved (no file) and has content
                if (doc.File == null && !string.IsNullOrWhiteSpace(doc.Content))
                {
                    // Add to recent files as a temporary entry
                    var name = doc.Title;
                    recentService.AddTemporaryUntitled(name, doc.Content);
                }
            }

            return Task.FromResult(0);
        }

        #endregion

        #region View Mode

        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            // Cycle: Editor <-> Preview (no Split on WP)
            switch (_currentViewMode)
            {
                case EditorViewMode.Write:
                    _currentViewMode = EditorViewMode.Preview;
                    break;
                case EditorViewMode.Preview:
                    _currentViewMode = EditorViewMode.Write;
                    break;
            }

            if (_currentViewMode == EditorViewMode.Preview)
            {
                FlushPendingEditorText(refreshPreview: true);
            }

            UpdateViewMode();
            var _ = RenderPreviewAsync();
        }

        private void UpdateViewMode()
        {
            // Hide all
            EditorContainer.Visibility = Visibility.Collapsed;
            PreviewContainer.Visibility = Visibility.Collapsed;

            switch (_currentViewMode)
            {
                case EditorViewMode.Write:
                    EditorContainer.Visibility = Visibility.Visible;
                    break;
                case EditorViewMode.Preview:
                    PreviewContainer.Visibility = Visibility.Visible;
                    break;
            }
        }

        #endregion

        #region Immersive Mode

        private void ImmersiveButton_Click(object sender, RoutedEventArgs e)
        {
            _isImmersiveMode = !_isImmersiveMode;

            if (_isImmersiveMode)
            {
                // Hide TabBar
                TabBar.Visibility = Visibility.Collapsed;
                // Collapse CommandBar (close it)
                EditorCommandBar.IsOpen = false;
            }
            else
            {
                // Show TabBar
                TabBar.Visibility = Visibility.Visible;
            }
        }

        #endregion

        #region Editor Text Sync

        private void SyncEditorText()
        {
            if (ViewModel?.ActiveDocument == null) return;
            if (EditorBox?.Document == null) return;

            var content = ViewModel.ActiveDocument.Content ?? string.Empty;
            _editorRevision++;
            SetEditorTextWithoutNotification(content);
            _lastEditorText = NormalizeLineEndings(content);
            _editorTextDirty = false;
            _dirtyEditorDocument = null;
            _loadedEditorDocument = ViewModel.ActiveDocument;

            ApplyEditorFormatting();
            ResetUndoRedo();
        }



        /// <summary>
        /// EditorBox 加载完成后，确保文本和格式正确应用
        /// 这是修复从 MainPage 再次打开文件时格式丢失的关键
        /// </summary>
        private void EditorBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (_editorScrollViewer == null)
            {
                _editorScrollViewer = FindScrollViewer(EditorBox);
                if (_editorScrollViewer != null && _isPageActive)
                {
                    _editorScrollViewer.ViewChanged += OnEditorScrollViewerViewChanged;
                }
            }

            if (ViewModel != null && ViewModel.ActiveDocument != null)
            {
                if (!ReferenceEquals(_loadedEditorDocument, ViewModel.ActiveDocument))
                {
                    SyncEditorText();
                }
                else
                {
                    ApplyEditorFormatting();
                }
            }

            _pendingSyntaxRefresh = true;
            _syntaxTimer.Stop();
            _syntaxTimer.Start();
        }

        private void OnEditorScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate) return;
            _pendingSyntaxRefresh = true;
            _syntaxTimer.Stop();
            _syntaxTimer.Start();
        }

        private void SetEditorTextWithoutNotification(string text)
        {
            _suppressTextChanged = true;
            var doc = EditorBox.Document;
            doc.UndoLimit = 0;
            doc.BatchDisplayUpdates();
            try
            {
                _highlightRange = new MarkdownHighlightRange();
                doc.SetText(TextSetOptions.None, text ?? string.Empty);
                ApplyEditorFormatting();
            }
            finally
            {
                doc.ApplyDisplayUpdates();
                doc.UndoLimit = 100;
                _suppressTextChanged = false;
                _pendingAutoPairCharacter = null;
            }
        }

        private static string NormalizeLineEndings(string text)
        {
            return EditorPerformancePolicy.NormalizeText(text);
        }

        /// <summary>
        /// 强制应用编辑器的字体和字号。
        /// RichEditBox 在调用 SetText 后会重置格式，
        /// 该方法用于在每次更新后强制把字体"扳"回来。
        /// </summary>
        private void ApplyEditorFormatting()
        {
            if (EditorBox?.Document == null) return;

            try
            {
                float targetSize = 18f; // 磅值
                string targetFont = "Consolas";

                // XAML already applies the same font to existing text. Keep the
                // document default aligned without reading and formatting the
                // whole backing store after every SetText.
                var defaultFormat = EditorBox.Document.GetDefaultCharacterFormat();
                defaultFormat.Name = targetFont;
                defaultFormat.Size = targetSize;
                EditorBox.Document.SetDefaultCharacterFormat(defaultFormat);
                EditorBox.Document.UndoLimit = 100;
            }
            catch { /* Ignore formatting errors */ }
        }

        #endregion

        #region Editor Events

        private void CoreWindow_CharacterReceived(
            Windows.UI.Core.CoreWindow sender,
            Windows.UI.Core.CharacterReceivedEventArgs args)
        {
            if (EditorBox == null || EditorBox.FocusState == FocusState.Unfocused)
            {
                _pendingAutoPairCharacter = null;
                return;
            }

            var ctrlState = sender.GetKeyState(Windows.System.VirtualKey.Control);
            var isCtrlPressed = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down)
                                == Windows.UI.Core.CoreVirtualKeyStates.Down;
            if (isCtrlPressed || args.KeyCode > char.MaxValue)
            {
                _pendingAutoPairCharacter = null;
                return;
            }

            var character = (char)args.KeyCode;
            _pendingAutoPairCharacter = GetAutoPairClosingText(character, EditorSettingsService.Instance) != null
                ? (char?)character
                : null;
        }

        private void EditorBox_TextChanged(object sender, RoutedEventArgs e)
        {
            HandleTextChanged(EditorBox);
        }



        private void HandleTextChanged(RichEditBox editor)
        {
            if (_isUndoRedoLocked || _suppressTextChanged || _isAutoPairEdit) return;
            if (ViewModel?.ActiveDocument == null) return;
            if (editor?.Document == null) return;

            var autoPairCharacter = _pendingAutoPairCharacter;
            _pendingAutoPairCharacter = null;
            if (autoPairCharacter.HasValue)
            {
                TryApplyAutoPairAtCaret(editor, autoPairCharacter.Value);
            }

            _editorRevision++;
            _editorTextDirty = true;
            _dirtyEditorDocument = ViewModel.ActiveDocument;
            _dirtyEditorDocument.MarkEditorChanged();
            if (_previewCancellation != null) _previewCancellation.Cancel();
            _lastInputUtc = DateTime.UtcNow;
            _typingTimer.Interval = TimeSpan.FromMilliseconds(EditorPerformancePolicy.PreviewDelay(_lastEditorText.Length, _lastPreviewWorkMilliseconds));
            _pendingPreviewRefresh = true;
            _pendingSyntaxRefresh = true;

            _typingTimer.Stop();
            _typingTimer.Start();

            _syntaxTimer.Stop();
            _syntaxTimer.Start();

            if (_autoSave.IsEnabled)
            {
                if (!_autoSaveTimer.IsEnabled) _autoSaveTimer.Start();
            }

            ScheduleCaretCenteringIfNeeded();
        }

        private void ScheduleCaretCenteringIfNeeded()
        {
            var settings = EditorSettingsService.Instance;
            if (!settings.TypewriterFocusModeEnabled || !settings.KeepCaretInMiddleWhenTypewriterModeEnabled) return;

            _caretTimer.Stop();
            _caretTimer.Start();
        }

        private void CaretTimer_Tick(object sender, object e)
        {
            _caretTimer.Stop();
            KeepCaretCenteredIfNeeded();
        }

        private bool TryApplyAutoPairAtCaret(RichEditBox editor, char insertedCharacter)
        {
            var settings = EditorSettingsService.Instance;
            if (!settings.AutoPairBracketsAndQuotes && !settings.AutoPairCommonMarkdownSyntax) return false;

            var selection = editor.Document.Selection;
            var caret = selection.StartPosition;
            if (caret <= 0 || selection.EndPosition != caret) return false;

            string insertedText;
            editor.Document.GetRange(caret - 1, caret).GetText(TextGetOptions.None, out insertedText);
            if (string.IsNullOrEmpty(insertedText) || insertedText[0] != insertedCharacter) return false;

            var closing = GetAutoPairClosingText(insertedCharacter, settings);
            if (closing == null) return false;

            string nextText = string.Empty;
            try
            {
                editor.Document.GetRange(caret, caret + 1).GetText(TextGetOptions.None, out nextText);
            }
            catch
            {
                nextText = string.Empty;
            }
            if (!string.IsNullOrEmpty(nextText) && nextText[0] == closing[0]) return false;

            try
            {
                _isAutoPairEdit = true;
                selection.TypeText(closing);
                selection.SetRange(caret, caret);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _isAutoPairEdit = false;
            }
        }

        private static string GetAutoPairClosingText(char inserted, EditorSettingsService settings)
        {
            if (settings.AutoPairBracketsAndQuotes)
            {
                switch (inserted)
                {
                    case '(':
                        return ")";
                    case '[':
                        return "]";
                    case '{':
                        return "}";
                    case '"':
                        return "\"";
                    case '\'':
                        return "'";
                }
            }

            if (settings.AutoPairCommonMarkdownSyntax)
            {
                switch (inserted)
                {
                    case '*':
                    case '_':
                    case '~':
                    case '`':
                        return inserted.ToString();
                }
            }

            return null;
        }

        private void KeepCaretCenteredIfNeeded()
        {
            var settings = EditorSettingsService.Instance;
            if (!settings.TypewriterFocusModeEnabled || !settings.KeepCaretInMiddleWhenTypewriterModeEnabled) return;

            try
            {
                var scrollViewer = FindScrollViewer(EditorBox);
                if (scrollViewer == null || EditorBox?.Document == null) return;

                Windows.Foundation.Rect rect;
                int hit;
                EditorBox.Document.Selection.GetRect(PointOptions.ClientCoordinates, out rect, out hit);
                if (rect.Height <= 0) return;

                var target = scrollViewer.VerticalOffset + rect.Top - (scrollViewer.ViewportHeight / 2) + rect.Height;
                target = Math.Max(0, Math.Min(target, scrollViewer.ScrollableHeight));
                if (Math.Abs(target - scrollViewer.VerticalOffset) < 1) return;
                scrollViewer.ChangeView(null, target, null, true);
            }
            catch
            {
            }
        }

        private void EditorBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            _lastEditorKey = e.Key;
            EditorBox_InputActivity(sender, e);
            _pendingAutoPairCharacter = null;
            HandleKeyDown(EditorBox, e);
        }



        private void HandleKeyDown(RichEditBox editor, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var ctrlState = Windows.UI.Core.CoreWindow.GetForCurrentThread()
                .GetKeyState(Windows.System.VirtualKey.Control);
            bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

            if (isCtrl && e.Key == Windows.System.VirtualKey.S)
            {
                e.Handled = true;
                var _ = SaveCurrentDocumentAsync();
                return;
            }

            if (isCtrl && e.Key == Windows.System.VirtualKey.Z)
            {
                e.Handled = true;
                Undo();
                return;
            }

            if (isCtrl && e.Key == Windows.System.VirtualKey.Y)
            {
                e.Handled = true;
                Redo();
                return;
            }

            if (isCtrl && e.Key == Windows.System.VirtualKey.C)
            {
                if (TryHandlePlainTextClipboard(cut: false))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (isCtrl && e.Key == Windows.System.VirtualKey.X)
            {
                if (TryHandlePlainTextClipboard(cut: true))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Windows.System.VirtualKey.Tab)
            {
                e.Handled = true;
                editor?.Document?.Selection?.TypeText(new string(' ', MarkdownSettingsService.Instance.CodeIndentSize));
            }
        }

        private async Task SaveCurrentDocumentAsync()
        {
            if (ViewModel == null) return;
            FlushPendingEditorText(refreshPreview: false);
            await ViewModel.SaveAsync();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveCurrentDocumentAsync();
        }

        private bool TryHandlePlainTextClipboard(bool cut)
        {
#if WINDOWS_PHONE_APP
            return false;
#else
            var settings = EditorSettingsService.Instance;
            if (!settings.CopyMarkdownSourceAsPlainText && !settings.CopyCutWholeLinesWhenNoSelection)
            {
                return false;
            }

            var doc = EditorBox.Document;
            if (doc == null) return false;

            string fullText = string.Empty;
            doc.GetText(TextGetOptions.None, out fullText);
            if (fullText == null) fullText = string.Empty;

            var selection = doc.Selection;
            var start = selection.StartPosition;
            var end = selection.EndPosition;
            if (start > end) { var tmp = start; start = end; end = tmp; }

            string textToCopy;
            int replaceStart = start;
            int replaceEnd = end;
            var hasSelection = start != end;

            if (!hasSelection)
            {
                if (!settings.CopyCutWholeLinesWhenNoSelection) return false;

                replaceStart = start;
                while (replaceStart > 0 && fullText[replaceStart - 1] != '\r' && fullText[replaceStart - 1] != '\n') replaceStart--;

                replaceEnd = end;
                while (replaceEnd < fullText.Length && fullText[replaceEnd] != '\r' && fullText[replaceEnd] != '\n') replaceEnd++;
                if (replaceEnd < fullText.Length)
                {
                    replaceEnd++;
                    if (replaceEnd < fullText.Length && fullText[replaceEnd - 1] == '\r' && fullText[replaceEnd] == '\n') replaceEnd++;
                }

                textToCopy = fullText.Substring(replaceStart, Math.Max(0, replaceEnd - replaceStart));
            }
            else
            {
                var range = doc.GetRange(start, end);
                range.GetText(TextGetOptions.None, out textToCopy);
            }

            if (string.IsNullOrEmpty(textToCopy)) return false;

            var package = new DataPackage();
            package.SetText(settings.NormalizeLineEndings(textToCopy.TrimEnd('\0')));
            Clipboard.SetContent(package);

            if (cut)
            {
                var range = doc.GetRange(replaceStart, replaceEnd);
                range.SetText(TextSetOptions.None, string.Empty);
            }

            return true;
#endif
        }

        #endregion

        #region Undo/Redo

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            Undo();
        }

        private void RedoButton_Click(object sender, RoutedEventArgs e)
        {
            Redo();
        }

        private void ResetUndoRedo()
        {
            if (EditorBox?.Document == null) return;
            EditorBox.Document.UndoLimit = 0;
            EditorBox.Document.UndoLimit = 100;
        }

        private void Undo()
        {
            ApplyNativeUndoRedo(redo: false);
        }

        private void Redo()
        {
            ApplyNativeUndoRedo(redo: true);
        }

        private string GetNormalizedEditorText()
        {
            if (EditorBox?.Document == null) return string.Empty;
            string text;
            EditorBox.Document.GetText(TextGetOptions.None, out text);
            return NormalizeRichEditText(text);
        }

        private static string NormalizeRichEditText(string text)
        {
            return EditorPerformancePolicy.NormalizeText(text, richEditTerminalMarker: true);
        }

        private string FlushPendingEditorText(bool refreshPreview)
        {
            if (!_editorTextDirty || _dirtyEditorDocument == null || EditorBox?.Document == null)
            {
                if (refreshPreview && ViewModel != null) ViewModel.RefreshPreview();
                return _lastEditorText ?? string.Empty;
            }

            var text = GetNormalizedEditorText();
            if (ReferenceEquals(_dirtyEditorDocument, ViewModel?.ActiveDocument))
            {
                ViewModel.SetContentFromEditor(text);
                if (refreshPreview)
                {
                    ViewModel.RefreshPreview();
                }
            }
            else
            {
                _dirtyEditorDocument.CommitEditorText(text);
            }

            _lastEditorText = text;
            _editorTextDirty = false;
            _dirtyEditorDocument = null;
            return text;
        }

        private void ApplyNativeUndoRedo(bool redo)
        {
            if (EditorBox?.Document == null) return;

            _typingTimer.Stop();
            _syntaxTimer.Stop();
            FlushPendingEditorText(refreshPreview: false);

            var changedText = false;
            string undoText = null;
            var before = _lastEditorText ?? string.Empty;
            _isUndoRedoLocked = true;
            try
            {
                for (var attempt = 0; attempt < 16; attempt++)
                {
                    if (redo)
                    {
                        if (!EditorBox.Document.CanRedo()) break;
                        EditorBox.Document.Redo();
                    }
                    else
                    {
                        if (!EditorBox.Document.CanUndo()) break;
                        EditorBox.Document.Undo();
                    }

                    var current = GetNormalizedEditorText();
                    if (!string.Equals(current, before, StringComparison.Ordinal))
                    {
                        undoText = current;
                        changedText = true;
                        break;
                    }
                }
            }
            finally
            {
                _isUndoRedoLocked = false;
            }

            if (!changedText) return;

            _editorRevision++;
            _editorTextDirty = true;
            _dirtyEditorDocument = ViewModel?.ActiveDocument;
            if (_dirtyEditorDocument != null) _dirtyEditorDocument.CommitEditorText(undoText);
            _lastEditorText = undoText;
            _editorTextDirty = false;
            _dirtyEditorDocument = null;
            _pendingSyntaxRefresh = false;
            ViewModel?.RefreshPreview();
        }

        #endregion

        #region Timers

        private void EditorBox_InputActivity(object sender, RoutedEventArgs e)
        {
            if (!_suppressTextChanged && EditorBox.FocusState != FocusState.Unfocused)
                _lastInputUtc = DateTime.UtcNow;
        }

        private void EditorBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isPageActive && _pendingSyntaxRefresh) _syntaxTimer.Start();
        }

        private bool IsEditorKeyHeld()
        {
            return _lastEditorKey.HasValue && EditorBox.FocusState != FocusState.Unfocused
                && (Windows.UI.Core.CoreWindow.GetForCurrentThread().GetKeyState(_lastEditorKey.Value)
                    & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        }

        private void SyntaxTimer_Tick(object sender, object e)
        {
            _syntaxTimer.Stop();
            if (EditorBox.FocusState != FocusState.Unfocused) return;
            if (!_pendingSyntaxRefresh) return;
            if (IsEditorKeyHeld() || (DateTime.UtcNow - _lastInputUtc).TotalMilliseconds < 650)
            {
                _syntaxTimer.Start();
                return;
            }
            if (_isPreviewParseRunning)
            {
                _syntaxTimer.Start();
                return;
            }

            _pendingSyntaxRefresh = false;
            HighlightMarkdownSyntax();
        }

        private async void TypingTimer_Tick(object sender, object e)
        {
            _typingTimer.Stop();
            if (!_isPageActive || _isPreviewParseRunning || !_pendingPreviewRefresh || ViewModel == null) return;
            // Hidden previews do not require a native full-document snapshot.
            if (!(_currentViewMode == EditorViewMode.Preview) && !_pendingHiddenPreviewRefresh) return;
            if (IsEditorKeyHeld())
            {
                _typingTimer.Interval = TimeSpan.FromMilliseconds(180);
                _typingTimer.Start();
                return;
            }
            if (_editorTextDirty)
            {
                var delay = EditorPerformancePolicy.PreviewDelay(_lastEditorText.Length, _lastPreviewWorkMilliseconds);
                var remaining = delay - (DateTime.UtcNow - _lastInputUtc).TotalMilliseconds;
                if (remaining > 0)
                {
                    _typingTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, remaining));
                    _typingTimer.Start();
                    return;
                }
            }

            var document = ViewModel.ActiveDocument;
            if (document == null) return;
            var revision = _editorRevision;
            var requestVersion = _previewRequestVersion;
            _pendingPreviewRefresh = false;
            var refreshCss = _pendingPreviewCssRefresh;
            _pendingPreviewCssRefresh = false;
            _pendingHiddenPreviewRefresh = false;
            var markdown = FlushPendingEditorText(refreshPreview: false);
            _isPreviewParseRunning = true;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (_previewCancellation != null) _previewCancellation.Dispose();
                _previewCancellation = new System.Threading.CancellationTokenSource();
                var rendered = await ViewModel.RenderPreviewDocumentAsync(_backgroundRenderService, markdown, _previewCancellation.Token);
                if (rendered != null && _isPageActive && revision == _editorRevision && requestVersion == _previewRequestVersion
                    && ReferenceEquals(document, ViewModel.ActiveDocument)
                    && string.Equals(document.Content ?? string.Empty, markdown, StringComparison.Ordinal))
                {
                    ViewModel.ApplyPreviewResult(rendered, refreshCss: refreshCss);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Background preview render failed: " + ex.Message);
            }
            finally
            {
                _lastPreviewWorkMilliseconds = watch.Elapsed.TotalMilliseconds;
                _isPreviewParseRunning = false;
                if (_pendingPreviewRefresh && _isPageActive)
                {
                    _pendingPreviewCssRefresh |= refreshCss;
                    _typingTimer.Interval = TimeSpan.FromMilliseconds(EditorPerformancePolicy.PreviewDelay(_lastEditorText.Length, _lastPreviewWorkMilliseconds));
                    _typingTimer.Start();
                }
            }
        }

        private void QueuePreviewRefresh(bool refreshCss)
        {
            if (ViewModel?.ActiveDocument == null) return;

            _previewRequestVersion++;
            if (_previewCancellation != null) _previewCancellation.Cancel();
            _pendingPreviewRefresh = true;
            _pendingPreviewCssRefresh = _pendingPreviewCssRefresh || refreshCss;
            _pendingHiddenPreviewRefresh = true;

            if (!_editorTextDirty && !_isPreviewParseRunning)
            {
                TypingTimer_Tick(null, null);
            }
            else if (!_isPreviewParseRunning)
            {
                _typingTimer.Stop();
                _typingTimer.Start();
            }
        }

        private async void AutoSaveTimer_Tick(object sender, object e)
        {
            _autoSaveTimer.Stop();
            if (!_autoSave.IsEnabled || ViewModel == null || !_isPageActive) return;
            try
            {
                FlushPendingEditorText(refreshPreview: false);
                await ViewModel.AutoSaveAsync();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Auto save failed: " + ex.Message); }
            finally { if (_isPageActive) ConfigureAutoSaveTimer(); }
        }

        private void OnAutoSaveSettingsChanged(object sender, EventArgs e)
        {
            ConfigureAutoSaveTimer();
        }

        private void ConfigureAutoSaveTimer()
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(_autoSave.FrequencyMinutes);
            if (_autoSave.IsEnabled)
            {
                _autoSaveTimer.Start();
            }
        }

        #endregion

        #region Preview Rendering

        private async Task RenderPreviewAsync()
        {
            if (ViewModel == null) return;

            var targetWebView = _currentViewMode == EditorViewMode.Preview ? PreviewWebView : null;
            if (targetWebView == null) return;
            if (_isPreviewOperationRunning)
            {
                _pendingPreviewDomUpdate = true;
                return;
            }

            _isPreviewOperationRunning = true;
            _pendingPreviewDomUpdate = false;
            try
            {
                var theme = GetCurrentTheme();
                var previewTheme = ThemeSvc != null ? ThemeSvc.PreviewTheme : PreviewThemeType.Grey;
                ViewModel.SetTheme(theme);

                if (!_skeletonLoaded || theme != _lastTheme || previewTheme != _lastPreviewTheme || _renderService.NeedsLibraries(ViewModel.PreviewBlocks))
                {
                    _isWebViewReady = false;
                    _lastBlocks = null;
                    await _renderService.LoadSkeletonAsync(targetWebView, BuildPhoneCss(), theme, ViewModel.PreviewBlocks);
                    _skeletonLoaded = true;
                    _lastTheme = theme;
                    _lastPreviewTheme = previewTheme;
                }

                if (!_isWebViewReady)
                {
                    _pendingRender = true;
                    return;
                }

                var nextBlocks = ViewModel.PreviewBlocks ?? new List<MarkdownBlock>();
                if (ReferenceEquals(_lastBlocks, nextBlocks)) return;
                var applied = await _renderService.UpdateBlocksAsync(targetWebView, _lastBlocks, nextBlocks);
                // Track what actually reached the DOM, even if a newer result queued meanwhile.
                _lastBlocks = applied ? nextBlocks : null;

            }
            finally
            {
                _isPreviewOperationRunning = false;
                if (_pendingPreviewDomUpdate && _isPageActive)
                {
                    _pendingPreviewDomUpdate = false;
                    var _ = RenderPreviewAsync();
                }
            }
        }

        private void PreviewWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            HandleWebViewNavigationCompleted();
        }

        private void SplitPreviewWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            HandleWebViewNavigationCompleted();
        }

        private void HandleWebViewNavigationCompleted()
        {
            _isWebViewReady = true;
            if (_pendingRender)
            {
                _pendingRender = false;
                var _ = RenderPreviewAsync();
            }
        }

        private ElementTheme GetCurrentTheme()
        {
            return ThemeSvc?.IsDarkTheme == true ? ElementTheme.Dark : ElementTheme.Light;
        }

        private string BuildPhoneCss()
        {
            var isDark = GetCurrentTheme() == ElementTheme.Dark;
            var background = ThemeSvc != null
                ? ThemeSvc.GetPreviewBackground()
                : (isDark ? "#1d1d1d" : "#ffffff");
            var foreground = isDark ? "#f3f3f3" : "#1a1a1a";

            return @"<style>
                html, body {
                    background-color: " + background + @";
                    min-height: 100%;
                }
                body {
                    font-family: 'Segoe UI', sans-serif;
                    font-size: 16px;
                    line-height: 1.6;
                    padding: 12px 16px;
                    margin: 0;
                    color: " + foreground + @";
                    background-color: " + background + @";
                    word-wrap: break-word;
                    overflow-x: hidden;
                }
                img { max-width: 100%; height: auto; }
                pre { overflow-x: auto; padding: 12px; font-size: 14px; }
                code { font-family: Consolas, monospace; font-size: 14px; }
                table { border-collapse: collapse; width: 100%; }
                th, td { border: 1px solid #444; padding: 8px; }
            </style>";
        }

        #endregion

        #region Syntax Highlighting

        private void HighlightMarkdownSyntax()
        {
            if (EditorBox.FocusState != FocusState.Unfocused)
            {
                _pendingSyntaxRefresh = true;
                return;
            }
            var wasSuppressed = _suppressTextChanged;
            _suppressTextChanged = true;
            try { _highlightRange = MarkdownHighlightingHelper.HighlightVisible(EditorBox, _highlightRange); }
            finally { _suppressTextChanged = wasSuppressed; }
        }

        #endregion

        #region ViewModel Events

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "PreviewContent")
            {
                var _ = RenderPreviewAsync();
            }
            else if (e.PropertyName == "ActiveDocument")
            {
                FlushPendingEditorText(refreshPreview: false);
                SyncEditorText();
            }
            else if (e.PropertyName == EditorViewModel.PreviewRefreshRequestedPropertyName)
            {
                QueuePreviewRefresh(refreshCss: true);
            }
        }

        private void OnThemePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                _skeletonLoaded = false;
                HighlightMarkdownSyntax();
                var __ = RenderPreviewAsync();
            });
        }

        private void OnMarkdownOrEditorSettingsChanged(object sender, EventArgs e)
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                FlushPendingEditorText(refreshPreview: false);
                ApplyRuntimeEditorSettings();
                _skeletonLoaded = false;
                HighlightMarkdownSyntax();
                if (ViewModel != null)
                {
                    ViewModel.RefreshPreview();
                }
                var __ = RenderPreviewAsync();
            });
        }

        private void ApplyRuntimeEditorSettings()
        {
            if (EditorBox == null) return;
            EditorBox.IsSpellCheckEnabled = EditorSettingsService.Instance.IsSpellCheckEnabled;
        }

        #endregion

        #region Export

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as AppBarButton;
            if (button == null || ViewModel == null) return;

            var format = button.Tag as string;
            if (string.IsNullOrEmpty(format)) return;

            FlushPendingEditorText(refreshPreview: false);
            await ViewModel.ExportAsync(format);
        }

        #endregion

        #region Outline

        private void OutlineButton_Click(object sender, RoutedEventArgs e)
        {
            ShowOutlinePopup();
        }

        private void ShowOutlinePopup()
        {
            FlushPendingEditorText(refreshPreview: true);

            if (_outlinePopup != null && _outlinePopup.IsOpen)
            {
                _outlinePopup.IsOpen = false;
                return;
            }

            var items = ViewModel != null ? ViewModel.OutlineItems : null;
            var bounds = Window.Current.Bounds;
            var panel = new Grid
            {
                Width = bounds.Width,
                MaxHeight = Math.Max(260, bounds.Height * 0.72),
                Background = (SolidColorBrush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
            };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid { Margin = new Thickness(16, 14, 10, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Outline",
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);

            var close = new Button
            {
                Content = "x",
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            close.Click += (s, args) =>
            {
                if (_outlinePopup != null)
                {
                    _outlinePopup.IsOpen = false;
                }
            };
            Grid.SetColumn(close, 1);

            header.Children.Add(title);
            header.Children.Add(close);
            Grid.SetRow(header, 0);
            panel.Children.Add(header);

            if (items == null || items.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "No headings",
                    FontSize = 17,
                    Opacity = 0.7,
                    Margin = new Thickness(16, 24, 16, 24)
                };
                Grid.SetRow(empty, 1);
                panel.Children.Add(empty);
            }
            else
            {
                var list = new ListView
                {
                    ItemsSource = items,
                    IsItemClickEnabled = true,
                    SelectionMode = ListViewSelectionMode.None,
                    Margin = new Thickness(4, 0, 4, 10),
                    ItemTemplate = BuildOutlineItemTemplate()
                };
                list.ItemClick += OutlineList_ItemClick;
                Grid.SetRow(list, 1);
                panel.Children.Add(list);
            }

            _outlinePopup = new Windows.UI.Xaml.Controls.Primitives.Popup
            {
                Child = new Border
                {
                    Child = panel,
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1, 1, 1, 0)
                },
                IsLightDismissEnabled = true
            };
            _outlinePopup.HorizontalOffset = 0;
            _outlinePopup.VerticalOffset = Math.Max(0, bounds.Height - panel.MaxHeight - 72);
            _outlinePopup.IsOpen = true;
        }

        private static DataTemplate BuildOutlineItemTemplate()
        {
            const string template =
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<Grid Padding=\"{Binding Indent}\" MinHeight=\"44\">" +
                "<TextBlock Text=\"{Binding Title}\" FontSize=\"17\" TextTrimming=\"CharacterEllipsis\" VerticalAlignment=\"Center\"/>" +
                "</Grid>" +
                "</DataTemplate>";
            return (DataTemplate)XamlReader.Load(template);
        }

        private async void OutlineList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as MarkdownOutlineItem;
            if (item == null)
            {
                return;
            }

            if (_outlinePopup != null)
            {
                _outlinePopup.IsOpen = false;
            }

            await NavigateToOutlineItemAsync(item);
        }

        private async Task NavigateToOutlineItemAsync(MarkdownOutlineItem item)
        {
            FlushPendingEditorText(refreshPreview: true);

            if (item == null)
            {
                return;
            }

            if (_currentViewMode == EditorViewMode.Write)
            {
                NavigateEditorToLine(item.Line);
                return;
            }

            await RenderPreviewAsync();
            await NavigatePreviewToOutlineItemAsync(item);
        }

        private void NavigateEditorToLine(int line)
        {
            if (EditorBox == null || EditorBox.Document == null)
            {
                return;
            }

            var text = GetNormalizedEditorText();
            var position = GetLineStartPosition(text, line);
            try
            {
                EditorBox.Focus(FocusState.Programmatic);
                var range = EditorBox.Document.GetRange(position, position);
                EditorBox.Document.Selection.SetRange(position, position);
                range.ScrollIntoView(PointOptions.Start);
            }
            catch
            {
            }
        }

        private static int GetLineStartPosition(string text, int line)
        {
            if (line <= 0 || string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int position = 0;
            int currentLine = 0;
            while (position < text.Length && currentLine < line)
            {
                if (text[position] == '\n')
                {
                    currentLine++;
                }
                position++;
            }

            return Math.Min(position, text.Length);
        }

        private async Task NavigatePreviewToOutlineItemAsync(MarkdownOutlineItem item)
        {
            if (PreviewWebView == null || !_isWebViewReady || item == null)
            {
                return;
            }

            try
            {
                var escaped = EscapeJavaScriptString(item.AnchorId);
                var block = item.BlockIndex.ToString();
                var script = "var el=document.querySelector('[data-block=\"" + block + "\"]');" +
                             "if(!el){ el=document.getElementById('" + escaped + "'); }" +
                             "if(el){ el.scrollIntoView(true); 'ok'; } else { 'missing'; }";
                await PreviewWebView.InvokeScriptAsync("eval", new[] { script });
            }
            catch
            {
            }
        }

        private static string EscapeJavaScriptString(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "")
                .Replace("\n", "");
        }

        #endregion

        #region Search

        /// <summary>
        /// 搜索按钮点击事件
        /// </summary>
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSearchDialog();
        }

        /// <summary>
        /// 显示搜索对话框
        /// </summary>
        private void ShowSearchDialog()
        {
            // 创建搜索弹窗内容
            var searchBox = new TextBox
            {
                PlaceholderText = "Search...",
                Text = _lastSearchText,
                Width = 260,
                Margin = new Thickness(0, 0, 0, 12)
            };
            searchBox.SelectAll();

            var findPrevButton = new Button
            {
                Content = "← Find Previous",
                Width = 125,
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 12
            };

            var findNextButton = new Button
            {
                Content = "Find Next →",
                Width = 125,
                FontSize = 12
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            buttonPanel.Children.Add(findPrevButton);
            buttonPanel.Children.Add(findNextButton);

            var contentPanel = new StackPanel();

            // 标题栏 (包含标题和关闭按钮)
            var titlePanel = new Grid();
            titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = "Search",
                FontSize = 18,
                FontWeight = Windows.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 0);

            var closeButton = new Button
            {
                Content = "✕",
                FontSize = 12,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            Grid.SetColumn(closeButton, 1);

            titlePanel.Children.Add(titleBlock);
            titlePanel.Children.Add(closeButton);
            titlePanel.Margin = new Thickness(0, 0, 0, 12);

            contentPanel.Children.Add(titlePanel);
            contentPanel.Children.Add(searchBox);
            contentPanel.Children.Add(buttonPanel);

            // 使用 Border 包装 StackPanel 以支持 Padding 和 Border
            var contentBorder = new Border
            {
                Padding = new Thickness(16),
                Background = (SolidColorBrush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"],
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                Child = contentPanel
            };

            var popup = new Windows.UI.Xaml.Controls.Primitives.Popup
            {
                Child = contentBorder,
                IsLightDismissEnabled = false  // 禁用点击外部关闭
            };

            // 计算位置: 水平居中, 垂直位于 37.8% 处
            var windowBounds = Window.Current.Bounds;
            popup.HorizontalOffset = (windowBounds.Width - 300) / 2;
            popup.VerticalOffset = windowBounds.Height * 0.378;

            // 关闭按钮事件
            closeButton.Click += (s, args) =>
            {
                _lastSearchText = searchBox.Text;
                popup.IsOpen = false;
            };

            // 按钮事件 - 不关闭对话框
            findNextButton.Click += (s, args) =>
            {
                _lastSearchText = searchBox.Text;
                FindAndSelect(searchBox.Text, findNext: true);
            };

            findPrevButton.Click += (s, args) =>
            {
                _lastSearchText = searchBox.Text;
                FindAndSelect(searchBox.Text, findNext: false);
            };

            // 支持 Enter 键触发向下查找, Escape 关闭
            searchBox.KeyDown += (s, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                {
                    _lastSearchText = searchBox.Text;
                    FindAndSelect(searchBox.Text, findNext: true);
                    args.Handled = true;
                }
                else if (args.Key == Windows.System.VirtualKey.Escape)
                {
                    _lastSearchText = searchBox.Text;
                    popup.IsOpen = false;
                    args.Handled = true;
                }
            };

            popup.IsOpen = true;
            searchBox.Focus(FocusState.Programmatic);
        }


        /// <summary>
        /// 在编辑器或预览中查找并选中/高亮文本
        /// </summary>
        private void FindAndSelect(string searchText, bool findNext)
        {
            if (string.IsNullOrEmpty(searchText)) return;

            // 根据当前视图模式决定搜索目标
            if (_currentViewMode == EditorViewMode.Preview)
            {
                // 预览模式：只在 WebView 中搜索
                FindInPreview(searchText, findNext);
            }
            else if (_currentViewMode == EditorViewMode.Split)
            {
                // 分屏模式：两边都搜索
                FindInEditor(searchText, findNext);
                FindInPreview(searchText, findNext);
            }
            else
            {
                // 编辑模式：只在 RichEditBox 中搜索
                FindInEditor(searchText, findNext);
            }
        }

        /// <summary>
        /// 在编辑器中查找并选中文本
        /// </summary>
        private void FindInEditor(string searchText, bool findNext)
        {
            if (EditorBox?.Document == null) return;

            string fullText;
            EditorBox.Document.GetText(TextGetOptions.None, out fullText);
            if (string.IsNullOrEmpty(fullText)) return;

            int startIndex;
            int foundIndex = -1;

            if (findNext)
            {
                // 向下查找
                startIndex = _lastSearchIndex >= 0 ? _lastSearchIndex + 1 : 0;
                if (startIndex >= fullText.Length) startIndex = 0;
                
                foundIndex = fullText.IndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase);
                
                // 如果没找到，从头开始找
                if (foundIndex < 0 && startIndex > 0)
                {
                    foundIndex = fullText.IndexOf(searchText, 0, StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                // 向上查找
                startIndex = _lastSearchIndex > 0 ? _lastSearchIndex - 1 : fullText.Length - 1;
                if (startIndex < 0) startIndex = fullText.Length - 1;
                
                foundIndex = fullText.LastIndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase);
                
                // 如果没找到，从尾开始找
                if (foundIndex < 0 && startIndex < fullText.Length - 1)
                {
                    foundIndex = fullText.LastIndexOf(searchText, fullText.Length - 1, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (foundIndex >= 0)
            {
                // 清除上一次的高亮
                if (_lastHighlightStart >= 0 && _lastHighlightLength > 0)
                {
                    try
                    {
                        var oldRange = EditorBox.Document.GetRange(_lastHighlightStart, _lastHighlightStart + _lastHighlightLength);
                        oldRange.CharacterFormat.BackgroundColor = Colors.Transparent;
                    }
                    catch { /* 忽略索引越界 */ }
                }
                
                _lastSearchIndex = foundIndex;
                _lastHighlightStart = foundIndex;
                _lastHighlightLength = searchText.Length;
                
                var range = EditorBox.Document.GetRange(foundIndex, foundIndex + searchText.Length);
                
                // 高亮当前匹配的文本（黄色背景，黑色文字）
                range.CharacterFormat.BackgroundColor = Colors.Yellow;
                range.CharacterFormat.ForegroundColor = Colors.Black;
                
                range.ScrollIntoView(PointOptions.Start);
                EditorBox.Document.Selection.SetRange(foundIndex, foundIndex + searchText.Length);
            }
        }

        /// <summary>
        /// 在预览 WebView 中查找并高亮文本
        /// </summary>
        private async void FindInPreview(string searchText, bool findNext)
        {
            if (PreviewWebView == null || !_isWebViewReady) return;

            try
            {
                // 使用更可靠的 DOM 遍历方法查找并高亮文本
                var escapedText = searchText.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", "");
                var backwards = findNext ? "false" : "true";
                var script = string.Format(
                    @"(function() {{
                        // 清除所有之前的高亮
                        var highlights = document.querySelectorAll('.search-highlight');
                        for (var i = 0; i < highlights.length; i++) {{
                            var h = highlights[i];
                            var parent = h.parentNode;
                            parent.replaceChild(document.createTextNode(h.textContent), h);
                            parent.normalize();
                        }}
                        
                        // 使用 window.find 定位文本
                        var found = window.find('{0}', false, {1}, true);
                        
                        if (found) {{
                            var sel = window.getSelection();
                            if (sel && sel.rangeCount > 0) {{
                                var range = sel.getRangeAt(0);
                                
                                // 创建高亮 span
                                var span = document.createElement('span');
                                span.className = 'search-highlight';
                                span.style.cssText = 'background-color: #FFFF00 !important; color: #000000 !important; padding: 2px; border-radius: 2px;';
                                
                                try {{
                                    range.surroundContents(span);
                                    span.scrollIntoView({{ behavior: 'smooth', block: 'center' }});
                                }} catch(e) {{
                                    try {{
                                        var frag = range.extractContents();
                                        span.appendChild(frag);
                                        range.insertNode(span);
                                        span.scrollIntoView({{ behavior: 'smooth', block: 'center' }});
                                    }} catch(e2) {{}}
                                }}
                            }}
                        }}
                        return found ? 'found' : 'notfound';
                    }})()",
                    escapedText,
                    backwards);

                await PreviewWebView.InvokeScriptAsync("eval", new[] { script });
            }
            catch
            {
                // 忽略脚本执行错误
            }
        }

        private ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            var scrollViewer = root as ScrollViewer;
            if (scrollViewer != null) return scrollViewer;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }

            return null;
        }

        #endregion
    }
}
