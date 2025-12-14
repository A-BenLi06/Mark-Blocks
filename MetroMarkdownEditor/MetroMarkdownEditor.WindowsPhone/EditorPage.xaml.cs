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
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Input;

namespace MetroMarkdownEditor.WindowsPhone
{
    public sealed partial class EditorPage : Page
    {
        private readonly DispatcherTimer _typingTimer;
        private readonly MarkdownRenderService _renderService = new MarkdownRenderService();
        private readonly DispatcherTimer _undoTimer;
        private readonly Stack<string> _undoStack = new Stack<string>();
        private readonly Stack<string> _redoStack = new Stack<string>();
        private readonly DispatcherTimer _autoSaveTimer;
        private readonly AutoSaveService _autoSave = AutoSaveService.Instance;
        private bool _isUndoRedoLocked;
        private string _lastEditorText = string.Empty;
        private IReadOnlyList<MarkdownBlock> _lastBlocks = new List<MarkdownBlock>();

        private bool _skeletonLoaded;
        private bool _isWebViewReady;
        private bool _pendingRender;
        private ElementTheme _lastTheme = ElementTheme.Light;
        private INotifyPropertyChanged _themeViewModel;

        private EditorViewModel ViewModel
        {
            get { return DataContext as EditorViewModel; }
        }

        public EditorPage()
        {
            InitializeComponent();
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _typingTimer.Tick += TypingTimer_Tick;
            _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _undoTimer.Tick += UndoTimer_Tick;
            _autoSave.SettingsChanged += OnAutoSaveSettingsChanged;
            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            PreviewWebView.NavigationCompleted += PreviewWebView_NavigationCompleted;
            ApplyEditorStyle();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var locator = App.Current.Resources["Locator"] as ViewModelLocator;
            if (locator != null)
            {
                _themeViewModel = locator.Theme;
                _themeViewModel.PropertyChanged += OnThemeViewModelPropertyChanged;
            }

            if (ViewModel != null)
            {
                // 鍏堣В缁戯紝闃叉澶氭杩涘叆椤甸潰閲嶅缁戝畾瀵艰嚧鍐呭瓨娉勬紡鎴栧娆¤Е鍙?                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            var request = e.Parameter as EditorNavigationRequest;
            if (ViewModel != null)
            {
                // 1. 鏍规嵁涓嶅悓妯″紡鍒濆鍖栨暟鎹?                if (request != null)
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

                // 2. 鍏滃簳锛氬鏋滄病鏈夋墦寮€鐨勬枃妗ｏ紝灏卞垵濮嬪寲涓€涓柊鐨?                if (!ViewModel.OpenDocuments.Any())
                {
                    await ViewModel.InitializeAsync();
                }

                // 3. 鍚屾鏂囧瓧鍐呭鍒扮紪杈戝櫒
                SyncEditorText();
                ResetUndoRedo();
            }

            // 4. 娓叉煋棰勮
            await RenderPreviewAsync();

            // 5. 銆愭渶鍚庝竴閬撻槻绾裤€戝己鍒跺簲鐢ㄥぇ瀛椾綋鏍煎紡
            // 鏀惧湪鏈€鍚庢墽琛岋紝纭繚瑕嗙洊鎺変箣鍓嶆墍鏈夋搷浣滃彲鑳藉甫鏉ョ殑閲嶇疆
            ApplyEditorFormatting();
        }

        // METHOD: Force apply font styling to the RichEditBox Document.
        // ISSUE: RichEditBox often ignores XAML FontSize/FontFamily properties after text is loaded.
        // LOGIC:
        // 1. Get the 'ITextCharacterFormat' from 'EditorBox.Document.GetDefaultCharacterFormat()'.
        // 2. Set the 'Name' to "Consolas".
        // 3. Set the 'Size' to 22 (Note: RichEditBox uses Points, not Pixels, so this value needs to be explicitly set).
        // 4. Apply the format back using 'EditorBox.Document.SetDefaultCharacterFormat(format)'.
        // 5. Also apply this format to the current selection ('EditorBox.Document.Selection.CharacterFormat = format') to update existing text immediately.
        private void ApplyEditorFormatting()
        {
            if (EditorBox == null || EditorBox.Document == null) return;

            var format = EditorBox.Document.GetDefaultCharacterFormat();
            format.Name = "Consolas";
            format.Size = 16; // Set to 21 points for better readability on Windows Phone
            EditorBox.Document.SetDefaultCharacterFormat(format);
            EditorBox.Document.Selection.CharacterFormat = format;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_themeViewModel != null)
            {
                _themeViewModel.PropertyChanged -= OnThemeViewModelPropertyChanged;
            }

            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _autoSave.SettingsChanged -= OnAutoSaveSettingsChanged;
            _autoSaveTimer.Stop();

            base.OnNavigatedFrom(e);
        }

        private void OnThemeViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                HighlightMarkdownSyntax();
                var __ = RenderPreviewAsync();
            });
        }

        private void EditorBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var ctrlState = Windows.UI.Core.CoreWindow.GetForCurrentThread().GetKeyState(Windows.System.VirtualKey.Control);
            bool isCtrlPressed = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            if (isCtrlPressed && e.Key == Windows.System.VirtualKey.S)
            {
                e.Handled = true;
                var _ = ViewModel.SaveAsync();
                return;
            }

            if (isCtrlPressed && e.Key == Windows.System.VirtualKey.Z)
            {
                e.Handled = true;
                Undo();
                return;
            }
            if (isCtrlPressed && e.Key == Windows.System.VirtualKey.Y)
            {
                e.Handled = true;
                Redo();
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Tab)
            {
                e.Handled = true;

                var shiftState = Windows.UI.Core.CoreWindow.GetForCurrentThread().GetKeyState(Windows.System.VirtualKey.Shift);
                bool isShiftPressed = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

                if (isShiftPressed)
                {
                    // === Shift + Tab: 鍙嶅悜缂╄繘 ===
                    var doc = EditorBox.Document;
                    if (doc != null)
                    {
                        string fullText = string.Empty;
                        doc.GetText(Windows.UI.Text.TextGetOptions.None, out fullText);

                        var selection = doc.Selection;
                        int selStart = selection.StartPosition;
                        int selEnd = selection.EndPosition;
                        
                        // 纭繚 Start <= End
                        if (selStart > selEnd) { var t = selStart; selStart = selEnd; selEnd = t; }

                        // 1. 鍚戝墠瀵绘壘褰撳墠琛岀殑璧风偣
                        int lineStart = selStart;
                        while (lineStart > 0 && fullText[lineStart - 1] != '\r')
                        {
                            lineStart--;
                        }

                        // 2. 鍚戝悗瀵绘壘褰撳墠(鎴栨渶鍚庨€変腑)琛岀殑缁堢偣
                        // 銆愪慨澶嶃€戜笉瑕佸寘鍚渶鍚庣殑 \r锛屽彧璇诲埌 \r 涔嬪墠鍗冲彲
                        int lineEnd = selEnd;
                        while (lineEnd < fullText.Length && fullText[lineEnd] != '\r')
                        {
                            lineEnd++;
                        }
                        
                        // 銆愬凡鍒犻櫎銆戝紩璧?Bug 鐨勪唬鐮侊細 lineEnd++ 
                        // 鎴戜滑涓嶉渶瑕佹妸鏈€鍚庝竴琛岀殑鎹㈣绗︿篃鍗疯繘鏉ュ鐞嗭紝鐣欑潃瀹冨湪鍘熷湴灏辫銆?
                        // 3. 鎴彇闇€瑕佸鐞嗙殑鏂囨湰鍧?                        if (lineEnd <= lineStart) return; // 鍙湁绌鸿鎴栧紓甯告儏鍐碉紝鐩存帴杩斿洖
                        string segment = fullText.Substring(lineStart, lineEnd - lineStart);
                        
                        // 4. 鎸夎鎷嗗垎 (鐜板湪 segment 涓嶅寘鍚湯灏剧殑 \r锛孲plit 缁撴灉浼氬緢骞插噣)
                        var lines = segment.Split('\r'); 

                        var sb = new StringBuilder();
                        int offsetToSelStart = selStart - lineStart;
                        int offsetToSelEnd = selEnd - lineStart;
                        int cumulativeRemoved = 0;
                        int removedBeforeSelStart = 0;
                        int removedBeforeSelEnd = 0;
                        int cursor = 0;

                        for (int i = 0; i < lines.Length; i++)
                        {
                            string line = lines[i];
                            int removed = 0;

                            if (!string.IsNullOrEmpty(line))
                            {
                                // 閫昏緫锛氬鏋滄槸 Tab 寮€澶达紝鍒?1 涓紱濡傛灉鏄┖鏍硷紝鏈€澶氬垹 4 涓?                                
                                if (line[0] == '\t')
                                {
                                    removed = 1;
                                    line = line.Substring(1);
                                }
                                else
                                {
                                    int spaceCount = 0;
                                    while (spaceCount < line.Length && spaceCount < 4 && line[spaceCount] == ' ')
                                    {
                                        spaceCount++;
                                    }
                                    if (spaceCount > 0)
                                    {
                                        removed = spaceCount;
                                        line = line.Substring(removed);
                                    }
                                }
                            }

                            int originalLength = lines[i].Length; 
                            // 杩欓噷涓嶉渶瑕佸啀鍒ゆ柇 TrailingBreak 浜嗭紝鍥犱负鎴戜滑娌″寘鍚湯灏炬崲琛岀锛?                            // 鍙湁涓棿鐨勬崲琛岀锛坙ines 鏁扮粍涔嬮棿鐨勶級闇€瑕佽ˉ鍥?\r
                            bool isIntermediateLine = (i < lines.Length - 1);

                            // 璁＄畻閫夊尯鍋忕Щ閲忕殑鍙樺寲
                            if (offsetToSelStart >= cursor && offsetToSelStart <= cursor + originalLength + (isIntermediateLine?1:0))
                                removedBeforeSelStart = cumulativeRemoved + (offsetToSelStart > cursor ? removed : 0); // 绮楃暐淇
                            
                            // 鏇寸簿鍑嗙殑鍏夋爣璺熼殢閫昏緫鏈夌偣澶嶆潅锛岃繖閲屼娇鐢ㄧ畝鍖栫増锛?                            // 濡傛灉鏀瑰姩鍙戠敓鍦ㄥ厜鏍囧乏杈癸紝鍏夋爣灏卞乏绉?                            if (cursor < offsetToSelStart) removedBeforeSelStart += removed;
                            if (cursor < offsetToSelEnd) removedBeforeSelEnd += removed;

                            sb.Append(line);
                            if (isIntermediateLine)
                            {
                                sb.Append('\r'); // 琛ュ洖涓棿琚?Split 鎷挎帀鐨勬崲琛岀
                            }

                            cumulativeRemoved += removed;
                            cursor += originalLength + 1; // +1 鏄负浜嗛€昏緫璁℃暟锛屽疄闄呬笉褰卞搷 sb
                        }

                        // 5. 鏇挎崲鏂囨湰
                        var range = doc.GetRange(lineStart, lineEnd);
                        range.SetText(Windows.UI.Text.TextSetOptions.None, sb.ToString());

                        // 6. 鎭㈠閫夊尯 (淇浣嶇疆)
                        // 杩欓噷鐨勮绠楃◢寰湁鐐?tricky锛岀畝鍗曞仛娉曟槸鐩存帴鍑忓幓绱鍒犻櫎閲忥紝
                        // 浣嗕负浜嗕弗璋紝鏈€濂介噸鏂拌绠?                        int newSelStart = Math.Max(lineStart, selStart - removedBeforeSelEnd); // 绠€鍖栧鐞嗭紝闃叉瓒婄晫
                        // 瀹為檯寤鸿锛氱洿鎺ュ叏閫夊鐞嗗悗鐨勫潡锛屾垨鑰呯畝鍗曠殑淇濇寔 Start 浣嶇疆
                        doc.Selection.SetRange(Math.Max(lineStart, selStart - cumulativeRemoved), Math.Max(lineStart, selEnd - cumulativeRemoved));
                        
                        // 涓婇潰鍏夋爣璁＄畻澶鏉傚鏄撻敊锛屾渶绋冲Ε鐨勭畝鍗曞洖閫€锛?                        // 淇濇寔閫変腑杩欏嚑琛?                        // doc.Selection.SetRange(lineStart, lineStart + sb.Length);
                    }
                }
                else
                {
                    // 鏅€?Tab
                    EditorBox.Document.Selection.TypeText("\t");
                }
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "PreviewContent")
            {
                var _ = RenderPreviewAsync();
            }
            else if (e.PropertyName == "ActiveDocument")
            {
                SyncEditorText();
                ResetUndoRedo();
            }
        }

        private async Task RenderPreviewAsync()
        {
            if (ViewModel == null) return;

            var theme = GetCurrentTheme();
            ViewModel.SetTheme(theme);

            if (!_skeletonLoaded || theme != _lastTheme)
            {
                _isWebViewReady = false;
                await _renderService.LoadSkeletonAsync(PreviewWebView, BuildPhoneCss(), theme);
                _skeletonLoaded = true;
                _lastTheme = theme;
            }

            if (!_isWebViewReady)
            {
                _pendingRender = true;
                return;
            }

            await _renderService.UpdateContentAsync(PreviewWebView, ViewModel.PreviewContent ?? string.Empty, isMarkdown: false);
            _lastBlocks = ViewModel.PreviewBlocks ?? new List<MarkdownBlock>();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var frame = Frame;
            if (frame != null && frame.CanGoBack)
            {
                frame.GoBack();
            }
            else if (frame != null)
            {
                frame.Navigate(typeof(MainPage));
            }
        }

        private void EditorBox_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_isUndoRedoLocked) return;
            if (ViewModel == null || ViewModel.ActiveDocument == null) return;

            string text = string.Empty;
            EditorBox.Document.GetText(TextGetOptions.None, out text);

            if (text != null)
            {
                text = text.Replace('\r', '\n').TrimEnd('\0', '\n');
            }

            ViewModel.SetContentFromEditor(text);
            _undoTimer.Stop();
            _undoTimer.Start();
            if (_autoSave.IsEnabled)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Start();
            }

            var change = AnalyzeChange(_lastEditorText, text);
            _lastEditorText = text;

            bool hotRendered = false;
            if (change.IsHot && change.LineIndex >= 0 && _skeletonLoaded)
            {
                var targetBlock = FindBlockForLine(change.LineIndex);
                var updatedBlock = BuildUpdatedBlock(targetBlock, text);
                if (updatedBlock != null)
                {
                    var _ = _renderService.UpdateBlockAsync(PreviewWebView, updatedBlock);
                    UpdateLocalBlockCache(updatedBlock);
                    hotRendered = true;
                }
            }

            if (change.HasChange && (change.RequiresCold || !hotRendered))
            {
                _typingTimer.Stop();
                _typingTimer.Start();
            }
            else
            {
                _typingTimer.Stop();
            }
        }

        private void TypingTimer_Tick(object sender, object e)
        {
            _typingTimer.Stop();

            var scroll = FindScrollViewer(EditorBox);
            double? vertical = scroll != null ? (double?)scroll.VerticalOffset : null;

            HighlightMarkdownSyntax();

            if (ViewModel != null)
            {
                ViewModel.RefreshPreview();
            }

            if (scroll != null && vertical.HasValue)
            {
                scroll.ChangeView(null, vertical, null, true);
            }
        }

        private void UndoTimer_Tick(object sender, object e)
        {
            _undoTimer.Stop();
            SaveSnapshot();
        }

        private async void AutoSaveTimer_Tick(object sender, object e)
        {
            _autoSaveTimer.Stop();
            if (!_autoSave.IsEnabled) return;
            if (ViewModel == null || ViewModel.ActiveDocument == null) return;
            if (ViewModel.ActiveDocument.File == null) return;
            if (!ViewModel.ActiveDocument.IsDirty) return;

            await ViewModel.SaveAsync();
        }

        private TextChangeInfo AnalyzeChange(string previous, string current)
        {
            var prevLines = SplitLines(previous);
            var currLines = SplitLines(current);

            if (prevLines.Length != currLines.Length)
            {
                return new TextChangeInfo { HasChange = true, RequiresCold = true, LineIndex = Math.Min(prevLines.Length, currLines.Length) };
            }

            int diffCount = 0;
            int diffLine = -1;
            for (int i = 0; i < currLines.Length; i++)
            {
                if (!string.Equals(prevLines[i], currLines[i], StringComparison.Ordinal))
                {
                    diffCount++;
                    if (diffLine == -1)
                    {
                        diffLine = i;
                    }
                }
            }

            if (diffCount == 0)
            {
                return new TextChangeInfo { HasChange = false, LineIndex = -1 };
            }

            if (diffCount > 1)
            {
                return new TextChangeInfo { HasChange = true, RequiresCold = true, LineIndex = diffLine };
            }

            var structural = IsStructuralChange(prevLines[diffLine], currLines[diffLine], previous, current);
            return new TextChangeInfo
            {
                HasChange = true,
                IsHot = !structural,
                RequiresCold = structural,
                LineIndex = diffLine
            };
        }

        private bool IsStructuralChange(string previousLine, string currentLine, string previousText, string currentText)
        {
            var prev = previousLine ?? string.Empty;
            var curr = currentLine ?? string.Empty;

            if (Math.Abs((previousText ?? string.Empty).Length - (currentText ?? string.Empty).Length) > 120)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(prev) != string.IsNullOrWhiteSpace(curr))
            {
                return true;
            }

            var trimmed = curr.TrimStart();
            if (trimmed.StartsWith("[") && trimmed.Contains("]:"))
            {
                return true;
            }

            return false;
        }

        private MarkdownBlock FindBlockForLine(int lineIndex)
        {
            if (_lastBlocks == null)
            {
                return null;
            }

            foreach (var block in _lastBlocks)
            {
                if (lineIndex >= block.StartLine && lineIndex <= block.EndLine)
                {
                    return block;
                }
            }

            return null;
        }

        private MarkdownBlock BuildUpdatedBlock(MarkdownBlock template, string markdown)
        {
            if (template == null)
            {
                return null;
            }

            var lines = SplitLines(markdown);
            if (lines.Length == 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            var start = Math.Max(0, template.StartLine);
            var end = Math.Min(lines.Length - 1, template.EndLine);
            if (start > end)
            {
                return null;
            }

            for (int i = start; i <= end; i++)
            {
                sb.AppendLine(lines[i]);
            }

            return new MarkdownBlock
            {
                Index = template.Index,
                StartLine = start,
                EndLine = end,
                Text = sb.ToString()
            };
        }

        private void UpdateLocalBlockCache(MarkdownBlock updated)
        {
            if (_lastBlocks == null || updated == null)
            {
                return;
            }

            var list = new List<MarkdownBlock>(_lastBlocks);
            if (updated.Index >= 0 && updated.Index < list.Count)
            {
                list[updated.Index] = updated;
                _lastBlocks = list;
            }
        }

        private string[] SplitLines(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        }

        private ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            var viewer = root as ScrollViewer;
            if (viewer != null) return viewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }

            return null;
        }

        private string GetNormalizedEditorText()
        {
            if (EditorBox == null || EditorBox.Document == null) return string.Empty;
            string text = string.Empty;
            EditorBox.Document.GetText(TextGetOptions.None, out text);
            return (text ?? string.Empty).Replace('\r', '\n').TrimEnd('\0', '\n');
        }

        private void SaveSnapshot()
        {
            var snapshot = GetNormalizedEditorText();
            if (_undoStack.Count == 0 || _undoStack.Peek() != snapshot)
            {
                _undoStack.Push(snapshot);
            }
            _redoStack.Clear();
        }

        private void ResetUndoRedo()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            var snapshot = GetNormalizedEditorText();
            _undoStack.Push(snapshot);
        }

        private async void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            Undo();
            await RenderPreviewAsync();
        }

        private async void RedoButton_Click(object sender, RoutedEventArgs e)
        {
            Redo();
            await RenderPreviewAsync();
        }

        private void Undo()
        {
            if (_undoStack.Count <= 1) return;

            _isUndoRedoLocked = true;
            try
            {
                var current = _undoStack.Pop();
                _redoStack.Push(current);
                var previous = _undoStack.Peek();
                ApplySnapshot(previous);
            }
            finally
            {
                _isUndoRedoLocked = false;
            }
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;

            _isUndoRedoLocked = true;
            try
            {
                var next = _redoStack.Pop();
                _undoStack.Push(next);
                ApplySnapshot(next);
            }
            finally
            {
                _isUndoRedoLocked = false;
            }
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

        private void ApplySnapshot(string text)
        {
            if (EditorBox == null || EditorBox.Document == null) return;

            _undoTimer.Stop();
            EditorBox.Document.SetText(TextSetOptions.None, text ?? string.Empty);
            _lastEditorText = (text ?? string.Empty).Replace('\r', '\n');
            ApplyEditorFormatting();
            HighlightMarkdownSyntax();
            if (ViewModel != null)
            {
                ViewModel.SetContentFromEditor(text);
                ViewModel.RefreshPreview();
            }
            var _ = RenderPreviewAsync();
        }

        private struct TextChangeInfo
        {
            public bool HasChange;
            public bool IsHot;
            public bool RequiresCold;
            public int LineIndex;
        }

        private void HighlightMarkdownSyntax()
        {
            if (EditorBox == null || EditorBox.Document == null) return;

            ITextDocument doc = EditorBox.Document;
            string text = string.Empty;
            doc.GetText(TextGetOptions.None, out text);

            if (string.IsNullOrEmpty(text)) return;

            try
            {
                doc.BatchDisplayUpdates();

                bool isDark = false;
                var rootFrame = Window.Current.Content as FrameworkElement;

                if (rootFrame != null)
                {
                    isDark = rootFrame.RequestedTheme == ElementTheme.Dark;
                }
                else
                {
                    isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
                }

                Color bodyColor;
                Color syntaxColor;

                if (isDark)
                {
                    bodyColor = Colors.White;
                    syntaxColor = Color.FromArgb(255, 120, 120, 120);
                }
                else
                {
                    bodyColor = Colors.Black;
                    syntaxColor = Color.FromArgb(255, 150, 150, 150);
                }

                int start = doc.Selection.StartPosition;
                int end = doc.Selection.EndPosition;

                ITextRange fullRange = doc.GetRange(0, text.Length);
                fullRange.CharacterFormat.ForegroundColor = bodyColor;

                RegexOptions options = RegexOptions.Multiline;

                MatchCollection headers = Regex.Matches(text, @"(?:^|\r)(#{1,6})(?=\s)", options);
                foreach (Match m in headers)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                MatchCollection links = Regex.Matches(text, @"(!?\[)(.*?)(\])(\(.*?\))", options);
                foreach (Match m in links)
                {
                    ITextRange r1 = doc.GetRange(m.Groups[1].Index, m.Groups[1].Index + m.Groups[1].Length);
                    r1.CharacterFormat.ForegroundColor = syntaxColor;

                    ITextRange r3 = doc.GetRange(m.Groups[3].Index, m.Groups[3].Index + m.Groups[3].Length);
                    r3.CharacterFormat.ForegroundColor = syntaxColor;

                    ITextRange r4 = doc.GetRange(m.Groups[4].Index, m.Groups[4].Index + m.Groups[4].Length);
                    r4.CharacterFormat.ForegroundColor = syntaxColor;
                }

                MatchCollection styles = Regex.Matches(text, @"(\*\*|__|\*|_|~~)(.+?)\1", options);
                foreach (Match m in styles)
                {
                    Group leftSign = m.Groups[1];
                    ITextRange rLeft = doc.GetRange(leftSign.Index, leftSign.Index + leftSign.Length);
                    rLeft.CharacterFormat.ForegroundColor = syntaxColor;

                    int rightSignStart = m.Index + m.Length - leftSign.Length;
                    ITextRange rRight = doc.GetRange(rightSignStart, rightSignStart + leftSign.Length);
                    rRight.CharacterFormat.ForegroundColor = syntaxColor;
                }

                MatchCollection quotes = Regex.Matches(text, @"(?:^|\r)(>\s)", options);
                foreach (Match m in quotes)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                MatchCollection hrs = Regex.Matches(text, @"(?:^|\r)(\-\-\-|\*\*\*)$", options);
                foreach (Match m in hrs)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                doc.Selection.SetRange(start, end);
                doc.Selection.CharacterFormat.ForegroundColor = bodyColor;
            }
            catch
            {
            }
            finally
            {
                doc.ApplyDisplayUpdates();
            }
        }

        private void SyncEditorText()
        {
            if (ViewModel != null && ViewModel.ActiveDocument != null && EditorBox != null)
            {
                var text = ViewModel.ActiveDocument.Content ?? string.Empty;
                _lastEditorText = text.Replace("\r\n", "\n");
                EditorBox.Document.SetText(TextSetOptions.None, text);
                ApplyEditorFormatting();
                HighlightMarkdownSyntax();
                ResetUndoRedo();
            }
        }
        private async void ExportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuFlyoutItem;
            if (item == null) return;

            var format = item.Tag != null ? item.Tag.ToString() : null;
            if (ViewModel != null)
            {
                await ViewModel.ExportAsync(format);
            }
        }

        private void AutoSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            var flyout = new AutoSaveSettings();
            flyout.DataContext = AutoSaveService.Instance;
            flyout.ShowIndependent();
        }

        private void ApplyEditorStyle()
        {
            if (EditorBox == null) return;

            var format = EditorBox.Document.GetDefaultParagraphFormat();
            format.SetLineSpacing(LineSpacingRule.Multiple, 1.5f);
            EditorBox.Document.SetDefaultParagraphFormat(format);
        }

        private async void PreviewWebView_NavigationCompleted(object sender, WebViewNavigationCompletedEventArgs e)
        {
            _isWebViewReady = true;

            if (EditorBox != null && EditorBox.Document != null)
            {
                string current = string.Empty;
                EditorBox.Document.GetText(TextGetOptions.None, out current);
                if (!string.IsNullOrWhiteSpace(current))
                {
                    await RenderPreviewAsync();
                }
            }

            if (_pendingRender)
            {
                _pendingRender = false;
                await RenderPreviewAsync();
            }
        }

        public async Task OpenFileAsync(StorageFile file)
        {
            if (file == null) return;

            var content = await FileIO.ReadTextAsync(file);

            _lastEditorText = (content ?? string.Empty).Replace("\r\n", "\n");
            EditorBox.Document.SetText(TextSetOptions.None, content ?? string.Empty);
            HighlightMarkdownSyntax();
            ResetUndoRedo();

            if (ViewModel != null)
            {
                ViewModel.SetContentFromEditor(content);
                ViewModel.RefreshPreview();
            }

            if (_isWebViewReady)
            {
                await RenderPreviewAsync();
            }
            else
            {
                _pendingRender = true;
            }
        }

        private ElementTheme GetCurrentTheme()
        {
            var root = Window.Current.Content as FrameworkElement;
            if (root != null)
            {
                return root.RequestedTheme;
            }
            return ElementTheme.Light;
        }

        private string BuildPhoneCss()
        {
            var baseCss = ViewModel != null ? ViewModel.PreviewCss : string.Empty;
            var phoneScale = "<style>body{font-size:14px;line-height:1.6;} pre{font-size:12px;} pre code, code{font-size:12px;}</style>";
            return (baseCss ?? string.Empty) + phoneScale;
        }
    }
}
