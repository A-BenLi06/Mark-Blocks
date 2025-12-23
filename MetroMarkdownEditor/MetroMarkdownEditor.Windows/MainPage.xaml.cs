using MetroMarkdownEditor.ViewModels;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.Windows;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace MetroMarkdownEditor
{
    public sealed partial class MainPage : Page
    {
        private MainViewModel ViewModel
        {
            get { return DataContext as MainViewModel; }
        }

        private BackgroundService BackgroundSvc
        {
            get { return ((ViewModelLocator)App.Current.Resources["Locator"]).Background; }
        }

        private ThemeService ThemeSvc
        {
            get { return ((ViewModelLocator)App.Current.Resources["Locator"]).Theme; }
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

            // Subscribe to theme changes
            ThemeSvc.ThemeChanged += OnThemeChanged;
            
            // Subscribe to background changes
            BackgroundSvc.BackgroundChanged += OnBackgroundChanged;

            // Initialize background service
            await BackgroundSvc.InitializeAsync();
            
            // Update overlay color based on current theme
            UpdateOverlayColor();

            if (ViewModel != null)
            {
                // IMPORTANT: Always unsubscribe first to prevent duplicate subscriptions
                // (ViewModel is a singleton, so subscriptions persist across navigations)
                ViewModel.NavigationRequested -= OnNavigationRequested;
                ViewModel.NavigationRequested += OnNavigationRequested;
                await ViewModel.InitializeAsync();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Only update overlay, don't subscribe to events (handled in OnNavigatedTo)
            UpdateOverlayColor();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.NavigationRequested -= OnNavigationRequested;
            }
            ThemeSvc.ThemeChanged -= OnThemeChanged;
            BackgroundSvc.BackgroundChanged -= OnBackgroundChanged;
        }

        private void OnNavigationRequested(object sender, EditorNavigationRequest e)
        {
            Frame.Navigate(typeof(EditorPage), e);
        }



        private void OnThemeChanged(object sender, System.EventArgs e)
        {
            UpdateOverlayColor();
        }

        private void OnBackgroundChanged(object sender, System.EventArgs e)
        {
            UpdateOverlayColor();
        }

        /// <summary>
        /// Updates the overlay color based on current theme
        /// Dark mode: 70% black overlay
        /// Light mode: 70% white overlay
        /// </summary>
        private void UpdateOverlayColor()
        {
            if (BackgroundOverlay != null && BackgroundSvc.UseCustomBackground)
            {
                BackgroundOverlay.Fill = BackgroundSvc.GetOverlayBrush(ThemeSvc.IsDarkTheme);
            }
        }
    }
}

