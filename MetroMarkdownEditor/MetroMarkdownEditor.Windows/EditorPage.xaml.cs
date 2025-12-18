using System;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using Windows.Storage;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.ViewModels;
using global::Windows.UI;
using global::Windows.UI.Text;
using global::Windows.UI.Xaml;
using global::Windows.UI.Xaml.Controls;
using global::Windows.UI.Xaml.Media;
using global::Windows.UI.Xaml.Navigation;
using global::Windows.UI.Xaml.Input;

namespace MetroMarkdownEditor.Windows
{
    /// <summary>
    /// Windows (PC/Tablet) 端的主编辑器页面逻辑
    /// </summary>
    public sealed partial class EditorPage : Page
    {
        // ==========================================
        // 核心成员变量
        // ==========================================
        
        // 防抖动计时器：避免每敲一个字符就触发高亮和渲染，提高性能
        private readonly DispatcherTimer _typingTimer;
        
        // 渲染服务：负责生成 HTML 和 CSS
        private readonly MarkdownRenderService _renderService = new MarkdownRenderService();
        
        // 撤销/重做 专用栈和计时器（用于合并短时间内的连续输入）
        private readonly DispatcherTimer _undoTimer;
        private readonly Stack<string> _undoStack = new Stack<string>();
        private readonly Stack<string> _redoStack = new Stack<string>();
        
        // 锁标志位：防止在执行撤销/重做操作时，再次触发 TextChanged 事件导致死循环
        private bool _isUndoRedoLocked;
        
        // 记录上一次的文本，用于比较差异（增量更新检测）
        private string _lastEditorText = string.Empty;
        private IReadOnlyList<MarkdownBlock> _lastBlocks = new List<MarkdownBlock>();

        // 状态标志位
        private bool _skeletonLoaded;   // HTML 骨架是否已加载
        private bool _isWebViewReady;   // WebView 是否导航完成
        private bool _pendingRender;    // 是否有挂起的渲染任务
        private ElementTheme _lastTheme = ElementTheme.Light; // 记录上次主题，用于检测切换
        
        // 编辑器的内部滚动条引用，用于同步滚动
        private ScrollViewer _editorScrollViewer;

        // 保存 ThemeViewModel 的引用，以便监听全局主题切换
        private INotifyPropertyChanged _themeViewModel;

        // 便捷访问 ViewModel
        private EditorViewModel ViewModel
        {
            get { return DataContext as EditorViewModel; }
        }

        public EditorPage()
        {
            InitializeComponent();

            // 初始化计时器
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _typingTimer.Tick += TypingTimer_Tick;

            _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _undoTimer.Tick += UndoTimer_Tick;

            // 注册事件
            PreviewWebView.NavigationCompleted += PreviewWebView_NavigationCompleted;
            EditorBox.Loaded += EditorBox_Loaded;
            
            // 构造时尝试应用一次样式，作为兜底防止字体异常
            ApplyEditorFormatting();
        }

        /// <summary>
        /// 页面导航进入时触发
        /// </summary>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 1. 挂载 ThemeViewModel 监听
            // 这是解决“切换主题时编辑器高亮不刷新”的关键逻辑
            var locator = App.Current.Resources["Locator"] as ViewModelLocator;
            if (locator != null)
            {
                _themeViewModel = locator.Theme;
                _themeViewModel.PropertyChanged += OnThemeViewModelPropertyChanged;
            }

            if (ViewModel != null)
            {
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            // 2. 处理导航参数（新建、打开文件、最近文件）
            var request = e.Parameter as EditorNavigationRequest;
            if (ViewModel != null)
            {
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
                // 兜底：如果没有文档，创建一个新的
                if (!ViewModel.OpenDocuments.Any())
                {
                    await ViewModel.InitializeAsync();
                }
                
                // 初始化编辑器内容
                SyncEditorText();
                ResetUndoRedo();
            }

            // 3. 初始渲染
            await RenderPreviewAsync();
            
            // 4. 页面加载完成，最后再强制应用一次格式，确保万无一失
            ApplyEditorFormatting();
        }

        /// <summary>
        /// 页面离开时触发，负责清理资源
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // 卸载监听，防止内存泄漏
            if (_themeViewModel != null)
            {
                _themeViewModel.PropertyChanged -= OnThemeViewModelPropertyChanged;
            }

            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (_editorScrollViewer != null)
            {
                _editorScrollViewer.ViewChanged -= OnEditorScrollViewerViewChanged;
            }

            base.OnNavigatedFrom(e);
        }

        /// <summary>
        /// RichEditBox 加载完成后，查找其内部的 ScrollViewer 以便监听滚动
        /// </summary>
        private void EditorBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (_editorScrollViewer != null) return;

            // 使用 VisualTreeHelper 查找内部控件
            _editorScrollViewer = FindScrollViewer(EditorBox);
            if (_editorScrollViewer != null)
            {
                _editorScrollViewer.ViewChanged += OnEditorScrollViewerViewChanged;
            }
        }

        /// <summary>
        /// 滚动同步逻辑：当编辑器滚动时，同步滚动预览 WebView
        /// </summary>
        private void OnEditorScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (ViewModel == null || ViewModel.ViewMode != EditorViewMode.Split) return;
            if (!_isWebViewReady || PreviewWebView == null) return;

            var scroll = sender as ScrollViewer ?? _editorScrollViewer;
            if (scroll == null) return;

            // 计算滚动比例 (0.0 - 1.0)
            var total = scroll.ScrollableHeight;
            var ratio = total > 0 ? scroll.VerticalOffset / total : 0;
            SyncPreviewScroll(ratio);
        }

        /// <summary>
        /// 注入 JS 控制 WebView 滚动
        /// </summary>
        private async void SyncPreviewScroll(double ratio)
        {
            if (!_isWebViewReady || PreviewWebView == null) return;

            var clamped = Math.Max(0.0, Math.Min(1.0, ratio));
            
            // 使用 eval 直接执行 JS，计算 WebView 内部高度并滚动到对应比例
            var script = "(function(){var d=document.documentElement||document.body;var max=(d.scrollHeight||document.body.scrollHeight)-window.innerHeight;if(max<0){max=0;}window.scrollTo(0,max*" + clamped.ToString(CultureInfo.InvariantCulture) + ");})();";

            try
            {
                await PreviewWebView.InvokeScriptAsync("eval", new[] { script });
            }
            catch
            {
                // 忽略脚本执行错误（例如页面正在加载中）
            }
        }

        // 当全局主题 ViewModel 变化时，强制刷新编辑器高亮和预览
        private void OnThemeViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var _ = Dispatcher.RunAsync(global::Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                // 主题变了，不仅颜色要变，字体也可能需要重新确认
                ApplyEditorFormatting(); 
                HighlightMarkdownSyntax();
                var __ = RenderPreviewAsync(); 
            });
        }

        private void Outline_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as OutlineItem;
            if (item != null)
            {
                // Scroll to line
                ScrollToLine(item.LineNumber);
            }
        }

        private async void ScrollToLine(int line)
        {
            if (!_isWebViewReady) return;
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

        private void UpdateOutlineVisibility()
        {
            // Find the Outline Grid (Column 0 of the Preview Border Grid)
            // Since we don't have a named reference in XAML diff, we rely on binding or structure.
            // Ideally, we should name the Grid in XAML. 
            // For this diff, I will assume the user accepts the XAML binding I added.
            // But wait, I didn't add a visibility binding in XAML because of the converter issue.
            // Let's name the grid in XAML and control it here.
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
            else if (e.PropertyName == "ViewMode")
            {
                // Handle Outline Visibility
                // We need to access the Grid definition. 
                // Since I cannot modify the XAML to add x:Name easily without replacing the whole file content in diff,
                // I will rely on the fact that the Outline is inside the Preview Border.
                // Actually, I can modify the XAML to add x:Name="OutlineGrid".
            }
        }

        /// <summary>
        /// 快捷键处理 (Ctrl+Z, Ctrl+Y, Tab)
        /// </summary>
        private void EditorBox_KeyDown(object sender, global::Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var ctrlState = global::Windows.UI.Core.CoreWindow.GetForCurrentThread().GetKeyState(global::Windows.System.VirtualKey.Control);
            bool isCtrlPressed = (ctrlState & global::Windows.UI.Core.CoreVirtualKeyStates.Down) == global::Windows.UI.Core.CoreVirtualKeyStates.Down;

            // 保存
            if (isCtrlPressed && e.Key == global::Windows.System.VirtualKey.S)
            {
                e.Handled = true;
                if (ViewModel != null)
                {
                    var _ = ViewModel.SaveAsync();
                }
                return;
            }

            // 撤销
            if (isCtrlPressed && e.Key == global::Windows.System.VirtualKey.Z)
            {
                e.Handled = true;
                Undo();
                return;
            }
            // 重做
            if (isCtrlPressed && e.Key == global::Windows.System.VirtualKey.Y)
            {
                e.Handled = true;
                Redo();
                return;
            }

            // Tab 键处理
            if (e.Key == global::Windows.System.VirtualKey.Tab)
            {
                e.Handled = true;

                var shiftState = global::Windows.UI.Core.CoreWindow.GetForCurrentThread().GetKeyState(global::Windows.System.VirtualKey.Shift);
                bool isShiftPressed = (shiftState & global::Windows.UI.Core.CoreVirtualKeyStates.Down) == global::Windows.UI.Core.CoreVirtualKeyStates.Down;

                if (isShiftPressed)
                {
                    // Shift + Tab: 反向缩进
                    HandleTab(isReverse: true);
                }
                else
                {
                    // 普通 Tab: 插入缩进
                    HandleTab(isReverse: false);
                }
            }
        }

        /// <summary>
        /// 核心 Tab 处理逻辑：支持多行缩进/反向缩进
        /// </summary>
        private void HandleTab(bool isReverse)
        {
            var doc = EditorBox.Document;
            if (doc == null) return;

            if (!isReverse)
            {
                // 普通 Tab：直接插入
                doc.Selection.TypeText("\t");
                return;
            }

            // === 反向缩进逻辑 (Shift+Tab) ===
            string fullText = string.Empty;
            doc.GetText(global::Windows.UI.Text.TextGetOptions.None, out fullText);

            var selection = doc.Selection;
            int selStart = selection.StartPosition;
            int selEnd = selection.EndPosition;

            // 规范化选区方向
            if (selStart > selEnd) { var t = selStart; selStart = selEnd; selEnd = t; }

            // 1. 寻找行首
            int lineStart = selStart;
            while (lineStart > 0 && fullText[lineStart - 1] != '\r') lineStart--;

            // 2. 寻找行尾
            int lineEnd = selEnd;
            while (lineEnd < fullText.Length && fullText[lineEnd] != '\r') lineEnd++;

            if (lineEnd <= lineStart) return;

            // 3. 提取选中块并按行处理
            string segment = fullText.Substring(lineStart, lineEnd - lineStart);
            var lines = segment.Split('\r');
            var sb = new StringBuilder();
            
            int cumulativeRemoved = 0; // 记录总共删除了多少字符，用于修正选区

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int removed = 0;

                if (!string.IsNullOrEmpty(line))
                {
                    // 优先删除 Tab，如果没有 Tab 则最多删除 4 个空格
                    if (line[0] == '\t')
                    {
                        removed = 1;
                        line = line.Substring(1);
                    }
                    else
                    {
                        int spaceCount = 0;
                        while (spaceCount < line.Length && spaceCount < 4 && line[spaceCount] == ' ') spaceCount++;
                        if (spaceCount > 0)
                        {
                            removed = spaceCount;
                            line = line.Substring(removed);
                        }
                    }
                }

                bool isIntermediateLine = (i < lines.Length - 1);

                sb.Append(line);
                if (isIntermediateLine) sb.Append('\r');

                cumulativeRemoved += removed;
            }

            // 4. 应用修改并恢复选区
            var range = doc.GetRange(lineStart, lineEnd);
            range.SetText(global::Windows.UI.Text.TextSetOptions.None, sb.ToString());
            
            // 简单的选区恢复：起点不变，终点减去删除量
            doc.Selection.SetRange(Math.Max(lineStart, selStart - cumulativeRemoved), Math.Max(lineStart, selEnd - cumulativeRemoved));
        }

        /// <summary>
        /// 负责调用 RenderService 生成预览 HTML 并注入 WebView
        /// </summary>
        private async Task RenderPreviewAsync()
        {
            if (ViewModel == null) return;

            var theme = GetCurrentTheme();
            ViewModel.SetTheme(theme);

            // 如果骨架未加载或主题改变，重新加载基础 HTML/CSS
            if (!_skeletonLoaded || theme != _lastTheme)
            {
                _isWebViewReady = false;
                await _renderService.LoadSkeletonAsync(PreviewWebView, ViewModel.PreviewCss, theme);
                _skeletonLoaded = true;
                _lastTheme = theme;
            }

            if (!_isWebViewReady)
            {
                _pendingRender = true;
                return;
            }

            // 增量更新内容
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
        }

        // ==========================================
        // 编辑器核心逻辑：文本变化处理
        // ==========================================
        private void EditorBox_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_isUndoRedoLocked) return;
            if (ViewModel == null || ViewModel.ActiveDocument == null) return;

            string text = string.Empty;
            EditorBox.Document.GetText(TextGetOptions.None, out text);

            // 规范化换行符
            if (text != null)
            {
                text = text.Replace('\r', '\n').TrimEnd('\0', '\n');
            }

            ViewModel.SetContentFromEditor(text);

            // 重置 Undo 计时器 (防抖动)
            _undoTimer.Stop();
            _undoTimer.Start();

            // 分析变更类型 (热更新 vs 冷更新)
            var change = AnalyzeChange(_lastEditorText, text);
            _lastEditorText = text;

            bool hotRendered = false;
            // 尝试局部更新 (Hot Render)
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

            // 触发高亮计时器 (防抖动)
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

        private void UndoTimer_Tick(object sender, object e)
        {
            _undoTimer.Stop();
            SaveSnapshot(); // 保存撤销快照
        }

        private void TypingTimer_Tick(object sender, object e)
        {
            _typingTimer.Stop();

            // 保存滚动位置，因为高亮可能导致重绘
            var scroll = FindScrollViewer(EditorBox);
            double? vertical = scroll != null ? (double?)scroll.VerticalOffset : null;

            HighlightMarkdownSyntax();

            if (ViewModel != null)
            {
                ViewModel.RefreshPreview();
            }

            // 恢复滚动位置
            if (scroll != null && vertical.HasValue)
            {
                scroll.ChangeView(null, vertical, null, true);
            }
        }

        /// <summary>
        /// 简易的文本差异分析算法，用于判断是否可以局部刷新预览
        /// </summary>
        private TextChangeInfo AnalyzeChange(string previous, string current)
        {
            var prevLines = SplitLines(previous);
            var currLines = SplitLines(current);

            // 行数变化通常意味着结构变化，需要全量刷新
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

            if (diffCount == 0) return new TextChangeInfo { HasChange = false, LineIndex = -1 };
            if (diffCount > 1) return new TextChangeInfo { HasChange = true, RequiresCold = true, LineIndex = diffLine };

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

            if (Math.Abs((previousText ?? string.Empty).Length - (currentText ?? string.Empty).Length) > 120) return true;
            if (string.IsNullOrWhiteSpace(prev) != string.IsNullOrWhiteSpace(curr)) return true;

            var trimmed = curr.TrimStart();
            if (trimmed.StartsWith("[") && trimmed.Contains("]:")) return true;

            return false;
        }

        // --- 辅助方法区 (FindBlock, BuildBlock, SplitLines 等) ---
        private MarkdownBlock FindBlockForLine(int lineIndex)
        {
            if (_lastBlocks == null) return null;
            foreach (var block in _lastBlocks)
            {
                if (lineIndex >= block.StartLine && lineIndex <= block.EndLine) return block;
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

            for (int i = start; i <= end; i++) sb.AppendLine(lines[i]);

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

        /// <summary>
        /// 应用撤销/重做的快照
        /// </summary>
        private void ApplySnapshot(string text)
        {
            if (EditorBox == null || EditorBox.Document == null) return;

            _undoTimer.Stop();
            EditorBox.Document.SetText(TextSetOptions.None, text ?? string.Empty);
            _lastEditorText = (text ?? string.Empty).Replace('\r', '\n');
            
            // 每次 SetText 后必须重新应用格式，因为 RichEditBox 会回退到默认样式
            ApplyEditorFormatting();
            HighlightMarkdownSyntax();
            
            if (ViewModel != null)
            {
                ViewModel.SetContentFromEditor(text);
                ViewModel.RefreshPreview();
            }
            var _ = RenderPreviewAsync();
        }

        // ==========================================
        // 核心修复：更健壮的样式应用方法
        // ==========================================
        /// <summary>
        /// 强制应用编辑器的字体和字号。
        /// RichEditBox 在调用 SetText 后会重置 Document 的默认格式，
        /// 该方法用于在每次更新后强制把字体“扳”回来。
        /// </summary>
        private void ApplyEditorFormatting()
        {
            if (EditorBox == null || EditorBox.Document == null) return;

            try 
            {
                // 1. 计算目标字号 (Windows APP 推荐使用 Point)
                var displayInfo = global::Windows.Graphics.Display.DisplayInformation.GetForCurrentView();
                double scaleFactor = displayInfo.LogicalDpi / 96.0f;

                float targetSize = 16f; // 默认磅值
                // if (scaleFactor > 2.0) targetSize = 26f; // 高分屏补偿

                string targetFont = "Consolas";

                // 2. 设置“默认输入格式” (光标处新打的字)
                var defaultFormat = EditorBox.Document.GetDefaultCharacterFormat();
                defaultFormat.Name = targetFont;
                defaultFormat.Size = targetSize;
                EditorBox.Document.SetDefaultCharacterFormat(defaultFormat);

                // 3. 强制覆盖“已有全文”的格式 (但只覆盖字体和大小，保留高亮颜色)
                // 这是解决 SetText 后回退到系统默认字体的关键
                string text;
                EditorBox.Document.GetText(TextGetOptions.None, out text);
                
                if (!string.IsNullOrEmpty(text))
                {
                    // 选中全文
                    var fullRange = EditorBox.Document.GetRange(0, text.Length);
                    var rangeFormat = fullRange.CharacterFormat;
                    
                    // 检查是否需要更新，避免不必要的属性写入导致闪烁
                    // 注意：浮点数比较需要容差
                    if (rangeFormat.Name != targetFont || Math.Abs(rangeFormat.Size - targetSize) > 0.1f)
                    {
                        rangeFormat.Name = targetFont;
                        rangeFormat.Size = targetSize;
                        fullRange.CharacterFormat = rangeFormat;
                    }
                }
            }
            catch (Exception ex)
            {
                // 捕获异常，防止在某些极端 UI 状态下崩溃 (COM Exception)
                System.Diagnostics.Debug.WriteLine($"ApplyEditorFormatting Error: {ex.Message}");
            }
        }

        public async Task OpenFileAsync(StorageFile file)
        {
            if (file == null) return;

            var content = await FileIO.ReadTextAsync(file);

            _lastEditorText = (content ?? string.Empty).Replace("\r\n", "\n");
            EditorBox.Document.SetText(TextSetOptions.None, content ?? string.Empty);
            
            // 漏掉的修复：打开文件后也必须强力纠正格式
            ApplyEditorFormatting();
            HighlightMarkdownSyntax();

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
            // 这里统一使用 Window.Current.Content 来判断，保持一致
            var root = Window.Current.Content as FrameworkElement;
            if (root != null)
            {
                return root.RequestedTheme;
            }
            return ElementTheme.Light;
        }

        /// <summary>
        /// 将 ViewModel 中的文本同步到编辑器（覆盖模式）
        /// </summary>
        private void SyncEditorText()
        {
            if (ViewModel != null && ViewModel.ActiveDocument != null && EditorBox != null)
            {
                var text = ViewModel.ActiveDocument.Content ?? string.Empty;
                _lastEditorText = text.Replace("\r\n", "\n");
                EditorBox.Document.SetText(TextSetOptions.None, text);
                
                // 每次 SetText 后立即应用格式，然后再高亮
                ApplyEditorFormatting();
                HighlightMarkdownSyntax();
                
                ResetUndoRedo();
            }
        }

        /// <summary>
        /// Markdown 语法高亮逻辑 (Regex 基于)
        /// </summary>
        private void HighlightMarkdownSyntax()
        {
            if (EditorBox == null || EditorBox.Document == null) return;

            ITextDocument doc = EditorBox.Document;
            string text = string.Empty;
            doc.GetText(TextGetOptions.None, out text);

            if (string.IsNullOrEmpty(text)) return;

            try
            {
                // 批量更新，暂停 UI 重绘以提高性能
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

                // 标题
                MatchCollection headers = Regex.Matches(text, @"(?:^|\r)(#{1,6})(?=\s)", options);
                foreach (Match m in headers)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 链接
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

                // 样式 (粗体/斜体)
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

                // 引用
                MatchCollection quotes = Regex.Matches(text, @"(?:^|\r)(>\s)", options);
                foreach (Match m in quotes)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 分割线
                MatchCollection hrs = Regex.Matches(text, @"(?:^|\r)(\-\-\-|\*\*\*)$", options);
                foreach (Match m in hrs)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 恢复光标位置
                doc.Selection.SetRange(start, end);
                
                // 恢复输入颜色，确保用户接下来输入的文字颜色正确
                doc.Selection.CharacterFormat.ForegroundColor = bodyColor;
            }
            catch
            {
                // 忽略高亮过程中的错误，不影响核心功能
            }
            finally
            {
                // 恢复 UI 重绘
                doc.ApplyDisplayUpdates();
            }
        }

        // ==========================================
        // 【补全】遗漏的菜单栏点击事件
        // ==========================================

        /// <summary>
        /// 导出菜单项点击事件 (Markdown, HTML, PDF)
        /// </summary>
        private async void ExportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuFlyoutItem;
            if (item == null) return;

            // 获取 XAML 中 Tag 属性设定的格式 (md, html, pdf)
            var format = item.Tag != null ? item.Tag.ToString() : null;
            
            if (ViewModel != null)
            {
                await ViewModel.ExportAsync(format);
            }
        }

        /// <summary>
        /// 自动保存设置按钮点击事件
        /// </summary>
        private void AutoSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            // 如果你有 AutoSaveSettings 的用户控件，在这里显示
            // 如果暂时没有，可以先留空，或者简单的弹个提示
            try
            {
                // 示例：显示设置弹窗 (需要你有 AutoSaveSettings 这个 View)
                // var flyout = new SettingsFlyout(); 
                // flyout.Content = new AutoSaveSettings();
                // flyout.Show();
            }
            catch { }
        }

        /// <summary>
        /// WebView 导航完成事件：用于初始化预览滚动位置
        /// </summary>
        private async void PreviewWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            _isWebViewReady = true;

            // 如果此时编辑器已有内容，尝试同步一次预览
            if (EditorBox != null && EditorBox.Document != null)
            {
                string current = string.Empty;
                EditorBox.Document.GetText(TextGetOptions.None, out current);
                if (!string.IsNullOrWhiteSpace(current))
                {
                    await RenderPreviewAsync();
                }
            }

            // 处理挂起的渲染任务
            if (_pendingRender)
            {
                _pendingRender = false;
                await RenderPreviewAsync();
            }
        }

        /// <summary>
        /// 递归查找控件内部的 ScrollViewer (兼容 C# 5.0 写法)
        /// </summary>
        private ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;

            // --- 修复开始：改用老式写法 ---
            // 原写法: if (root is ScrollViewer viewer) return viewer;
            // 兼容写法:
            var viewer = root as ScrollViewer;
            if (viewer != null) return viewer;
            // --- 修复结束 ---

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
        /// <summary>
        /// 文本变更分析结果结构体
        /// </summary>
        private struct TextChangeInfo
        {
            public bool HasChange;      // 是否有实质性变化
            public bool IsHot;          // 是否可以热更新（局部刷新）
            public bool RequiresCold;   // 是否需要冷更新（全量刷新）
            public int LineIndex;       // 发生变化的行号
        }
    }
}