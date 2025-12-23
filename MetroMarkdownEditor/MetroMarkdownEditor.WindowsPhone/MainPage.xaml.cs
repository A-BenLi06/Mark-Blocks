using MetroMarkdownEditor.ViewModels;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.WindowsPhone;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.Storage.Pickers;
using System;
using System.Collections.Generic;

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

        private void RecentFiles_ItemClick(object sender, ItemClickEventArgs e)
        {
            var request = new EditorNavigationRequest
            {
                Mode = EditorLaunchMode.Recent,
                RecentFile = e.ClickedItem as Services.RecentFileItem
            };
            Frame.Navigate(typeof(EditorPage), request);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            UpdateOverlayColor();
        }

        private void OnBackgroundChanged(object sender, EventArgs e)
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

        private void ChooseBackground_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");

            // Set flag so App.OnActivated knows we're picking a background image
            App.IsPickingBackgroundImage = true;
            
            // For Windows Phone 8.1, we need to use PickSingleFileAndContinue
            picker.PickSingleFileAndContinue();
        }

        private void ResetBackground_Click(object sender, RoutedEventArgs e)
        {
            BackgroundSvc.ClearBackground();
        }
    }
}

