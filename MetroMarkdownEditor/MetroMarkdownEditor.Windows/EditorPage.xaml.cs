using System;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using Windows.ApplicationModel.DataTransfer;
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
        private readonly DispatcherTimer _syntaxTimer;
        private readonly DispatcherTimer _hotPreviewTimer;
        private readonly DispatcherTimer _previewScrollTimer;
        
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
        private double _pendingPreviewScrollRatio;
        private int _renderGeneration;
        private bool _pendingSyntaxRefresh;
        private bool _pendingColdPreviewRefresh;
        private MarkdownBlock _pendingHotBlock;
        private bool _isPreviewOperationRunning;
        private bool _pendingPreviewRenderRequest;
        private bool _suppressEditorScrollSync;
        private bool _isAutoPairEdit;
        private bool _editorTextDirty;
        private DocumentViewModel _dirtyEditorDocument;
        private double _lastPreviewScrollRatio = -1;
        
        // 编辑器的内部滚动条引用，用于同步滚动
        private ScrollViewer _editorScrollViewer;

        // 保存 ThemeViewModel 的引用，以便监听全局主题切换
        private INotifyPropertyChanged _themeViewModel;

        // 搜索功能状态变量
        private int _lastSearchIndex = -1;
        private string _lastSearchText = string.Empty;
        private int _lastHighlightStart = -1;
        private int _lastHighlightLength = 0;

        // 便捷访问 ViewModel
        private EditorViewModel ViewModel
        {
            get { return DataContext as EditorViewModel; }
        }

        public EditorPage()
        {
            InitializeComponent();

            // 初始化计时器
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _typingTimer.Tick += TypingTimer_Tick;

            _syntaxTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _syntaxTimer.Tick += SyntaxTimer_Tick;

            _hotPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _hotPreviewTimer.Tick += HotPreviewTimer_Tick;

            _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _undoTimer.Tick += UndoTimer_Tick;

            _previewScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _previewScrollTimer.Tick += PreviewScrollTimer_Tick;

            // 注册事件
            EditorBox.Loaded += EditorBox_Loaded;
            MarkdownSettingsService.Instance.SettingsChanged += OnMarkdownOrEditorSettingsChanged;
            EditorSettingsService.Instance.SettingsChanged += OnMarkdownOrEditorSettingsChanged;
            
            // 构造时尝试应用一次样式，作为兜底防止字体异常
            ApplyEditorFormatting();
            ApplyRuntimeEditorSettings();
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
                // 关键：先清空撤销历史，确保新文件不继承旧文件的 Undo 记录
                _undoStack.Clear();
                _redoStack.Clear();
                
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
                
                // 初始化编辑器内容 (SyncEditorText 内部会调用 ResetUndoRedo)
                SyncEditorText();
            }

            // 3. 初始渲染
            await RenderPreviewAsync();
            
            // 4. 页面加载完成，最后再强制应用一次格式，确保万无一失
            ApplyEditorFormatting();
            
            // 5. 注册全局快捷键监听（即使焦点不在编辑框也能触发）
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
        }

        /// <summary>
        /// 页面离开时触发，负责清理资源
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            FlushPendingEditorText(refreshPreview: false);

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

            _typingTimer.Stop();
            _syntaxTimer.Stop();
            _hotPreviewTimer.Stop();
            _undoTimer.Stop();
            _previewScrollTimer.Stop();
            
            // 取消全局快捷键监听
            Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
            MarkdownSettingsService.Instance.SettingsChanged -= OnMarkdownOrEditorSettingsChanged;
            EditorSettingsService.Instance.SettingsChanged -= OnMarkdownOrEditorSettingsChanged;

            base.OnNavigatedFrom(e);
        }
        
        /// <summary>
        /// 全局快捷键处理（即使焦点不在编辑框也能触发）
        /// </summary>
        private void CoreWindow_KeyDown(global::Windows.UI.Core.CoreWindow sender, global::Windows.UI.Core.KeyEventArgs args)
        {
            var ctrlState = sender.GetKeyState(global::Windows.System.VirtualKey.Control);
            bool isCtrlPressed = (ctrlState & global::Windows.UI.Core.CoreVirtualKeyStates.Down) == global::Windows.UI.Core.CoreVirtualKeyStates.Down;
            
            if (!isCtrlPressed) return;
            
            // Ctrl+S: 保存
            if (args.VirtualKey == global::Windows.System.VirtualKey.S && !EditorBox.FocusState.Equals(FocusState.Unfocused))
            {
                // 编辑框有焦点时，由 EditorBox_KeyDown 处理，避免重复触发两次保存
                return;
            }
            if (args.VirtualKey == global::Windows.System.VirtualKey.S)
            {
                args.Handled = true;
                var _ = SaveFromShortcutAsync();
                return;
            }
            
            // Ctrl+Z: 撤销 (只在编辑框没有焦点时处理，遟免重复)
            if (args.VirtualKey == global::Windows.System.VirtualKey.Z && !EditorBox.FocusState.Equals(FocusState.Unfocused))
            {
                // 编辑框有焦点时，由 EditorBox_KeyDown 处理
                return;
            }
            if (args.VirtualKey == global::Windows.System.VirtualKey.Z)
            {
                args.Handled = true;
                Undo();
                return;
            }
            
            // Ctrl+Y: 重做
            if (args.VirtualKey == global::Windows.System.VirtualKey.Y && !EditorBox.FocusState.Equals(FocusState.Unfocused))
            {
                return;
            }
            if (args.VirtualKey == global::Windows.System.VirtualKey.Y)
            {
                args.Handled = true;
                Redo();
                return;
            }
            
            // Ctrl+F: 搜索
            if (args.VirtualKey == global::Windows.System.VirtualKey.F)
            {
                args.Handled = true;
                ShowSearchDialog();
                return;
            }
        }

        /// <summary>
        /// RichEditBox 加载完成后，查找其内部的 ScrollViewer 以便监听滚动
        /// 并确保文本和格式正确应用（解决导航后格式丢失问题）
        /// </summary>
        private void EditorBox_Loaded(object sender, RoutedEventArgs e)
        {
            // 查找内部 ScrollViewer（仅第一次）
            if (_editorScrollViewer == null)
            {
                _editorScrollViewer = FindScrollViewer(EditorBox);
                if (_editorScrollViewer != null)
                {
                    _editorScrollViewer.ViewChanged += OnEditorScrollViewerViewChanged;
                }
            }
            
            // 每次加载完成后，确保同步文本和应用格式
            // 这是修复从 MainPage 再次打开同一文件时格式丢失的关键
            if (ViewModel != null && ViewModel.ActiveDocument != null)
            {
                SyncEditorText();
            }

            UpdateEditorBottomSpacer();
        }

        /// <summary>
        /// 滚动同步逻辑：当编辑器滚动时，同步滚动预览 WebView
        /// </summary>
        private void OnEditorScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (ViewModel == null || ViewModel.ViewMode != EditorViewMode.Split) return;
            if (!_isWebViewReady || PreviewWebView == null) return;
            if (_suppressEditorScrollSync) return;

            var scroll = sender as ScrollViewer ?? _editorScrollViewer;
            if (scroll == null) return;

            // 计算滚动比例 (0.0 - 1.0)
            var total = scroll.ScrollableHeight;
            var ratio = total > 0 ? scroll.VerticalOffset / total : 0;
            if (Math.Abs(ratio - _pendingPreviewScrollRatio) < 0.002 && e.IsIntermediate)
            {
                return;
            }

            _pendingPreviewScrollRatio = ratio;

            if (!e.IsIntermediate)
            {
                var _ = SyncPreviewScrollAsync(_pendingPreviewScrollRatio);
                _previewScrollTimer.Stop();
                return;
            }

            if (!_previewScrollTimer.IsEnabled)
            {
                _previewScrollTimer.Start();
            }
        }

        /// <summary>
        /// 注入 JS 控制 WebView 滚动
        /// </summary>
        private async Task SyncPreviewScrollAsync(double ratio)
        {
            if (!_isWebViewReady || PreviewWebView == null) return;
            if (_isPreviewOperationRunning) return;

            var clamped = Math.Max(0.0, Math.Min(1.0, ratio));
            if (Math.Abs(clamped - _lastPreviewScrollRatio) < 0.001)
            {
                return;
            }

            try
            {
                _lastPreviewScrollRatio = clamped;
                await PreviewWebView.InvokeScriptAsync("__mdScrollToRatio", new[] { clamped.ToString(CultureInfo.InvariantCulture) });
            }
            catch
            {
                // 忽略脚本执行错误（例如页面正在加载中）
            }
        }

        private void PreviewScrollTimer_Tick(object sender, object e)
        {
            _previewScrollTimer.Stop();

            if (!_isWebViewReady || PreviewWebView == null || ViewModel == null || ViewModel.ViewMode != EditorViewMode.Split)
            {
                return;
            }

            if (_isPreviewOperationRunning)
            {
                _previewScrollTimer.Start();
                return;
            }

            var _ = SyncPreviewScrollAsync(_pendingPreviewScrollRatio);
        }

        private void EditorPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateEditorBottomSpacer();
        }

        // 当全局主题 ViewModel 变化时，强制刷新编辑器高亮和预览
        private void OnThemeViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var _ = Dispatcher.RunAsync(global::Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                // 主题变了，不仅颜色要变，字体也可能需要重新确认
                ApplyEditorFormatting(); 
                HighlightMarkdownSyntax(forceFullDocument: true);
                var __ = RenderPreviewAsync(); 
            });
        }

        private void OnMarkdownOrEditorSettingsChanged(object sender, EventArgs e)
        {
            var _ = Dispatcher.RunAsync(global::Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                ApplyRuntimeEditorSettings();
                _skeletonLoaded = false;
                _lastBlocks = new List<MarkdownBlock>();
                HighlightMarkdownSyntax(forceFullDocument: true);
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

        // When theme changed, refresh highlighting and preview
        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "PreviewContent")
            {
                if (ShouldRenderPreview())
                {
                    var _ = RenderPreviewAsync();
                }
            }
            else if (e.PropertyName == "ActiveDocument")
            {
                FlushPendingEditorText(refreshPreview: false);
                SyncEditorText();
                ResetUndoRedo();
                
                // 自动滚动标签栏使当前文档可见 (平滑动画)
                var __ = ScrollToActiveDocumentAsync();
            }
            else if (e.PropertyName == "ViewMode" && ShouldRenderPreview())
            {
                var _ = RenderPreviewAsync();
            }
        }
        
        /// <summary>
        /// 平滑滚动到当前激活的文档标签页
        /// </summary>
        private async System.Threading.Tasks.Task ScrollToActiveDocumentAsync()
        {
            if (ViewModel?.ActiveDocument == null || FileTabsListView == null) return;
            
            try
            {
                // 等待一帧确保布局已更新
                await System.Threading.Tasks.Task.Delay(50);
                
                // 获取 ListView 内部的 ScrollViewer
                var scrollViewer = FindScrollViewer(FileTabsListView);
                if (scrollViewer == null) return;
                
                // 获取当前激活文档的索引
                var index = ViewModel.OpenDocuments.IndexOf(ViewModel.ActiveDocument);
                if (index < 0) return;
                
                // 获取对应的容器
                var container = FileTabsListView.ContainerFromIndex(index) as FrameworkElement;
                if (container == null) return;
                
                // 计算目标位置 (居中显示)
                var transform = container.TransformToVisual(FileTabsListView);
                var position = transform.TransformPoint(new global::Windows.Foundation.Point(0, 0));
                
                var targetOffset = position.X - (scrollViewer.ViewportWidth / 2) + (container.ActualWidth / 2);
                targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.ScrollableWidth));
                
                // 平滑滚动
                scrollViewer.ChangeView(targetOffset, null, null, false); // false = 启用动画
            }
            catch { /* 忽略滚动错误 */ }
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
                var _ = SaveFromShortcutAsync();
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

            if (isCtrlPressed && e.Key == global::Windows.System.VirtualKey.C)
            {
                if (TryHandlePlainTextClipboard(cut: false))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (isCtrlPressed && e.Key == global::Windows.System.VirtualKey.X)
            {
                if (TryHandlePlainTextClipboard(cut: true))
                {
                    e.Handled = true;
                    return;
                }
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
                doc.Selection.TypeText(new string(' ', MarkdownSettingsService.Instance.CodeIndentSize));
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
                    // 优先删除 Tab，如果没有 Tab 则删除当前代码缩进大小的空格
                    if (line[0] == '\t')
                    {
                        removed = 1;
                        line = line.Substring(1);
                    }
                    else
                    {
                        int spaceCount = 0;
                        var indentSize = MarkdownSettingsService.Instance.CodeIndentSize;
                        while (spaceCount < line.Length && spaceCount < indentSize && line[spaceCount] == ' ') spaceCount++;
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

        private bool TryHandlePlainTextClipboard(bool cut)
        {
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
                if (!settings.CopyCutWholeLinesWhenNoSelection)
                {
                    return false;
                }

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

            if (string.IsNullOrEmpty(textToCopy))
            {
                return false;
            }

            var package = new DataPackage();
            package.SetText(settings.NormalizeLineEndings(textToCopy.TrimEnd('\0')));
            Clipboard.SetContent(package);

            if (cut)
            {
                var range = doc.GetRange(replaceStart, replaceEnd);
                range.SetText(TextSetOptions.None, string.Empty);
            }

            return true;
        }

        /// <summary>
        /// 负责调用 RenderService 生成预览 HTML 并注入 WebView
        /// </summary>
        private async Task RenderPreviewAsync()
        {
            if (ViewModel == null) return;
            if (!ShouldRenderPreview()) return;
            if (_isPreviewOperationRunning)
            {
                _pendingPreviewRenderRequest = true;
                return;
            }

            _isPreviewOperationRunning = true;
            _pendingPreviewRenderRequest = false;

            try
            {
                var generation = ++_renderGeneration;

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

                var nextBlocks = ViewModel.PreviewBlocks ?? new List<MarkdownBlock>();
                bool appliedBlockDiff = false;

                if (_skeletonLoaded && CanApplyBlockDiff(_lastBlocks, nextBlocks))
                {
                    var changedBlocks = FindChangedBlocks(_lastBlocks, nextBlocks);
                    if (changedBlocks.Count == 0)
                    {
                        _lastBlocks = nextBlocks;
                        return;
                    }

                    if (changedBlocks.Count > 0 && changedBlocks.Count <= 4)
                    {
                        foreach (var block in changedBlocks)
                        {
                            if (generation != _renderGeneration)
                            {
                                return;
                            }

                            await _renderService.UpdateBlockAsync(PreviewWebView, block);
                        }

                        appliedBlockDiff = true;
                    }
                }

                if (!appliedBlockDiff)
                {
                    await _renderService.UpdateContentAsync(PreviewWebView, ViewModel.PreviewContent ?? string.Empty, isMarkdown: false);
                }

                if (generation == _renderGeneration)
                {
                    _lastBlocks = nextBlocks;
                }
            }
            finally
            {
                _isPreviewOperationRunning = false;
                DrainQueuedPreviewWork();
            }
        }

        private void DrainQueuedPreviewWork()
        {
            if (_pendingColdPreviewRefresh)
            {
                _typingTimer.Stop();
                _typingTimer.Start();
                return;
            }

            if (_pendingPreviewRenderRequest)
            {
                _pendingPreviewRenderRequest = false;
                var _ = RenderPreviewAsync();
            }
        }

        private void UpdateEditorBottomSpacer()
        {
            if (EditorBox == null) return;

            double viewportHeight = 0;
            if (_editorScrollViewer != null && _editorScrollViewer.ViewportHeight > 0)
            {
                viewportHeight = _editorScrollViewer.ViewportHeight;
            }
            else if (EditorPaneBorder != null && EditorPaneBorder.ActualHeight > 0)
            {
                viewportHeight = EditorPaneBorder.ActualHeight;
            }
            else if (ActualHeight > 0)
            {
                viewportHeight = ActualHeight;
            }

            if (viewportHeight <= 0) return;

            var bottomPadding = Math.Max(24, Math.Min(44, viewportHeight * 0.06));
            var currentPadding = EditorBox.Padding;
            var updatedPadding = new Thickness(currentPadding.Left, 24, currentPadding.Right, bottomPadding);

            if (!AreClose(currentPadding.Bottom, updatedPadding.Bottom) ||
                !AreClose(currentPadding.Left, updatedPadding.Left) ||
                !AreClose(currentPadding.Top, updatedPadding.Top) ||
                !AreClose(currentPadding.Right, updatedPadding.Right))
            {
                EditorBox.Padding = updatedPadding;
            }
        }

        private static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.5;
        }

        private async Task SaveFromShortcutAsync()
        {
            if (ViewModel == null || ViewModel.IsSaving)
            {
                return;
            }

            try
            {
                FlushPendingEditorText(refreshPreview: false);
                await ViewModel.SaveAsync();
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore duplicate/blocked writes from shortcut path; UI save state remains visible in the view model.
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveFromShortcutAsync();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            FlushPendingEditorText(refreshPreview: false);

            // Always navigate to MainPage, ignoring navigation history
            var frame = Frame;
            if (frame != null)
            {
                frame.Navigate(typeof(MainPage));
            }
        }

        // ==========================================
        // 编辑器核心逻辑：文本变化处理
        // ==========================================
        private void EditorBox_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_isUndoRedoLocked) return;
            if (ViewModel == null || ViewModel.ActiveDocument == null) return;

            if (!_isAutoPairEdit)
            {
                TryApplyAutoPairAtCaret();
            }

            _editorTextDirty = true;
            _dirtyEditorDocument = ViewModel.ActiveDocument;

            // 重置 Undo 计时器 (防抖动)
            _undoTimer.Stop();
            _undoTimer.Start();

            _hotPreviewTimer.Stop();
            _pendingHotBlock = null;
            _pendingSyntaxRefresh = true;
            _pendingColdPreviewRefresh = true;
            _syntaxTimer.Stop();
            _syntaxTimer.Start();
            _typingTimer.Stop();
            _typingTimer.Start();

            KeepCaretCenteredIfNeeded();
        }

        private bool TryApplyAutoPairAtCaret()
        {
            var settings = EditorSettingsService.Instance;
            if (!settings.AutoPairBracketsAndQuotes && !settings.AutoPairCommonMarkdownSyntax)
            {
                return false;
            }

            var selection = EditorBox.Document.Selection;
            var caret = selection.StartPosition;
            if (caret <= 0 || selection.EndPosition != caret)
            {
                return false;
            }

            string insertedText = string.Empty;
            EditorBox.Document.GetRange(caret - 1, caret).GetText(TextGetOptions.None, out insertedText);
            if (string.IsNullOrEmpty(insertedText))
            {
                return false;
            }

            var closing = GetAutoPairClosingText(insertedText[0], settings);
            if (closing == null)
            {
                return false;
            }

            string nextText = string.Empty;
            try
            {
                EditorBox.Document.GetRange(caret, caret + 1).GetText(TextGetOptions.None, out nextText);
            }
            catch
            {
                nextText = string.Empty;
            }
            if (!string.IsNullOrEmpty(nextText) && nextText[0] == closing[0])
            {
                return false;
            }

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
            if (!settings.TypewriterFocusModeEnabled || !settings.KeepCaretInMiddleWhenTypewriterModeEnabled)
            {
                return;
            }

            try
            {
                var scrollViewer = _editorScrollViewer ?? FindScrollViewer(EditorBox);
                if (scrollViewer == null || EditorBox == null || EditorBox.Document == null) return;

                global::Windows.Foundation.Rect rect;
                int hit;
                EditorBox.Document.Selection.GetRect(PointOptions.ClientCoordinates, out rect, out hit);
                if (rect.Height <= 0) return;

                var target = scrollViewer.VerticalOffset + rect.Top - (scrollViewer.ViewportHeight / 2) + rect.Height;
                target = Math.Max(0, Math.Min(target, scrollViewer.ScrollableHeight));
                scrollViewer.ChangeView(null, target, null, true);
            }
            catch
            {
            }
        }

        private void UndoTimer_Tick(object sender, object e)
        {
            _undoTimer.Stop();
            SaveSnapshot(); // 保存撤销快照
        }

        private void SyntaxTimer_Tick(object sender, object e)
        {
            _syntaxTimer.Stop();

            if (!_pendingSyntaxRefresh)
            {
                return;
            }

            if (_isPreviewOperationRunning)
            {
                _syntaxTimer.Start();
                return;
            }

            var scroll = FindScrollViewer(EditorBox);
            double? vertical = scroll != null ? (double?)scroll.VerticalOffset : null;

            try
            {
                _suppressEditorScrollSync = true;
                HighlightMarkdownSyntax(forceFullDocument: false);
                _pendingSyntaxRefresh = false;

                if (scroll != null && vertical.HasValue)
                {
                    scroll.ChangeView(null, vertical, null, true);
                }
            }
            finally
            {
                _suppressEditorScrollSync = false;
            }
        }

        private async void HotPreviewTimer_Tick(object sender, object e)
        {
            _hotPreviewTimer.Stop();

            var block = _pendingHotBlock;
            _pendingHotBlock = null;

            if (block == null || PreviewWebView == null || !_isWebViewReady || !ShouldRenderPreview())
            {
                return;
            }

            if (_isPreviewOperationRunning)
            {
                _pendingHotBlock = block;
                _hotPreviewTimer.Start();
                return;
            }

            _isPreviewOperationRunning = true;
            try
            {
                await _renderService.UpdateBlockAsync(PreviewWebView, block);
                UpdateLocalBlockCache(block);
            }
            catch
            {
            }
            finally
            {
                _isPreviewOperationRunning = false;
                DrainQueuedPreviewWork();
            }
        }

        private void TypingTimer_Tick(object sender, object e)
        {
            _typingTimer.Stop();

            if (_isPreviewOperationRunning)
            {
                _typingTimer.Start();
                return;
            }

            if (_pendingColdPreviewRefresh && ViewModel != null)
            {
                _pendingColdPreviewRefresh = false;
                FlushPendingEditorText(refreshPreview: true);
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

        private bool ShouldRenderPreview()
        {
            return ViewModel != null && ViewModel.ViewMode != EditorViewMode.Write;
        }

        private bool CanApplyBlockDiff(IReadOnlyList<MarkdownBlock> previousBlocks, IReadOnlyList<MarkdownBlock> nextBlocks)
        {
            if (previousBlocks == null || nextBlocks == null) return false;
            if (previousBlocks.Count == 0 || previousBlocks.Count != nextBlocks.Count) return false;
            return true;
        }

        private List<MarkdownBlock> FindChangedBlocks(IReadOnlyList<MarkdownBlock> previousBlocks, IReadOnlyList<MarkdownBlock> nextBlocks)
        {
            var changed = new List<MarkdownBlock>();
            if (previousBlocks == null || nextBlocks == null) return changed;

            var count = Math.Min(previousBlocks.Count, nextBlocks.Count);
            for (int i = 0; i < count; i++)
            {
                var previous = previousBlocks[i];
                var next = nextBlocks[i];
                if (!string.Equals(previous.Text, next.Text, StringComparison.Ordinal))
                {
                    changed.Add(next);
                }
            }

            return changed;
        }

        private string GetNormalizedEditorText()
        {
            if (EditorBox == null || EditorBox.Document == null) return string.Empty;
            string text = string.Empty;
            EditorBox.Document.GetText(TextGetOptions.None, out text);
            return (text ?? string.Empty).Replace('\r', '\n').TrimEnd('\0', '\n');
        }

        private void FlushPendingEditorText(bool refreshPreview)
        {
            if (!_editorTextDirty || _dirtyEditorDocument == null || EditorBox == null || EditorBox.Document == null)
            {
                return;
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
                _dirtyEditorDocument.Content = text;
            }

            _lastEditorText = text;
            _editorTextDirty = false;
            _dirtyEditorDocument = null;
        }

        private void SaveSnapshot()
        {
            FlushPendingEditorText(refreshPreview: false);
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
            _editorTextDirty = false;
            _dirtyEditorDocument = null;
            
            // 每次 SetText 后必须重新应用格式，因为 RichEditBox 会回退到默认样式
            ApplyEditorFormatting();
            HighlightMarkdownSyntax(forceFullDocument: true);
            
            if (ViewModel != null)
            {
                ViewModel.SetContentFromEditor(text);
                ViewModel.RefreshPreview();
            }
            var _ = RenderPreviewAsync();
            UpdateEditorBottomSpacer();
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
            HighlightMarkdownSyntax(forceFullDocument: true);

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
                HighlightMarkdownSyntax(forceFullDocument: true);
                
                ResetUndoRedo();
                UpdateEditorBottomSpacer();
            }
        }

        /// <summary>
        /// Markdown 语法高亮逻辑 (Regex 基于)
        /// </summary>
        private void HighlightMarkdownSyntax(bool forceFullDocument)
        {
            if (EditorBox == null || EditorBox.Document == null) return;

            ITextDocument doc = EditorBox.Document;

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
                bool useFullDocument = forceFullDocument;
                int highlightStart = 0;
                int highlightLength;
                string workingText;

                if (useFullDocument)
                {
                    string text = string.Empty;
                    doc.GetText(TextGetOptions.None, out text);
                    if (string.IsNullOrEmpty(text)) return;
                    workingText = text.Replace('\r', '\n');
                    highlightLength = text.Length;
                }
                else
                {
                    highlightStart = Math.Max(0, Math.Min(start, end) - 4096);
                    var highlightEnd = Math.Max(start, end) + 4096;
                    string text = string.Empty;
                    try
                    {
                        doc.GetRange(highlightStart, highlightEnd).GetText(TextGetOptions.None, out text);
                    }
                    catch
                    {
                        doc.GetText(TextGetOptions.None, out text);
                        highlightStart = 0;
                    }

                    if (string.IsNullOrEmpty(text)) return;
                    workingText = text.Replace('\r', '\n').TrimEnd('\0');
                    highlightLength = workingText.Length;
                }

                ITextRange fullRange = doc.GetRange(highlightStart, highlightStart + highlightLength);
                fullRange.CharacterFormat.ForegroundColor = bodyColor;

                RegexOptions options = RegexOptions.Multiline;

                // 标题
                MatchCollection headers = Regex.Matches(workingText, @"(?:^|\n)(#{1,6})(?=\s)", options);
                foreach (Match m in headers)
                {
                    Group g = m.Groups[1];
                    int rangeStart = highlightStart + g.Index;
                    ITextRange range = doc.GetRange(rangeStart, rangeStart + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 链接
                MatchCollection links = Regex.Matches(workingText, @"(!?\[)(.*?)(\])(\(.*?\))", options);
                foreach (Match m in links)
                {
                    ITextRange r1 = doc.GetRange(highlightStart + m.Groups[1].Index, highlightStart + m.Groups[1].Index + m.Groups[1].Length);
                    r1.CharacterFormat.ForegroundColor = syntaxColor;
                    ITextRange r3 = doc.GetRange(highlightStart + m.Groups[3].Index, highlightStart + m.Groups[3].Index + m.Groups[3].Length);
                    r3.CharacterFormat.ForegroundColor = syntaxColor;
                    ITextRange r4 = doc.GetRange(highlightStart + m.Groups[4].Index, highlightStart + m.Groups[4].Index + m.Groups[4].Length);
                    r4.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 样式 (粗体/斜体)
                MatchCollection styles = Regex.Matches(workingText, @"(\*\*|__|\*|_|~~)(.+?)\1", options);
                foreach (Match m in styles)
                {
                    Group leftSign = m.Groups[1];
                    ITextRange rLeft = doc.GetRange(highlightStart + leftSign.Index, highlightStart + leftSign.Index + leftSign.Length);
                    rLeft.CharacterFormat.ForegroundColor = syntaxColor;
                    int rightSignStart = highlightStart + m.Index + m.Length - leftSign.Length;
                    ITextRange rRight = doc.GetRange(rightSignStart, rightSignStart + leftSign.Length);
                    rRight.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 引用
                MatchCollection quotes = Regex.Matches(workingText, @"(?:^|\n)(>\s)", options);
                foreach (Match m in quotes)
                {
                    Group g = m.Groups[1];
                    int rangeStart = highlightStart + g.Index;
                    ITextRange range = doc.GetRange(rangeStart, rangeStart + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 分割线
                MatchCollection hrs = Regex.Matches(workingText, @"(?:^|\n)(\-\-\-|\*\*\*)$", options);
                foreach (Match m in hrs)
                {
                    Group g = m.Groups[1];
                    int rangeStart = highlightStart + g.Index;
                    ITextRange range = doc.GetRange(rangeStart, rangeStart + g.Length);
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
                FlushPendingEditorText(refreshPreview: false);
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

        // ==========================================
        // 搜索功能
        // ==========================================

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
                Width = 420,  // 1.5x 宽度
                Margin = new Thickness(0, 0, 0, 12)
            };
            searchBox.SelectAll();

            var findPrevButton = new Button
            {
                Content = "← Find Previous",
                Width = 195,  // 1.5x 宽度
                Margin = new Thickness(0, 0, 12, 0)
            };

            var findNextButton = new Button
            {
                Content = "Find Next →",
                Width = 195  // 1.5x 宽度
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
                FontSize = 20,
                FontWeight = global::Windows.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 0);

            var closeButton = new Button
            {
                Content = "✕",
                FontSize = 14,
                Width = 32,
                Height = 32,
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
                Padding = new Thickness(20),
                Background = (SolidColorBrush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"],
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                Child = contentPanel
            };

            var popup = new global::Windows.UI.Xaml.Controls.Primitives.Popup
            {
                Child = contentBorder,
                IsLightDismissEnabled = false  // 禁用点击外部关闭
            };

            // 计算位置: 水平居中, 垂直位于 37.8% 处
            var windowBounds = Window.Current.Bounds;
            double dialogWidth = 510;  // 宽度增加到1.5倍
            popup.HorizontalOffset = (windowBounds.Width - dialogWidth) / 2;
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
                if (args.Key == global::Windows.System.VirtualKey.Enter)
                {
                    _lastSearchText = searchBox.Text;
                    FindAndSelect(searchBox.Text, findNext: true);
                    args.Handled = true;
                }
                else if (args.Key == global::Windows.System.VirtualKey.Escape)
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
        /// <param name="searchText">要搜索的文本</param>
        /// <param name="findNext">true=向下查找, false=向上查找</param>
        private void FindAndSelect(string searchText, bool findNext)
        {
            if (string.IsNullOrEmpty(searchText)) return;

            // 根据当前视图模式决定搜索目标
            if (ViewModel != null && ViewModel.ViewMode == EditorViewMode.Preview)
            {
                // 预览模式：只在 WebView 中搜索
                FindInPreview(searchText, findNext);
            }
            else if (ViewModel != null && ViewModel.ViewMode == EditorViewMode.Split)
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
            if (EditorBox == null || EditorBox.Document == null) return;

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
                                    
                                    // 滚动到可见区域
                                    span.scrollIntoView({{ behavior: 'smooth', block: 'center' }});
                                }} catch(e) {{
                                    // 如果 surroundContents 失败，尝试手动创建节点
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
    }
}

