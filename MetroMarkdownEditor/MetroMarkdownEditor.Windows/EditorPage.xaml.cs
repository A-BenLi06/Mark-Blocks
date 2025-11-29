using System.ComponentModel;
using System.Linq;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Controls.Primitives;

using Windows.UI.Core;

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

        private void SourceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ViewModel == null || ViewModel.ActiveDocument == null)
            {
                return;
            }

            ViewModel.SetContentFromEditor(SourceTextBox.Text);
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
            if (ViewModel != null && ViewModel.ActiveDocument != null && SourceTextBox != null)
            {
                SourceTextBox.Text = ViewModel.ActiveDocument.Content ?? string.Empty;
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
    }
}
