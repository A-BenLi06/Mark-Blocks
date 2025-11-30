using System;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.ViewModels;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MetroMarkdownEditor
{
    public sealed partial class EditorPage : Page
    {
        // ==========================================
        // 核心变量定义
        // ==========================================
        private readonly DispatcherTimer _typingTimer;
        private readonly MarkdownRenderService _renderService = new MarkdownRenderService();
        
        private bool _skeletonLoaded;
        private ElementTheme _lastTheme = ElementTheme.Light;

        // 保存 ThemeViewModel 的引用，以便监听主题切换
        private INotifyPropertyChanged _themeViewModel;

        private EditorViewModel ViewModel
        {
            get { return DataContext as EditorViewModel; }
        }

        public EditorPage()
        {
            InitializeComponent();
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _typingTimer.Tick += TypingTimer_Tick;
            ApplyEditorStyle();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 1. 【新增】挂�?ThemeViewModel 监听
            // 这是解决“切换主题时编辑器不刷新”的关键
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
                if (!ViewModel.OpenDocuments.Any())
                {
                    await ViewModel.InitializeAsync();
                }
                SyncEditorText();
            }

            await RenderPreviewAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // 2. 【新增】卸载监听，防止内存泄漏
            if (_themeViewModel != null)
            {
                _themeViewModel.PropertyChanged -= OnThemeViewModelPropertyChanged;
            }

            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            base.OnNavigatedFrom(e);
        }

        // 3. 【新增】当主题 ViewModel 变化时，强制刷新编辑器高�?        
        private void OnThemeViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                HighlightMarkdownSyntax();
                var __ = RenderPreviewAsync(); // 同时刷新预览�?            
            });
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
            }
        }

        private async Task RenderPreviewAsync()
        {
            if (ViewModel == null) return;

            var theme = GetCurrentTheme();
            ViewModel.SetTheme(theme);

            if (!_skeletonLoaded || theme != _lastTheme)
            {
                await _renderService.LoadSkeletonAsync(PreviewWebView, ViewModel.PreviewCss, theme);
                _skeletonLoaded = true;
                _lastTheme = theme;
            }

            await _renderService.UpdateContentAsync(PreviewWebView, ViewModel.PreviewContent ?? string.Empty);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var frame = Frame;
            if (frame != null)
            {
                frame.Navigate(typeof(MainPage));
            }
        }

        private void EditorBox_TextChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || ViewModel.ActiveDocument == null) return;

            string text = string.Empty;
            EditorBox.Document.GetText(TextGetOptions.None, out text);
            
            if (text != null)
            {
                text = text.Replace('\r', '\n').TrimEnd('\0', '\n');
            }

            ViewModel.SetContentFromEditor(text);
            _typingTimer.Stop();
            _typingTimer.Start();
        }

        private void TypingTimer_Tick(object sender, object e)
        {
            _typingTimer.Stop();
            HighlightMarkdownSyntax();

            if (ViewModel != null)
            {
                ViewModel.RefreshPreview();
                var _ = RenderPreviewAsync();
            }
        }

        // ==========================================
        // 核心修复：语法高亮逻辑
        // ==========================================
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

                // 4. 【核心修复】准确判断当�?UI 的真实主�?                // 直接读取 RootFrame (Window.Current.Content) 的主题设�?                // 这是全局 SettingFlyout 修改的地方，以此为准�?                
                bool isDark = false;
                var rootFrame = Window.Current.Content as FrameworkElement;
                
                if (rootFrame != null)
                {
                    isDark = rootFrame.RequestedTheme == ElementTheme.Dark;
                }
                else
                {
                    // 降级判断：如果没�?RootFrame，则读取系统默认
                    isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
                }

                Color bodyColor;
                Color syntaxColor;

                if (isDark)
                {
                    // 深色模式：白字，深灰符号
                    bodyColor = Colors.White;
                    syntaxColor = Color.FromArgb(255, 120, 120, 120);
                }
                else
                {
                    // 浅色模式：黑字，浅灰符号
                    bodyColor = Colors.Black;
                    syntaxColor = Color.FromArgb(255, 150, 150, 150);
                }

                // 保存光标
                int start = doc.Selection.StartPosition;
                int end = doc.Selection.EndPosition;

                // 重置全文颜色
                ITextRange fullRange = doc.GetRange(0, text.Length);
                fullRange.CharacterFormat.ForegroundColor = bodyColor;

                RegexOptions options = RegexOptions.Multiline;

                // 规则A: 标题 (#, ##)
                MatchCollection headers = Regex.Matches(text, @"(?:^|\r)(#{1,6})(?=\s)", options);
                foreach (Match m in headers)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 规则B: 链接 [txt](url)
                MatchCollection links = Regex.Matches(text, @"(!?\[)(.*?)(\])(\(.*?\))", options);
                foreach (Match m in links)
                {
                    // ![
                    ITextRange r1 = doc.GetRange(m.Groups[1].Index, m.Groups[1].Index + m.Groups[1].Length);
                    r1.CharacterFormat.ForegroundColor = syntaxColor;

                    // 文字部分保持 bodyColor

                    // ]
                    ITextRange r3 = doc.GetRange(m.Groups[3].Index, m.Groups[3].Index + m.Groups[3].Length);
                    r3.CharacterFormat.ForegroundColor = syntaxColor;

                    // (url)
                    ITextRange r4 = doc.GetRange(m.Groups[4].Index, m.Groups[4].Index + m.Groups[4].Length);
                    r4.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 规则C: 粗体/斜体/删除�?                
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

                // 规则D: 引用 (>)
                MatchCollection quotes = Regex.Matches(text, @"(?:^|\r)(>\s)", options);
                foreach (Match m in quotes)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 规则E: 分割�?                
                MatchCollection hrs = Regex.Matches(text, @"(?:^|\r)(\-\-\-|\*\*\*)$", options);
                foreach (Match m in hrs)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // 恢复光标
                doc.Selection.SetRange(start, end);
                
                // 恢复输入颜色
                doc.Selection.CharacterFormat.ForegroundColor = bodyColor;
            }
            catch
            {
                // 忽略异常
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
                EditorBox.Document.SetText(TextSetOptions.None, text);
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

        private void ApplyEditorStyle()
        {
            if (EditorBox == null) return;

            var format = EditorBox.Document.GetDefaultParagraphFormat();
            format.SetLineSpacing(LineSpacingRule.Multiple, 1.5f);
            EditorBox.Document.SetDefaultParagraphFormat(format);
        }

        private ElementTheme GetCurrentTheme()
        {
            // 这里统一使用 Window.Current.Content 来判断，保持一致�?            
            var root = Window.Current.Content as FrameworkElement;
            if (root != null)
            {
                return root.RequestedTheme;
            }
            return ElementTheme.Light;
        }
    }
}