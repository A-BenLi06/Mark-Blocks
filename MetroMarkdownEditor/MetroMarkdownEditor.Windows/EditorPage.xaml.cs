using System.ComponentModel;
using System.Linq;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.ViewModels;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Controls.Primitives;

namespace MetroMarkdownEditor
{
    public sealed partial class EditorPage : Page
    {
        private EditorViewModel ViewModel
        {
            get { return DataContext as EditorViewModel; }
        }

        private readonly DispatcherTimer _typingTimer;

        public EditorPage()
        {
            InitializeComponent();
            _typingTimer = new DispatcherTimer();
            _typingTimer.Interval = System.TimeSpan.FromMilliseconds(500);
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

            RenderPreview();
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
            if (e.PropertyName == "PreviewHtml")
            {
                RenderPreview();
            }
            else if (e.PropertyName == "ActiveDocument")
            {
                SyncEditorText();
            }
        }

        private void RenderPreview()
        {
            if (ViewModel != null && !string.IsNullOrEmpty(ViewModel.PreviewHtml))
            {
                PreviewWebView.NavigateToString(ViewModel.PreviewHtml);
            }
        }

        private void BackButton_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
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
            // 1. 获取文本 (此时里面全是 \r)
            EditorBox.Document.GetText(TextGetOptions.None, out text);
            
            if (text != null)
            {
                // 【核心修复】将 \r 替换�?\n，让 Markdown 解析器能识别换行
                text = text.Replace('\r', '\n');

                // 2. 清理末尾 (RichEditBox 总是会在最后多给一�?\0 和一个隐藏的换行)
                // 注意：因为上面已经把 \r 换成�?\n，所以这里要 TrimEnd \n
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
                RenderPreview();
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
            if (ViewModel == null)
            {
                return;
            }

            var item = sender as MenuFlyoutItem;
            var tag = item != null ? item.Tag as string : null;
            await ViewModel.ExportAsync(tag);
            RenderPreview();
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
    }
}
