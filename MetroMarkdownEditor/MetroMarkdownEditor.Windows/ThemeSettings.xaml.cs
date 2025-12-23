using System;
using MetroMarkdownEditor.ViewModels;
using MetroMarkdownEditor.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace MetroMarkdownEditor.Windows
{
    public sealed partial class ThemeSettings : SettingsFlyout
    {
        private BackgroundService _backgroundService;

        public ThemeSettings()
        {
            InitializeComponent();

            var locator = App.Current.Resources["Locator"] as ViewModelLocator;
            if (locator != null)
            {
                // Set Theme as DataContext for the dark theme toggle
                DataContext = locator.Theme;
                _backgroundService = locator.Background;
                
                // Update Reset button visibility based on background state
                UpdateResetButtonVisibility();
            }
        }

        private void UpdateResetButtonVisibility()
        {
            if (ResetImageButton != null && _backgroundService != null)
            {
                ResetImageButton.Visibility = _backgroundService.UseCustomBackground 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        }

        private async void ChooseBackground_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");

            var file = await picker.PickSingleFileAsync();
            if (file != null && _backgroundService != null)
            {
                await _backgroundService.SetBackgroundFromFileAsync(file);
                UpdateResetButtonVisibility();
            }
        }

        private void ResetBackground_Click(object sender, RoutedEventArgs e)
        {
            if (_backgroundService != null)
            {
                _backgroundService.ClearBackground();
                UpdateResetButtonVisibility();
            }
        }
    }
}

