using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.ViewModels;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;

namespace MetroMarkdownEditor
{
    public sealed partial class EditorPage : Page
    {
        private EditorViewModel ViewModel
        {
            get { return DataContext as EditorViewModel; }
        }

        private readonly DispatcherTimer _typingTimer;
        private readonly MarkdownRenderService _renderService = new MarkdownRenderService();
        private bool _skeletonLoaded;
        private ElementTheme _lastTheme = ElementTheme.Light;

        public EditorPage()
        {
            InitializeComponent();
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _typingTimer.Tick += TypingTimer_Tick;
            ApplyEditorStyle();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

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
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            base.OnNavigatedFrom(e);
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
            if (ViewModel == null)
            {
                return;
            }

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
            if (ViewModel == null || ViewModel.ActiveDocument == null)
            {
                return;
            }

            string text;
            EditorBox.Document.GetText(TextGetOptions.None, out text);
            if (text != null)
            {
                text = text.Replace('\r', '\n');
                text = text.TrimEnd('\0', '\n');
            }

            ViewModel.SetContentFromEditor(text);
            _typingTimer.Stop();
            _typingTimer.Start();
        }

        private void TypingTimer_Tick(object sender, object e)
        {
            _typingTimer.Stop();
            if (ViewModel != null)
            {
                ViewModel.RefreshPreview();
                var _ = RenderPreviewAsync();
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

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element != null)
            {
                FlyoutBase.ShowAttachedFlyout(element);
            }
        }

        private async void ExportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuFlyoutItem;
            if (item == null) return;

            // 获取格式 (md, html, pdf)
            string format = item.Tag.ToString();

            // 调用 ViewModel 的导出
            if (ViewModel != null)
            {
                await ViewModel.ExportAsync(format);
            }
        }

        private void ApplyEditorStyle()
        {
            if (EditorBox == null)
            {
                return;
            }

            var format = EditorBox.Document.GetDefaultParagraphFormat();
            format.SetLineSpacing(LineSpacingRule.Multiple, 1.5f);
            EditorBox.Document.SetDefaultParagraphFormat(format);
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
    }
}
