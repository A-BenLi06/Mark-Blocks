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
using Windows.Phone.UI.Input; // 必须引用，用于处理物理后退键

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
            this.InitializeComponent();

            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _typingTimer.Tick += TypingTimer_Tick;

            _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _undoTimer.Tick += UndoTimer_Tick;

            _autoSave.SettingsChanged += OnAutoSaveSettingsChanged;
            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;

            PreviewWebView.NavigationCompleted += PreviewWebView_NavigationCompleted;
            
            // 放在最后执行，确保安全
            ApplyEditorStyle();
            
            // 注册硬件后退键 (WP8.1 必需)
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;
        }

        // 处理物理后退键
        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            // 如果已经处理过（比如弹窗关闭），则跳过
            if (e.Handled) return;

            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var locator = App.Current.Resources["Locator"] as ViewModelLocator;
            if (locator != null)
            {
                _themeViewModel = locator.Theme;
                if (_themeViewModel != null)
                {
                    _themeViewModel.PropertyChanged += OnThemeViewModelPropertyChanged;
                }
            }

            if (ViewModel != null)
            {
                // 先解绑，防止重复绑定导致内存泄漏或多次触发
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;

                var request = e.Parameter as EditorNavigationRequest;
                
                // 1. 根据不同模式初始化数据
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

                // 2. 兜底：如果没有打开的文档，就初始化一个新的
                if (!ViewModel.OpenDocuments.Any())
                {
                    await ViewModel.InitializeAsync();
                }

                // 3. 同步文字内容到编辑器
                SyncEditorText();
                ResetUndoRedo();
            }

            // Check if we returned from Outline with a scroll request
            if (ViewModel != null && ViewModel.ScrollToLineRequest >= 0)
            {
                ScrollToLine(ViewModel.ScrollToLineRequest);
                ViewModel.ScrollToLineRequest = -1; // Reset
            }

            // 4. 渲染预览
            await RenderPreviewAsync();

            // 5. 【最后一道防线】强制应用大字体格式
            ApplyEditorFormatting();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // 移除硬件后退键监听，防止内存泄漏和页面逻辑冲突
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;

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
            _typingTimer.Stop();
            _undoTimer.Stop();

            base.OnNavigatedFrom(e);
        }

        // 强制应用字体样式
        private void ApplyEditorFormatting()
        {
            if (EditorBox == null || EditorBox.Document == null) return;

            try 
            {
                // 使用 try-catch 防止控件未准备好时访问属性崩溃
                var format = EditorBox.Document.GetDefaultCharacterFormat();
                format.Name = "Consolas";
                format.Size = 16; // Windows Phone 上 16pt 比较合适
                EditorBox.Document.SetDefaultCharacterFormat(format);
                EditorBox.Document.Selection.CharacterFormat = format;
            }
            catch { /* 忽略格式设置错误，不影响核心功能 */ }
        }

        private void OnThemeViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                HighlightMarkdownSyntax();
                var __ = RenderPreviewAsync();
            });
        }

        private void EditorBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrlState = Windows.UI.Core.CoreWindow.GetForCurrentThread().GetKeyState(Windows.System.VirtualKey.Control);
            bool isCtrlPressed = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            // 保存
            if (isCtrlPressed && e.Key == Windows.System.VirtualKey.S)
            {
                e.Handled = true;
                if (ViewModel != null)
                {
                    var _ = ViewModel.SaveAsync();
                }
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
                    HandleShiftTab(); // 提取 Shift+Tab 逻辑以保持代码整洁
                }
                else
                {
                    // 普通 Tab
                    EditorBox.Document.Selection.TypeText("\t");
                }
            }
        }

        // 提取出来的 Shift+Tab 逻辑
        private void HandleShiftTab()
        {
            var doc = EditorBox.Document;
            if (doc == null) return;

            string fullText = string.Empty;
            doc.GetText(TextGetOptions.None, out fullText);

            var selection = doc.Selection;
            int selStart = selection.StartPosition;
            int selEnd = selection.EndPosition;

            if (selStart > selEnd) { var t = selStart; selStart = selEnd; selEnd = t; }

            int lineStart = selStart;
            while (lineStart > 0 && fullText[lineStart - 1] != '\r')
            {
                lineStart--;
            }

            int lineEnd = selEnd;
            while (lineEnd < fullText.Length && fullText[lineEnd] != '\r')
            {
                lineEnd++;
            }

            if (lineEnd <= lineStart) return;
            string segment = fullText.Substring(lineStart, lineEnd - lineStart);
            var lines = segment.Split('\r');

            var sb = new StringBuilder();
            int cumulativeRemoved = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int removed = 0;

                if (!string.IsNullOrEmpty(line))
                {
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

                bool isIntermediateLine = (i < lines.Length - 1);
                sb.Append(line);
                if (isIntermediateLine)
                {
                    sb.Append('\r');
                }
                cumulativeRemoved += removed;
            }

            var range = doc.GetRange(lineStart, lineEnd);
            range.SetText(TextSetOptions.None, sb.ToString());

            // 简单恢复选区
            doc.Selection.SetRange(Math.Max(lineStart, selStart - cumulativeRemoved), Math.Max(lineStart, selEnd - cumulativeRemoved));
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
            else if (e.PropertyName == "ScrollToLineRequest")
            {
                if (ViewModel.ScrollToLineRequest >= 0)
                {
                    ScrollToLine(ViewModel.ScrollToLineRequest);
                    ViewModel.ScrollToLineRequest = -1;
                }
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
            if (Frame != null && Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else if (Frame != null)
            {
                // 如果不能回退，导航回主页 (根据你项目实际情况修改)
                // Frame.Navigate(typeof(MainPage)); 
            }
        }

        private void EditorBox_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_isUndoRedoLocked) return;
            if (ViewModel == null || ViewModel.ActiveDocument == null) return;
            if (EditorBox == null || EditorBox.Document == null) return;

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
            // 注意：FindScrollViewer 可能会比较耗性能，放在 Timer 结束时做是正确的
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

            await ViewModel.AutoSaveAsync();
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
            if (_lastBlocks == null) return null;

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
            if (template == null) return null;

            var lines = SplitLines(markdown);
            if (lines.Length == 0) return null;

            var sb = new StringBuilder();
            var start = Math.Max(0, template.StartLine);
            var end = Math.Min(lines.Length - 1, template.EndLine);
            if (start > end) return null;

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
            if (_lastBlocks == null || updated == null) return;

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
            if (root is ScrollViewer) return root as ScrollViewer;

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

                // 使用安全的方式获取主题
                bool isDark = GetCurrentTheme() == ElementTheme.Dark;

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

                // 标题 #
                MatchCollection headers = Regex.Matches(text, @"(?:^|\r)(#{1,6})(?=\s)", options);
                foreach (Match m in headers)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 链接 [text](url)
                MatchCollection links = Regex.Matches(text, @"(!?\[)(.*?)(\])(\(.*?\))", options);
                foreach (Match m in links)
                {
                    // 仅高亮符号部分
                    foreach (int groupIdx in new[] { 1, 3, 4 })
                    {
                        var g = m.Groups[groupIdx];
                        if (g.Success) 
                        {
                            ITextRange r = doc.GetRange(g.Index, g.Index + g.Length);
                            r.CharacterFormat.ForegroundColor = syntaxColor;
                        }
                    }
                }

                // 粗体/斜体
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

                // 引用 >
                MatchCollection quotes = Regex.Matches(text, @"(?:^|\r)(>\s)", options);
                foreach (Match m in quotes)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 分割线 ---
                MatchCollection hrs = Regex.Matches(text, @"(?:^|\r)(\-\-\-|\*\*\*)$", options);
                foreach (Match m in hrs)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 恢复光标位置和颜色
                doc.Selection.SetRange(start, end);
                doc.Selection.CharacterFormat.ForegroundColor = bodyColor;
            }
            catch
            {
                // 忽略高亮过程中的任何错误，不影响使用
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

        // 导出菜单点击
        private async void ExportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as AppBarButton;
            if (item == null) return; 

            var format = item.Tag != null ? item.Tag.ToString() : null;
            if (ViewModel != null)
            {
                await ViewModel.ExportAsync(format);
            }
        }

        private void OutlineButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(OutlinePage));
        }

        private async void ScrollToLine(int line)
        {
            if (!_isWebViewReady) return;
            // Find the block closest to this line
            string script = string.Format(@"
                (function() {{
                    var blocks = document.querySelectorAll('.md-block');
                    var target = null;
                    for (var i = 0; i < blocks.length; i++) {{
                        var start = parseInt(blocks[i].getAttribute('data-start'));
                        if (start >= {0}) {{
                            target = blocks[i];
                            break;
                        }}
                    }}
                    if (target) target.scrollIntoView();
                }})();", line);

            try { await PreviewWebView.InvokeScriptAsync("eval", new[] { script }); } catch { }
        }

        // 自动保存设置点击
        private void AutoSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var flyout = new AutoSaveSettings();
                flyout.DataContext = AutoSaveService.Instance;
                // 如果是 WinRT Flyout, 可能需要用 ShowAt 或者自定义扩展方法
                // 这里假设 ShowIndependent 是你的扩展方法或 Callisto 库的方法
                flyout.ShowIndependent(); 
            }
            catch 
            {
                // 如果设置页打开失败，忽略
            }
        }

        private void ApplyEditorStyle()
        {
            if (EditorBox == null) return;
            try
            {
                if (EditorBox.Document != null)
                {
                    var format = EditorBox.Document.GetDefaultParagraphFormat();
                    format.SetLineSpacing(LineSpacingRule.Multiple, 1.5f);
                    EditorBox.Document.SetDefaultParagraphFormat(format);
                }
            }
            catch { }
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
            if (EditorBox != null && EditorBox.Document != null)
            {
                EditorBox.Document.SetText(TextSetOptions.None, content ?? string.Empty);
                HighlightMarkdownSyntax();
            }
            
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
            // 针对手机优化字体大小
            var phoneScale = "<style>body{font-size:14px;line-height:1.6;} pre{font-size:12px;} pre code, code{font-size:12px;}</style>";
            return (baseCss ?? string.Empty) + phoneScale;
        }
    }
}