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
using Windows.Phone.UI.Input;

namespace MetroMarkdownEditor.WindowsPhone
{
    public sealed partial class EditorPage : Page
    {
        #region Fields

        private readonly DispatcherTimer _typingTimer;
        private readonly DispatcherTimer _undoTimer;
        private readonly DispatcherTimer _autoSaveTimer;
        private readonly MarkdownRenderService _renderService = new MarkdownRenderService();
        private readonly AutoSaveService _autoSave = AutoSaveService.Instance;
        private readonly Stack<string> _undoStack = new Stack<string>();
        private readonly Stack<string> _redoStack = new Stack<string>();

        private bool _isUndoRedoLocked;
        private string _lastEditorText = string.Empty;
        private bool _skeletonLoaded;
        private bool _isWebViewReady;
        private bool _pendingRender;
        private bool _isImmersiveMode;
        private EditorViewMode _currentViewMode = EditorViewMode.Write;
        private ElementTheme _lastTheme = ElementTheme.Light;
        private INotifyPropertyChanged _themeViewModel;

        // 搜索功能状态变量
        private int _lastSearchIndex = -1;
        private string _lastSearchText = string.Empty;
        private int _lastHighlightStart = -1;
        private int _lastHighlightLength = 0;

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

            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _typingTimer.Tick += TypingTimer_Tick;

            _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _undoTimer.Tick += UndoTimer_Tick;

            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSave.SettingsChanged += OnAutoSaveSettingsChanged;

            // Register EditorBox Loaded for robust font handling after navigation
            EditorBox.Loaded += EditorBox_Loaded;

            ConfigureAutoSaveTimer();
        }

        #endregion

        #region Navigation

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

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

                // Clear undo/redo history
                _undoStack.Clear();
                _redoStack.Clear();

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
            }

            // Reset view mode
            _currentViewMode = EditorViewMode.Write;
            UpdateViewMode();

            // Render preview
            await RenderPreviewAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
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

            // Stop timers
            _typingTimer.Stop();
            _undoTimer.Stop();
            _autoSaveTimer.Stop();
        }

        private async void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;

            // Preserve unsaved untitled files
            await PreserveUntitledFilesAsync();

            // Navigate to MainPage
            Frame.Navigate(typeof(MainPage));
            Frame.BackStack.Clear();
        }

        private Task PreserveUntitledFilesAsync()
        {
            if (ViewModel == null) return Task.FromResult(0);

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
            EditorBox.Document.SetText(TextSetOptions.None, content);
            _lastEditorText = content.Replace('\r', '\n');

            ApplyEditorFormatting();
            ResetUndoRedo();
        }



        /// <summary>
        /// EditorBox 加载完成后，确保文本和格式正确应用
        /// 这是修复从 MainPage 再次打开文件时格式丢失的关键
        /// </summary>
        private void EditorBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null && ViewModel.ActiveDocument != null)
            {
                SyncEditorText();
            }
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

                // 1. 设置"默认输入格式" (光标处新打的字)
                var defaultFormat = EditorBox.Document.GetDefaultCharacterFormat();
                defaultFormat.Name = targetFont;
                defaultFormat.Size = targetSize;
                EditorBox.Document.SetDefaultCharacterFormat(defaultFormat);

                // 2. 强制覆盖"已有全文"的格式 (关键！解决 SetText 后回退问题)
                string text;
                EditorBox.Document.GetText(TextGetOptions.None, out text);

                if (!string.IsNullOrEmpty(text))
                {
                    var fullRange = EditorBox.Document.GetRange(0, text.Length);
                    var rangeFormat = fullRange.CharacterFormat;

                    // 检查是否需要更新，避免不必要的属性写入导致闪烁
                    if (rangeFormat.Name != targetFont || Math.Abs(rangeFormat.Size - targetSize) > 0.1f)
                    {
                        rangeFormat.Name = targetFont;
                        rangeFormat.Size = targetSize;
                        fullRange.CharacterFormat = rangeFormat;
                    }
                }
            }
            catch { /* Ignore formatting errors */ }
        }

        #endregion

        #region Editor Events

        private void EditorBox_TextChanged(object sender, RoutedEventArgs e)
        {
            HandleTextChanged(EditorBox);
        }



        private void HandleTextChanged(RichEditBox editor)
        {
            if (_isUndoRedoLocked) return;
            if (ViewModel?.ActiveDocument == null) return;
            if (editor?.Document == null) return;

            string text;
            editor.Document.GetText(TextGetOptions.None, out text);
            text = (text ?? "").Replace('\r', '\n').TrimEnd('\0', '\n');

            ViewModel.SetContentFromEditor(text);
            _lastEditorText = text;

            // Restart timers
            _undoTimer.Stop();
            _undoTimer.Start();

            _typingTimer.Stop();
            _typingTimer.Start();

            if (_autoSave.IsEnabled)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Start();
            }
        }

        private void EditorBox_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
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
                ViewModel?.SaveCommand?.Execute(null);
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

            if (e.Key == Windows.System.VirtualKey.Tab)
            {
                e.Handled = true;
                editor?.Document?.Selection?.TypeText("\t");
            }
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
            _undoStack.Clear();
            _redoStack.Clear();
            var snapshot = GetNormalizedEditorText();
            _undoStack.Push(snapshot);
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

        private string GetNormalizedEditorText()
        {
            if (EditorBox?.Document == null) return string.Empty;
            string text;
            EditorBox.Document.GetText(TextGetOptions.None, out text);
            return (text ?? "").Replace('\r', '\n').TrimEnd('\0', '\n');
        }

        private void ApplySnapshot(string text)
        {
            if (EditorBox?.Document == null) return;

            _undoTimer.Stop();
            EditorBox.Document.SetText(TextSetOptions.None, text ?? "");
            _lastEditorText = (text ?? "").Replace('\r', '\n');

            ApplyEditorFormatting();
            HighlightMarkdownSyntax();

            ViewModel?.SetContentFromEditor(text);
            ViewModel?.RefreshPreview();
            // Note: RenderPreviewAsync will be triggered by ViewModel.PreviewContent change
        }

        #endregion

        #region Timers

        private void TypingTimer_Tick(object sender, object e)
        {
            _typingTimer.Stop();
            HighlightMarkdownSyntax();
            ViewModel?.RefreshPreview();
            // Note: RenderPreviewAsync will be triggered by ViewModel.PreviewContent change
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
            if (ViewModel?.ActiveDocument?.File == null) return;
            if (!ViewModel.ActiveDocument.IsDirty) return;

            await ViewModel.AutoSaveAsync();
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

            var theme = GetCurrentTheme();
            ViewModel.SetTheme(theme);

            // Determine which WebView to use
            WebView targetWebView = null;
            if (_currentViewMode == EditorViewMode.Preview)
            {
                targetWebView = PreviewWebView;
            }

            if (targetWebView == null) return;

            if (!_skeletonLoaded || theme != _lastTheme)
            {
                _isWebViewReady = false;
                await _renderService.LoadSkeletonAsync(targetWebView, BuildPhoneCss(), theme);
                _skeletonLoaded = true;
                _lastTheme = theme;
            }

            if (!_isWebViewReady)
            {
                _pendingRender = true;
                return;
            }

            await _renderService.UpdateContentAsync(targetWebView, ViewModel.PreviewContent ?? "", isMarkdown: false);
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
            return @"<style>
                body {
                    font-family: 'Segoe UI', sans-serif;
                    font-size: 16px;
                    line-height: 1.6;
                    padding: 12px 16px;
                    margin: 0;
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
            if (EditorBox?.Document == null) return;

            try
            {
                var doc = EditorBox.Document;
                string text;
                doc.GetText(TextGetOptions.None, out text);

                if (string.IsNullOrEmpty(text)) return;

                doc.BatchDisplayUpdates();

                bool isDark = GetCurrentTheme() == ElementTheme.Dark;
                Color bodyColor = isDark ? Colors.White : Colors.Black;
                Color syntaxColor = isDark
                    ? Color.FromArgb(255, 120, 120, 120)
                    : Color.FromArgb(255, 150, 150, 150);

                // Reset all text color
                var fullRange = doc.GetRange(0, text.Length);
                fullRange.CharacterFormat.ForegroundColor = bodyColor;

                // Highlight headers #
                var headers = Regex.Matches(text, @"(?:^|\r)(#{1,6})(?=\s)", RegexOptions.Multiline);
                foreach (Match m in headers)
                {
                    var g = m.Groups[1];
                    var range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // Highlight bold/italic markers
                var styles = Regex.Matches(text, @"(\*\*|__|[*_])(.+?)\1", RegexOptions.Multiline);
                foreach (Match m in styles)
                {
                    var g = m.Groups[1];
                    var r1 = doc.GetRange(g.Index, g.Index + g.Length);
                    r1.CharacterFormat.ForegroundColor = syntaxColor;

                    int rightStart = m.Index + m.Length - g.Length;
                    var r2 = doc.GetRange(rightStart, rightStart + g.Length);
                    r2.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // Highlight links [text](url) and images ![alt](url)
                var links = Regex.Matches(text, @"(!?\[)(.*?)(\])(\(.*?\))", RegexOptions.Multiline);
                foreach (Match m in links)
                {
                    // Dim the brackets and URL part, keep text visible
                    var r1 = doc.GetRange(m.Groups[1].Index, m.Groups[1].Index + m.Groups[1].Length);
                    r1.CharacterFormat.ForegroundColor = syntaxColor;
                    var r3 = doc.GetRange(m.Groups[3].Index, m.Groups[3].Index + m.Groups[3].Length);
                    r3.CharacterFormat.ForegroundColor = syntaxColor;
                    var r4 = doc.GetRange(m.Groups[4].Index, m.Groups[4].Index + m.Groups[4].Length);
                    r4.CharacterFormat.ForegroundColor = syntaxColor;
                }

                doc.ApplyDisplayUpdates();
            }
            catch { /* Ignore highlighting errors */ }
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
                SyncEditorText();
                ResetUndoRedo();
            }
        }

        private void OnThemePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                HighlightMarkdownSyntax();
                var __ = RenderPreviewAsync();
            });
        }

        #endregion

        #region Export

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as AppBarButton;
            if (button == null || ViewModel == null) return;

            var format = button.Tag as string;
            if (string.IsNullOrEmpty(format)) return;

            await ViewModel.ExportAsync(format);
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

        #endregion
    }
}
