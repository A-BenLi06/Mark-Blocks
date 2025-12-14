using MetroMarkdownEditor.ViewModels;
using MetroMarkdownEditor.WindowsPhone;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MetroMarkdownEditor
{
    public sealed partial class MainPage : Page
    {
        private MainViewModel ViewModel
        {
            get { return DataContext as MainViewModel; }
        }

        public MainPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (ViewModel != null)
            {
                ViewModel.NavigationRequested += OnNavigationRequested;
                await ViewModel.InitializeAsync();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.NavigationRequested -= OnNavigationRequested;
                ViewModel.NavigationRequested += OnNavigationRequested;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.NavigationRequested -= OnNavigationRequested;
            }
        }

        private void OnNavigationRequested(object sender, EditorNavigationRequest e)
        {
            Frame.Navigate(typeof(EditorPage), e);
        }

        private void RecentFiles_ItemClick(object sender, ItemClickEventArgs e)
        {
            var request = new EditorNavigationRequest
            {
                Mode = EditorLaunchMode.Recent,
                RecentFile = e.ClickedItem as Services.RecentFileItem
            };
            Frame.Navigate(typeof(EditorPage), request);
        }
    }
}
