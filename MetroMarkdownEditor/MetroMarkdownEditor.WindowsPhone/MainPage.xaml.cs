using MetroMarkdownEditor.ViewModels;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.WindowsPhone;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI;
using Windows.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.Phone.UI.Input;
using System;

namespace MetroMarkdownEditor
{
    public sealed partial class MainPage : Page
    {
        private MainViewModel ViewModel => DataContext as MainViewModel;

        private BackgroundService BackgroundSvc =>
            ((ViewModelLocator)App.Current.Resources["Locator"]).Background;

        private ThemeService ThemeSvc =>
            ((ViewModelLocator)App.Current.Resources["Locator"]).Theme;

        public MainPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Register hardware back button
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;

            // Subscribe to theme and background changes
            ThemeSvc.ThemeChanged += OnThemeChanged;
            BackgroundSvc.BackgroundChanged += OnBackgroundChanged;

            // Initialize services
            await BackgroundSvc.InitializeAsync();
            
            // Load background based on current mode and theme
            BackgroundSvc.LoadBackground(ThemeSvc.IsDarkTheme);
            UpdateOverlayColor();

            // Initialize ViewModel
            if (ViewModel != null)
            {
                await ViewModel.InitializeAsync();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            // Unregister handlers
            HardwareButtons.BackPressed -= HardwareButtons_BackPressed;
            ThemeSvc.ThemeChanged -= OnThemeChanged;
            BackgroundSvc.BackgroundChanged -= OnBackgroundChanged;
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            // On MainPage, let system handle back (exit app)
            // Do NOT set e.Handled = true
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            BackgroundSvc.LoadBackground(ThemeSvc.IsDarkTheme);
            UpdateOverlayColor();
        }

        private void OnBackgroundChanged(object sender, EventArgs e)
        {
            BackgroundSvc.LoadBackground(ThemeSvc.IsDarkTheme);
            UpdateOverlayColor();
        }

        private void UpdateOverlayColor()
        {
            if (BackgroundOverlay != null && BackgroundSvc.ShowOverlay)
            {
                BackgroundOverlay.Fill = BackgroundSvc.GetOverlayBrush(ThemeSvc.IsDarkTheme);
            }
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".markdown");
            picker.FileTypeFilter.Add(".txt");

            App.IsPickingEditorFile = true;
            picker.PickSingleFileAndContinue();
        }

        private void WrittingButton_Click(object sender, RoutedEventArgs e)
        {
            var request = new EditorNavigationRequest
            {
                Mode = EditorLaunchMode.New
            };
            Frame.Navigate(typeof(EditorPage), request);
        }

        private void RecentFiles_ItemClick(object sender, ItemClickEventArgs e)
        {
            var recentFile = e.ClickedItem as RecentFileItem;
            if (recentFile == null) return;

            var request = new EditorNavigationRequest
            {
                Mode = EditorLaunchMode.Recent,
                RecentFile = recentFile
            };
            Frame.Navigate(typeof(EditorPage), request);
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

            App.IsPickingBackgroundImage = true;
            picker.PickSingleFileAndContinue();
        }
    }
}
