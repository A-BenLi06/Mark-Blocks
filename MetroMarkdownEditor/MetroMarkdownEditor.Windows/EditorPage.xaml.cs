using System.ComponentModel;
using System.Linq;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.ViewModels;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MetroMarkdownEditor
{
    public sealed partial class EditorPage : Page
    {
        private EditorViewModel ViewModel
        {
            get { return DataContext as EditorViewModel; }
        }

        public EditorPage()
        {
            InitializeComponent();
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
    }
}
