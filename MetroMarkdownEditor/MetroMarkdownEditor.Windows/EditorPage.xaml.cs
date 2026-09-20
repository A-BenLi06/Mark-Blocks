using System;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Data.Json;
using Windows.Storage;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.ViewModels;
using global::Windows.UI;
using global::Windows.UI.Text;
using global::Windows.UI.Xaml;
using global::Windows.UI.Xaml.Controls;
using global::Windows.UI.Xaml.Markup;
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
        private readonly DispatcherTimer _previewScrollTimer;
        private readonly DispatcherTimer _autoSaveTimer;
        private readonly DispatcherTimer _caretTimer;
        
        // 渲染服务：负责生成 HTML 和 CSS
        private readonly MarkdownRenderService _renderService = new MarkdownRenderService();
        private readonly MarkdownRenderService _backgroundRenderService = new MarkdownRenderService();
        private readonly HttpClient _imageUploadHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private readonly AutoSaveService _autoSave = AutoSaveService.Instance;
        
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
        private bool _hasPendingPreviewScroll;
        private bool _isPreviewScrollRunning;
        private int _previewScrollFailureCount;
        private bool _pendingSyntaxRefresh;
        private bool _pendingColdPreviewRefresh;
        private bool _pendingPreviewCssRefresh;
        private bool _pendingHiddenPreviewRefresh;
        private bool _isPreviewOperationRunning;
        private bool _pendingPreviewRenderRequest;
        private bool _suppressEditorScrollSync;
        private bool _isAutoPairEdit;
        private bool _suppressTextChanged;
        private bool _editorTextDirty;
        private DocumentViewModel _dirtyEditorDocument;
        private double _lastPreviewScrollRatio = -1;
        private DocumentViewModel _loadedEditorDocument;
        private char? _pendingAutoPairCharacter;
        private int _editorRevision;
        private int _previewRequestVersion;
        private System.Threading.CancellationTokenSource _previewCancellation;
        private DateTime _lastInputUtc = DateTime.MinValue;
        private global::Windows.System.VirtualKey? _lastEditorKey;
        private double _lastPreviewWorkMilliseconds;
        private bool _isPreviewParseRunning;
        private bool _isPageActive;
        private MarkdownHighlightRange _highlightRange;
        
        // 编辑器的内部滚动条引用，用于同步滚动
        private ScrollViewer _editorScrollViewer;

        // 保存 ThemeViewModel 的引用，以便监听全局主题切换
        private INotifyPropertyChanged _themeViewModel;

        // 搜索功能状态变量
        private int _lastSearchIndex = -1;
        private string _lastSearchText = string.Empty;
        private int _lastHighlightStart = -1;
        private int _lastHighlightLength = 0;
        private global::Windows.UI.Xaml.Controls.Primitives.Popup _outlinePopup;

        // 便捷访问 ViewModel
        private EditorViewModel ViewModel
        {
            get { return DataContext as EditorViewModel; }
        }

        public EditorPage()
        {
            InitializeComponent();

            // 初始化计时器
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            _typingTimer.Tick += TypingTimer_Tick;

            _syntaxTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _syntaxTimer.Tick += SyntaxTimer_Tick;

            _previewScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _previewScrollTimer.Tick += PreviewScrollTimer_Tick;

            _caretTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _caretTimer.Tick += CaretTimer_Tick;

            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            ConfigureAutoSaveTimer();

            // 注册事件
            EditorBox.Loaded += EditorBox_Loaded;
            
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
            _isPageActive = true;

            MarkdownSettingsService.Instance.SettingsChanged += OnMarkdownOrEditorSettingsChanged;
            EditorSettingsService.Instance.SettingsChanged += OnMarkdownOrEditorSettingsChanged;
            _autoSave.SettingsChanged += OnAutoSaveSettingsChanged;
            if (_editorScrollViewer != null)
            {
                _editorScrollViewer.ViewChanged += OnEditorScrollViewerViewChanged;
            }

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
                
                // 初始化编辑器内容 (SyncEditorText 内部会调用 ResetUndoRedo)
                SyncEditorText();
                ViewModel.RefreshPreview();
            }

            // 3. 初始渲染
            await RenderPreviewAsync();
            
            // 4. 页面加载完成，最后再强制应用一次格式，确保万无一失
            ApplyEditorFormatting();
            
            // 5. 注册全局快捷键监听（即使焦点不在编辑框也能触发）
            Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
            Window.Current.CoreWindow.CharacterReceived += CoreWindow_CharacterReceived;
        }

        /// <summary>
        /// 页面离开时触发，负责清理资源
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            FlushPendingEditorText(refreshPreview: false);
            _isPageActive = false;
            if (_previewCancellation != null) _previewCancellation.Cancel();
            _editorRevision++;

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
            _previewScrollTimer.Stop();
            _autoSaveTimer.Stop();
            _caretTimer.Stop();
            
            // 取消全局快捷键监听
            Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
            Window.Current.CoreWindow.CharacterReceived -= CoreWindow_CharacterReceived;
            MarkdownSettingsService.Instance.SettingsChanged -= OnMarkdownOrEditorSettingsChanged;
            EditorSettingsService.Instance.SettingsChanged -= OnMarkdownOrEditorSettingsChanged;
            _autoSave.SettingsChanged -= OnAutoSaveSettingsChanged;

            base.OnNavigatedFrom(e);
        }
        
        /// <summary>
        /// 全局快捷键处理（即使焦点不在编辑框也能触发）
        /// </summary>
        private void CoreWindow_KeyDown(global::Windows.UI.Core.CoreWindow sender, global::Windows.UI.Core.KeyEventArgs args)
        {
            _pendingAutoPairCharacter = null;
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
                if (!ReferenceEquals(_loadedEditorDocument, ViewModel.ActiveDocument))
                {
                    SyncEditorText();
                }
                else
                {
                    ApplyEditorFormatting();
                }
            }

            UpdateEditorBottomSpacer();
        }

        /// <summary>
        /// 滚动同步逻辑：当编辑器滚动时，同步滚动预览 WebView
        /// </summary>
        private void OnEditorScrollViewerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!e.IsIntermediate && !_suppressEditorScrollSync)
            {
                _pendingSyntaxRefresh = true;
                _syntaxTimer.Stop();
                _syntaxTimer.Start();
            }
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
            _hasPendingPreviewScroll = true;
            _previewScrollFailureCount = 0;

            if (!e.IsIntermediate)
            {
                _pendingSyntaxRefresh = true;
                _syntaxTimer.Stop();
                _syntaxTimer.Start();
                // Caret auto-scroll also raises final ViewChanged events. Keep
                // script invocation out of RichEdit's input/layout event chain.
                if (!_previewScrollTimer.IsEnabled) _previewScrollTimer.Start();
                return;
            }

            if (!_previewScrollTimer.IsEnabled)
            {
                _previewScrollTimer.Start();
            }
        }

        private void CoreWindow_CharacterReceived(
            global::Windows.UI.Core.CoreWindow sender,
            global::Windows.UI.Core.CharacterReceivedEventArgs args)
        {
            if (EditorBox == null || EditorBox.FocusState == FocusState.Unfocused)
            {
                _pendingAutoPairCharacter = null;
                return;
            }

            var ctrlState = sender.GetKeyState(global::Windows.System.VirtualKey.Control);
            var isCtrlPressed = (ctrlState & global::Windows.UI.Core.CoreVirtualKeyStates.Down)
                                == global::Windows.UI.Core.CoreVirtualKeyStates.Down;
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

        /// <summary>
        /// 注入 JS 控制 WebView 滚动
        /// </summary>
        private async Task SyncPreviewScrollAsync(double ratio)
        {
            _pendingPreviewScrollRatio = ratio;
            _hasPendingPreviewScroll = true;

            if (!_isWebViewReady || PreviewWebView == null) return;
            if (_isPreviewScrollRunning) return;
            if (IsEditorKeyHeld() || (DateTime.UtcNow - _lastInputUtc).TotalMilliseconds < EditorPerformancePolicy.InputQuietMilliseconds)
            {
                _previewScrollTimer.Interval = TimeSpan.FromMilliseconds(EditorPerformancePolicy.InputQuietMilliseconds);
                if (!_previewScrollTimer.IsEnabled) _previewScrollTimer.Start();
                return;
            }
            _previewScrollTimer.Interval = TimeSpan.FromMilliseconds(33);
            if (_isPreviewOperationRunning)
            {
                if (!_previewScrollTimer.IsEnabled) _previewScrollTimer.Start();
                return;
            }

            var clamped = Math.Max(0.0, Math.Min(1.0, _pendingPreviewScrollRatio));
            _hasPendingPreviewScroll = false;
            _isPreviewScrollRunning = true;

            try
            {
                await PreviewWebView.InvokeScriptAsync("__mdScrollToRatio", new[] { clamped.ToString(CultureInfo.InvariantCulture) });
                _lastPreviewScrollRatio = clamped;
                _previewScrollFailureCount = 0;
            }
            catch
            {
                // Navigation/content replacement can temporarily reject script
                // calls. Keep the latest ratio queued so pointer release is never
                // lost merely because a preview update was in flight.
                _hasPendingPreviewScroll = true;
                _previewScrollFailureCount++;
            }
            finally
            {
                _isPreviewScrollRunning = false;
                if (_hasPendingPreviewScroll
                    && _previewScrollFailureCount < 3
                    && _isPageActive
                    && !_previewScrollTimer.IsEnabled)
                {
                    _previewScrollTimer.Start();
                }
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
                
                // 自动滚动标签栏使当前文档可见 (平滑动画)
                var __ = ScrollToActiveDocumentAsync();
            }
            else if (e.PropertyName == EditorViewModel.PreviewRefreshRequestedPropertyName)
            {
                QueuePreviewRefresh(refreshCss: true);
            }
            else if (e.PropertyName == "ViewMode" && ShouldRenderPreview())
            {
                QueuePreviewRefresh(refreshCss: false);
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
        private async void EditorBox_KeyDown(object sender, global::Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            _lastEditorKey = e.Key;
            EditorBox_InputActivity(sender, e);
            _pendingAutoPairCharacter = null;
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

            if (isCtrlPressed && e.Key == global::Windows.System.VirtualKey.V)
            {
                var data = GetClipboardContentSafe();
                if (data != null && data.Contains(StandardDataFormats.StorageItems))
                {
                    e.Handled = true;
                    await TryPasteImagePathsFromClipboardAsync(data);
                    return;
                }
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

        private static DataPackageView GetClipboardContentSafe()
        {
            try
            {
                return Clipboard.GetContent();
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> TryPasteImagePathsFromClipboardAsync(DataPackageView data)
        {
            if (data == null || EditorBox == null || EditorBox.Document == null)
            {
                return false;
            }

            IReadOnlyList<IStorageItem> items;
            try
            {
                items = await data.GetStorageItemsAsync();
            }
            catch
            {
                return false;
            }

            if (items == null || items.Count == 0)
            {
                return false;
            }

            var imagePaths = items
                .OfType<StorageFile>()
                .Where(IsSupportedImageFile)
                .Select(file => file.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();

            if (imagePaths.Count == 0)
            {
                return false;
            }

            var imageSettings = ImageSettingsService.Instance;
            var imageReferences = await TryUploadImagePathsWithPicGoAsync(imagePaths) ?? imagePaths;
            var markdownImages = imageReferences.Select(path => BuildMarkdownImageReference(path, imageSettings)).ToList();

            try
            {
                EditorBox.Document.Selection.TypeText(string.Join("\r", markdownImages));
            }
            catch
            {
                return false;
            }

            return true;
        }

        private async Task<IReadOnlyList<string>> TryUploadImagePathsWithPicGoAsync(IReadOnlyList<string> imagePaths)
        {
            var settings = ImageSettingsService.Instance;
            if (!settings.ShouldUploadLocalImages || imagePaths == null || imagePaths.Count == 0)
            {
                return null;
            }

            if (settings.Uploader != ImageUploader.PicGoCore && settings.Uploader != ImageUploader.PicList)
            {
                return null;
            }

            var uploadUri = BuildPicGoUploadUri(settings.PicGoServerUrl, settings.PicGoServerSecret);
            if (uploadUri == null)
            {
                return null;
            }

            try
            {
                var payload = "{\"list\":[" + string.Join(",", imagePaths.Select(QuoteJsonString)) + "]}";
                using (var request = new HttpRequestMessage(HttpMethod.Post, uploadUri))
                {
                    AddPicGoAuthHeaders(request, settings.PicGoServerSecret);
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    var response = await _imageUploadHttpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    var responseText = await response.Content.ReadAsStringAsync();
                    var urls = ParsePicGoUploadResult(responseText);
                    return urls != null && urls.Count == imagePaths.Count ? urls : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static Uri BuildPicGoUploadUri(string serverUrl, string secret)
        {
            var value = string.IsNullOrWhiteSpace(serverUrl) ? "http://127.0.0.1:36677" : serverUrl.Trim();
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "http://" + value;
            }

            var queryStart = value.IndexOf('?');
            var baseUrl = queryStart >= 0 ? value.Substring(0, queryStart) : value;
            var query = queryStart >= 0 ? value.Substring(queryStart + 1) : string.Empty;
            if (!baseUrl.TrimEnd('/').EndsWith("/upload", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl.TrimEnd('/') + "/upload";
            }

            var trimmedSecret = (secret ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(trimmedSecret))
            {
                var encodedSecret = Uri.EscapeDataString(trimmedSecret);
                var authQuery = "key=" + encodedSecret + "&secret=" + encodedSecret;
                query = string.IsNullOrEmpty(query) ? authQuery : query + "&" + authQuery;
            }

            Uri uploadUri;
            return Uri.TryCreate(string.IsNullOrEmpty(query) ? baseUrl : baseUrl + "?" + query, UriKind.Absolute, out uploadUri)
                ? uploadUri
                : null;
        }

        private static void AddPicGoAuthHeaders(HttpRequestMessage request, string secret)
        {
            var trimmedSecret = (secret ?? string.Empty).Trim();
            if (request == null || string.IsNullOrEmpty(trimmedSecret))
            {
                return;
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", trimmedSecret);
            request.Headers.TryAddWithoutValidation("X-PicGo-Secret", trimmedSecret);
        }

        private static IReadOnlyList<string> ParsePicGoUploadResult(string responseText)
        {
            JsonObject json;
            if (!JsonObject.TryParse(responseText ?? string.Empty, out json))
            {
                return null;
            }

            IJsonValue successValue;
            if (json.TryGetValue("success", out successValue)
                && successValue.ValueType == JsonValueType.Boolean
                && !successValue.GetBoolean())
            {
                return null;
            }

            IJsonValue resultValue;
            if (!json.TryGetValue("result", out resultValue) || resultValue.ValueType != JsonValueType.Array)
            {
                return null;
            }

            var urls = new List<string>();
            foreach (var item in resultValue.GetArray())
            {
                if (item.ValueType != JsonValueType.String)
                {
                    continue;
                }

                var url = item.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    urls.Add(url);
                }
            }

            return urls;
        }

        private static bool IsSupportedImageFile(StorageFile file)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.Path))
            {
                return false;
            }

            var fileType = (file.FileType ?? string.Empty).ToLowerInvariant();
            switch (fileType)
            {
                case ".bmp":
                case ".gif":
                case ".ico":
                case ".jpeg":
                case ".jpg":
                case ".png":
                case ".svg":
                case ".tif":
                case ".tiff":
                case ".webp":
                    return true;
                default:
                    return false;
            }
        }

        private static string BuildMarkdownImageReference(string path, ImageSettingsService settings)
        {
            var normalizedPath = (path ?? string.Empty).Replace('\\', '/');
            if (settings != null && settings.AddDotSlashForRelativePath && IsRelativeImagePath(normalizedPath) && !normalizedPath.StartsWith("./", StringComparison.Ordinal))
            {
                normalizedPath = "./" + normalizedPath;
            }

            if (settings != null && settings.AutoEscapeImageUrlWhenInsert)
            {
                normalizedPath = normalizedPath.Replace(" ", "%20");
            }

            if (RequiresAngleBracketLinkDestination(normalizedPath))
            {
                normalizedPath = "<" + normalizedPath.Replace("<", "%3C").Replace(">", "%3E") + ">";
            }

            return "![](" + normalizedPath + ")";
        }

        private static bool IsRelativeImagePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && !path.Contains("://")
                && !path.StartsWith("/", StringComparison.Ordinal)
                && !(path.Length > 1 && path[1] == ':');
        }

        private static string QuoteJsonString(string value)
        {
            var builder = new StringBuilder();
            builder.Append('"');
            foreach (var ch in value ?? string.Empty)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(ch))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(ch);
                        }
                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static bool RequiresAngleBracketLinkDestination(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.IndexOfAny(new[] { ' ', '\t', '(', ')' }) >= 0;
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
                var theme = GetCurrentTheme();
                ViewModel.SetTheme(theme);

                // 如果骨架未加载或主题改变，重新加载基础 HTML/CSS
                if (!_skeletonLoaded || theme != _lastTheme || _renderService.NeedsLibraries(ViewModel.PreviewBlocks))
                {
                    _isWebViewReady = false;
                    _lastBlocks = null;
                    await _renderService.LoadSkeletonAsync(PreviewWebView, ViewModel.PreviewCss, theme, ViewModel.PreviewBlocks);
                    _skeletonLoaded = true;
                    _lastTheme = theme;
                }

                if (!_isWebViewReady)
                {
                    _pendingRender = true;
                    return;
                }

                var nextBlocks = ViewModel.PreviewBlocks ?? new List<MarkdownBlock>();
                if (ReferenceEquals(_lastBlocks, nextBlocks)) return;
                var applied = await _renderService.UpdateBlocksAsync(PreviewWebView, _lastBlocks, nextBlocks);
                // Track what actually reached the DOM, even if a newer result queued meanwhile.
                _lastBlocks = applied ? nextBlocks : null;

            }
            finally
            {
                _isPreviewOperationRunning = false;
                DrainQueuedPreviewWork();
            }
        }

        private void DrainQueuedPreviewWork()
        {
            if (_hasPendingPreviewScroll && !_isPreviewScrollRunning)
            {
                var scroll = SyncPreviewScrollAsync(_pendingPreviewScrollRatio);
            }

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

        public void FlushEditorBufferForSuspension()
        {
            FlushPendingEditorText(refreshPreview: false);
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
            if (_isUndoRedoLocked || _suppressTextChanged || _isAutoPairEdit) return;
            if (ViewModel == null || ViewModel.ActiveDocument == null) return;

            var autoPairCharacter = _pendingAutoPairCharacter;
            _pendingAutoPairCharacter = null;
            if (autoPairCharacter.HasValue)
            {
                TryApplyAutoPairAtCaret(autoPairCharacter.Value);
            }

            _editorRevision++;
            _editorTextDirty = true;
            _dirtyEditorDocument = ViewModel.ActiveDocument;
            _dirtyEditorDocument.MarkEditorChanged();
            if (_previewCancellation != null) _previewCancellation.Cancel();
            _lastInputUtc = DateTime.UtcNow;
            _typingTimer.Interval = TimeSpan.FromMilliseconds(EditorPerformancePolicy.PreviewDelay(_lastEditorText.Length, _lastPreviewWorkMilliseconds));

            _pendingSyntaxRefresh = true;
            _pendingColdPreviewRefresh = true;
            _syntaxTimer.Stop();
            _syntaxTimer.Start();
            _typingTimer.Stop();
            _typingTimer.Start();

            if (_autoSave.IsEnabled)
            {
                if (!_autoSaveTimer.IsEnabled) _autoSaveTimer.Start();
            }

            ScheduleCaretCenteringIfNeeded();
        }

        private void ScheduleCaretCenteringIfNeeded()
        {
            var settings = EditorSettingsService.Instance;
            if (!settings.TypewriterFocusModeEnabled || !settings.KeepCaretInMiddleWhenTypewriterModeEnabled)
            {
                return;
            }

            // Coalesce native caret geometry and ChangeView work to one operation
            // per frame instead of placing it in RichEditBox.TextChanged.
            _caretTimer.Stop();
            _caretTimer.Start();
        }

        private void CaretTimer_Tick(object sender, object e)
        {
            _caretTimer.Stop();
            KeepCaretCenteredIfNeeded();
        }

        private bool TryApplyAutoPairAtCaret(char insertedCharacter)
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
            if (string.IsNullOrEmpty(insertedText) || insertedText[0] != insertedCharacter)
            {
                return false;
            }

            var closing = GetAutoPairClosingText(insertedCharacter, settings);
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
                if (Math.Abs(target - scrollViewer.VerticalOffset) < 1) return;
                scrollViewer.ChangeView(null, target, null, true);
            }
            catch
            {
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
            if (_autoSave.IsEnabled && (_editorTextDirty || (ViewModel != null && ViewModel.ActiveDocument != null && ViewModel.ActiveDocument.IsDirty)))
            {
                _autoSaveTimer.Start();
            }
        }

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
            // Query current state instead of relying on KeyUp: focus changes can
            // deliver the release elsewhere. Covers the initial key-repeat delay.
            return _lastEditorKey.HasValue && EditorBox.FocusState != FocusState.Unfocused
                && (global::Windows.UI.Core.CoreWindow.GetForCurrentThread().GetKeyState(_lastEditorKey.Value)
                    & global::Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        }

        private void SyntaxTimer_Tick(object sender, object e)
        {
            _syntaxTimer.Stop();
            if (EditorBox.FocusState != FocusState.Unfocused) return;

            // A queued tick can outlive Stop/Start; recheck quiet time before
            // native formatting, which can synchronously trigger RichEdit layout.
            if (_pendingSyntaxRefresh && (IsEditorKeyHeld() || (DateTime.UtcNow - _lastInputUtc).TotalMilliseconds < 650))
            {
                _syntaxTimer.Start();
                return;
            }

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

                if (scroll != null && vertical.HasValue && Math.Abs(scroll.VerticalOffset - vertical.Value) >= 1)
                {
                    scroll.ChangeView(null, vertical, null, true);
                }
            }
            finally
            {
                _suppressEditorScrollSync = false;
            }
        }

        private async void TypingTimer_Tick(object sender, object e)
        {
            _typingTimer.Stop();
            if (!_isPageActive || _isPreviewParseRunning || !_pendingColdPreviewRefresh || ViewModel == null) return;
            // Hidden previews do not require a native full-document snapshot.
            if (!(ShouldRenderPreview()) && !_pendingHiddenPreviewRefresh) return;
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
            _pendingColdPreviewRefresh = false;
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
                if (_pendingColdPreviewRefresh && _isPageActive)
                {
                    _pendingPreviewCssRefresh |= refreshCss;
                    _typingTimer.Interval = TimeSpan.FromMilliseconds(EditorPerformancePolicy.PreviewDelay(_lastEditorText.Length, _lastPreviewWorkMilliseconds));
                    _typingTimer.Start();
                }
            }
        }

        private void QueuePreviewRefresh(bool refreshCss)
        {
            if (ViewModel == null || ViewModel.ActiveDocument == null) return;

            _previewRequestVersion++;
            if (_previewCancellation != null) _previewCancellation.Cancel();
            _pendingColdPreviewRefresh = true;
            _pendingPreviewCssRefresh = _pendingPreviewCssRefresh || refreshCss;
            _pendingHiddenPreviewRefresh = true;

            // Document switches, settings changes and explicit refreshes already
            // have a ViewModel text snapshot, so start their worker immediately.
            // Keystrokes use the adaptive quiet period above.
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

        private bool ShouldRenderPreview()
        {
            return ViewModel != null && ViewModel.ViewMode != EditorViewMode.Write;
        }

        private string GetNormalizedEditorText()
        {
            if (EditorBox == null || EditorBox.Document == null) return string.Empty;
            string text = string.Empty;
            EditorBox.Document.GetText(TextGetOptions.None, out text);
            return NormalizeRichEditText(text);
        }

        private static string NormalizeRichEditText(string text)
        {
            return EditorPerformancePolicy.NormalizeText(text, richEditTerminalMarker: true);
        }

        private string FlushPendingEditorText(bool refreshPreview)
        {
            if (!_editorTextDirty || _dirtyEditorDocument == null || EditorBox == null || EditorBox.Document == null)
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

        private void ResetUndoRedo()
        {
            if (EditorBox == null || EditorBox.Document == null) return;
            EditorBox.Document.UndoLimit = 0;
            EditorBox.Document.UndoLimit = 100;
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            Undo();
        }

        private void RedoButton_Click(object sender, RoutedEventArgs e)
        {
            Redo();
        }

        private void Undo()
        {
            ApplyNativeUndoRedo(redo: false);
        }

        private void Redo()
        {
            ApplyNativeUndoRedo(redo: true);
        }

        private void ApplyNativeUndoRedo(bool redo)
        {
            if (EditorBox == null || EditorBox.Document == null) return;

            _typingTimer.Stop();
            _syntaxTimer.Stop();
            FlushPendingEditorText(refreshPreview: false);

            var changedText = false;
            string undoText = null;
            var before = _lastEditorText ?? string.Empty;
            _isUndoRedoLocked = true;
            try
            {
                // Programmatic syntax-color changes can appear as formatting-only
                // entries on some RichEdit implementations. Skip those and stop
                // at the first operation that actually changes Markdown text.
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
            _dirtyEditorDocument = ViewModel != null ? ViewModel.ActiveDocument : null;
            if (_dirtyEditorDocument != null) _dirtyEditorDocument.CommitEditorText(undoText);
            _lastEditorText = undoText;
            _editorTextDirty = false;
            _dirtyEditorDocument = null;
            _pendingSyntaxRefresh = false;
            if (ViewModel != null) ViewModel.RefreshPreview();
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
                float targetSize = 16f; // 默认磅值
                string targetFont = "Consolas";

                // The control already carries the same FontFamily/FontSize in
                // XAML. Updating only the document default keeps newly inserted
                // text consistent without a full GetText + range-format pass,
                // which fragments RichEdit's backing store and delays layout.
                var defaultFormat = EditorBox.Document.GetDefaultCharacterFormat();
                defaultFormat.Name = targetFont;
                defaultFormat.Size = targetSize;
                EditorBox.Document.SetDefaultCharacterFormat(defaultFormat);

                // RichEdit stores compact edit deltas, avoiding the previous
                // timer-driven full-document string snapshots.
                EditorBox.Document.UndoLimit = 100;
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

            _editorRevision++;
            _lastEditorText = NormalizeLineEndings(content);
            SetEditorTextWithoutNotification(content);
            
            // 漏掉的修复：打开文件后也必须强力纠正格式
            ApplyEditorFormatting();
            HighlightMarkdownSyntax(forceFullDocument: true);

            if (ViewModel != null)
            {
                ViewModel.SetContentFromEditor(content);
                ViewModel.RefreshPreview();
                _loadedEditorDocument = ViewModel.ActiveDocument;
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
                _editorRevision++;
                _typingTimer.Stop();
                _syntaxTimer.Stop();
                _pendingColdPreviewRefresh = false;
                _pendingSyntaxRefresh = false;
                _lastEditorText = NormalizeLineEndings(text);
                _editorTextDirty = false;
                _dirtyEditorDocument = null;
                _loadedEditorDocument = ViewModel.ActiveDocument;
                _lastPreviewScrollRatio = -1;
                SetEditorTextWithoutNotification(text);
                
                _pendingSyntaxRefresh = true;
                _syntaxTimer.Start();
                
                ResetUndoRedo();
                UpdateEditorBottomSpacer();
            }
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
        /// Markdown 语法高亮逻辑 (Regex 基于)
        /// </summary>
        private void HighlightMarkdownSyntax(bool forceFullDocument)
        {
            if (EditorBox.FocusState != FocusState.Unfocused)
            {
                _pendingSyntaxRefresh = true;
                return;
            }
            // Syntax display is intentionally viewport-virtualized for every
            // document. The flag is retained for call-site compatibility; themes,
            // edits and navigation all use the same bounded rendering path.
            var wasSuppressed = _suppressTextChanged;
            _suppressTextChanged = true;
            try { _highlightRange = MarkdownHighlightingHelper.HighlightVisible(EditorBox, _highlightRange); }
            finally { _suppressTextChanged = wasSuppressed; }
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
            _lastPreviewScrollRatio = -1;
            _previewScrollFailureCount = 0;

            // Navigation may complete after one or more queued refreshes. Apply
            // the newest prepared HTML once; do not read the whole RichEditBox or
            // inject the same preview twice.
            if (_pendingRender || (ViewModel != null && ViewModel.PreviewBlocks.Count > 0))
            {
                _pendingRender = false;
                await RenderPreviewAsync();
            }

            if (_hasPendingPreviewScroll)
            {
                await SyncPreviewScrollAsync(_pendingPreviewScrollRatio);
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
        // ==========================================
        // Outline
        // ==========================================

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
            var panel = new Grid
            {
                Width = 420,
                MaxHeight = Math.Max(260, Window.Current.Bounds.Height * 0.72),
                Background = (SolidColorBrush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
            };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titlePanel = new Grid { Margin = new Thickness(20, 16, 12, 8) };
            titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Outline",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);

            var close = new Button
            {
                Content = "x",
                Width = 36,
                Height = 36,
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

            titlePanel.Children.Add(title);
            titlePanel.Children.Add(close);
            Grid.SetRow(titlePanel, 0);
            panel.Children.Add(titlePanel);

            if (items == null || items.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "No headings",
                    FontSize = 16,
                    Opacity = 0.7,
                    Margin = new Thickness(20, 28, 20, 28)
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
                    Margin = new Thickness(8, 0, 8, 12),
                    ItemTemplate = BuildOutlineItemTemplate()
                };
                list.ItemClick += OutlineList_ItemClick;
                Grid.SetRow(list, 1);
                panel.Children.Add(list);
            }

            _outlinePopup = new global::Windows.UI.Xaml.Controls.Primitives.Popup
            {
                Child = new Border
                {
                    Child = panel,
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1)
                },
                IsLightDismissEnabled = true
            };

            var bounds = Window.Current.Bounds;
            _outlinePopup.HorizontalOffset = Math.Max(12, bounds.Width - 440);
            _outlinePopup.VerticalOffset = Math.Max(12, bounds.Height - panel.MaxHeight - 82);
            _outlinePopup.IsOpen = true;
        }

        private static DataTemplate BuildOutlineItemTemplate()
        {
            const string template =
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                "<Grid Padding=\"{Binding Indent}\" MinHeight=\"40\">" +
                "<TextBlock Text=\"{Binding Title}\" FontSize=\"15\" TextTrimming=\"CharacterEllipsis\" VerticalAlignment=\"Center\"/>" +
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

            if (ViewModel == null || item == null)
            {
                return;
            }

            if (ViewModel.ViewMode != EditorViewMode.Preview)
            {
                NavigateEditorToLine(item.Line);
            }

            if (ViewModel.ViewMode != EditorViewMode.Write)
            {
                await RenderPreviewAsync();
                await NavigatePreviewToOutlineItemAsync(item);
            }
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
                var block = item.BlockIndex.ToString(CultureInfo.InvariantCulture);
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

