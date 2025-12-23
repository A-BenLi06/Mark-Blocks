using System;
using System.Threading.Tasks;
using MetroMarkdownEditor.ViewModels;
using Windows.Storage;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace MetroMarkdownEditor.Services
{
    /// <summary>
    /// Service to manage custom background image for MainPage
    /// </summary>
    public class BackgroundService : BaseViewModel
    {
        private const string BackgroundPathKey = "CustomBackgroundPath";
        private const string UseCustomBackgroundKey = "UseCustomBackground";

        private ImageSource _backgroundImage;
        private bool _useCustomBackground;
        private string _currentBackgroundPath;

        public event EventHandler BackgroundChanged;

        public BackgroundService()
        {
            LoadSettings();
        }

        /// <summary>
        /// The current background image source
        /// </summary>
        public ImageSource BackgroundImage
        {
            get { return _backgroundImage; }
            private set
            {
                if (_backgroundImage != value)
                {
                    _backgroundImage = value;
                    RaisePropertyChanged();
                    OnBackgroundChanged();
                }
            }
        }

        /// <summary>
        /// Whether to use custom background
        /// </summary>
        public bool UseCustomBackground
        {
            get { return _useCustomBackground; }
            set
            {
                if (_useCustomBackground != value)
                {
                    _useCustomBackground = value;
                    SaveSettings();
                    RaisePropertyChanged();
                    OnBackgroundChanged();
                }
            }
        }

        /// <summary>
        /// Current background image path (ms-appdata URI)
        /// </summary>
        public string CurrentBackgroundPath
        {
            get { return _currentBackgroundPath; }
            private set
            {
                if (_currentBackgroundPath != value)
                {
                    _currentBackgroundPath = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Returns the overlay color based on current theme
        /// Dark mode: Black with 70% opacity
        /// Light mode: White with 70% opacity
        /// </summary>
        public SolidColorBrush GetOverlayBrush(bool isDarkTheme)
        {
            if (isDarkTheme)
            {
                // Black with 70% opacity (B2 = 178/255 ≈ 70%)
                return new SolidColorBrush(Color.FromArgb(178, 0, 0, 0));
            }
            else
            {
                // White with 70% opacity
                return new SolidColorBrush(Color.FromArgb(178, 255, 255, 255));
            }
        }

        /// <summary>
        /// Set background from a file picked by user
        /// </summary>
        public async Task<bool> SetBackgroundFromFileAsync(StorageFile file)
        {
            if (file == null) return false;

            try
            {
                // Use BackgroundHistoryService to add the image
                var path = await BackgroundHistoryService.Instance.AddImageAsync(file);
                if (!string.IsNullOrEmpty(path))
                {
                    CurrentBackgroundPath = path;
                    UseCustomBackground = true;
                    await LoadBackgroundImageAsync();
                    SaveSettings();
                    return true;
                }
            }
            catch
            {
                // Ignore errors
            }

            return false;
        }

        /// <summary>
        /// Set background from a history item
        /// </summary>
        public async Task SetBackgroundFromHistoryAsync(BackgroundHistoryItem item)
        {
            if (item == null) return;

            CurrentBackgroundPath = item.LocalPath;
            UseCustomBackground = true;
            await LoadBackgroundImageAsync();
            SaveSettings();
        }

        /// <summary>
        /// Clear custom background and return to default
        /// </summary>
        public void ClearBackground()
        {
            CurrentBackgroundPath = null;
            UseCustomBackground = false;
            BackgroundImage = null;
            SaveSettings();
        }

        /// <summary>
        /// Initialize and load the background image if set
        /// </summary>
        public async Task InitializeAsync()
        {
            await BackgroundHistoryService.Instance.LoadHistoryAsync();
            if (UseCustomBackground && !string.IsNullOrEmpty(CurrentBackgroundPath))
            {
                await LoadBackgroundImageAsync();
            }
        }

        private Task LoadBackgroundImageAsync()
        {
            if (string.IsNullOrEmpty(CurrentBackgroundPath))
            {
                BackgroundImage = null;
                return Task.FromResult(0);
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.UriSource = new Uri(CurrentBackgroundPath, UriKind.Absolute);
                BackgroundImage = bitmap;
            }
            catch
            {
                BackgroundImage = null;
            }

            return Task.FromResult(0);
        }

        private void LoadSettings()
        {
            var settings = ApplicationData.Current.LocalSettings;
            
            if (settings.Values.ContainsKey(UseCustomBackgroundKey))
            {
                _useCustomBackground = (bool)settings.Values[UseCustomBackgroundKey];
            }
            
            if (settings.Values.ContainsKey(BackgroundPathKey))
            {
                _currentBackgroundPath = settings.Values[BackgroundPathKey] as string;
            }
        }

        private void SaveSettings()
        {
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[UseCustomBackgroundKey] = _useCustomBackground;
            settings.Values[BackgroundPathKey] = _currentBackgroundPath ?? string.Empty;
        }

        protected virtual void OnBackgroundChanged()
        {
            var handler = BackgroundChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
