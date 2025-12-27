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
    /// MainPage background mode
    /// </summary>
    public enum MainPageBackgroundMode
    {
        Default,  // Bundled theme images (no overlay)
        Pure,     // Solid color only
        Custom    // User-selected image (with overlay)
    }

    /// <summary>
    /// Service to manage custom background image for MainPage
    /// </summary>
    public class BackgroundService : BaseViewModel
    {
        private const string BackgroundPathKey = "CustomBackgroundPath";
        private const string UseCustomBackgroundKey = "UseCustomBackground";
        private const string BackgroundModeKey = "MainPageBackgroundMode";

        private ImageSource _backgroundImage;
        private bool _useCustomBackground;
        private string _currentBackgroundPath;
        private MainPageBackgroundMode _backgroundMode = MainPageBackgroundMode.Default;
        private bool _lastIsDarkTheme;

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
                    // Do NOT call OnBackgroundChanged here to avoid infinite loop
                    // BackgroundChanged is fired by BackgroundMode setter or explicit calls
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
        /// Background mode: Default, Pure, or Custom
        /// </summary>
        public MainPageBackgroundMode BackgroundMode
        {
            get { return _backgroundMode; }
            set
            {
                if (_backgroundMode != value)
                {
                    _backgroundMode = value;
                    SaveSettings();
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsDefaultMode));
                    RaisePropertyChanged(nameof(IsPureMode));
                    RaisePropertyChanged(nameof(IsCustomMode));
                    RaisePropertyChanged(nameof(ShowBackground));
                    RaisePropertyChanged(nameof(ShowOverlay));
                    OnBackgroundChanged();
                }
            }
        }

        public bool IsDefaultMode
        {
            get { return _backgroundMode == MainPageBackgroundMode.Default; }
            set 
            { 
                if (value && _backgroundMode != MainPageBackgroundMode.Default) 
                    BackgroundMode = MainPageBackgroundMode.Default; 
            }
        }

        public bool IsPureMode
        {
            get { return _backgroundMode == MainPageBackgroundMode.Pure; }
            set 
            { 
                if (value && _backgroundMode != MainPageBackgroundMode.Pure) 
                    BackgroundMode = MainPageBackgroundMode.Pure; 
            }
        }

        public bool IsCustomMode
        {
            get { return _backgroundMode == MainPageBackgroundMode.Custom; }
            set 
            { 
                if (value && _backgroundMode != MainPageBackgroundMode.Custom) 
                    BackgroundMode = MainPageBackgroundMode.Custom; 
            }
        }

        /// <summary>
        /// Whether to show background image (Default or Custom mode)
        /// </summary>
        public bool ShowBackground
        {
            get { return _backgroundMode != MainPageBackgroundMode.Pure; }
        }

        /// <summary>
        /// Whether to show overlay (Custom mode only)
        /// </summary>
        public bool ShowOverlay
        {
            get { return _backgroundMode == MainPageBackgroundMode.Custom && _useCustomBackground; }
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

        /// <summary>
        /// Load background based on current mode and theme
        /// </summary>
        public void LoadBackground(bool isDarkTheme)
        {
            _lastIsDarkTheme = isDarkTheme;

            switch (_backgroundMode)
            {
                case MainPageBackgroundMode.Default:
                    LoadDefaultBackground(isDarkTheme);
                    break;

                case MainPageBackgroundMode.Pure:
                    BackgroundImage = null;
                    break;

                case MainPageBackgroundMode.Custom:
                    if (_useCustomBackground && !string.IsNullOrEmpty(_currentBackgroundPath))
                    {
                        LoadBackgroundImageAsync();
                    }
                    else
                    {
                        BackgroundImage = null;
                    }
                    break;
            }

            RaisePropertyChanged(nameof(ShowBackground));
            RaisePropertyChanged(nameof(ShowOverlay));
        }

        private void LoadDefaultBackground(bool isDarkTheme)
        {
            try
            {
                var themeFile = isDarkTheme ? "dark" : "light";
#if WINDOWS_PHONE_APP
                var uri = new Uri($"ms-appx:///Assets/Mainpage/wp-mainpagebackground-{themeFile}.jpg", UriKind.Absolute);
#else
                var uri = new Uri($"ms-appx:///Assets/Mainpage/win-mainpagebackground-{themeFile}.jpg", UriKind.Absolute);
#endif
                var bitmap = new BitmapImage(uri);
                BackgroundImage = bitmap;
            }
            catch
            {
                BackgroundImage = null;
            }
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

            if (settings.Values.ContainsKey(BackgroundModeKey))
            {
                var modeValue = settings.Values[BackgroundModeKey] as string;
                if (!string.IsNullOrEmpty(modeValue))
                {
                    Enum.TryParse(modeValue, out _backgroundMode);
                }
            }
        }

        private void SaveSettings()
        {
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[UseCustomBackgroundKey] = _useCustomBackground;
            settings.Values[BackgroundPathKey] = _currentBackgroundPath ?? string.Empty;
            settings.Values[BackgroundModeKey] = _backgroundMode.ToString();
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
