using System;
using MetroMarkdownEditor.ViewModels;
using Windows.Storage;
using Windows.UI.Xaml;

namespace MetroMarkdownEditor.Services
{
    public enum PreviewThemeType
    {
        Grey = 0,
        AMOLED = 1,
        TokyoNight = 2
    }

    public enum AppWindowStyle
    {
        Classic = 0,
        Unibody = 1
    }

    public enum AppFontSizeMode
    {
        Auto = 0,
        Customized = 1
    }

    public class ThemeService : BaseViewModel
    {
        private static readonly int[] ZoomValues = { 75, 90, 100, 110, 125, 150, 175, 200 };

        private bool _isDarkTheme;
        private bool _useSystemAccentColor;
        private PreviewThemeType _previewTheme;
        private AppWindowStyle _windowStyle = AppWindowStyle.Classic;
        private AppFontSizeMode _fontSizeMode = AppFontSizeMode.Auto;
        private int _customFontSize = 18;
        private int _zoomPercent = 100;
        private bool _zoomWithCtrlMouseWheel = true;
        private bool _showStatusBar = true;
        private int _readingSpeedWordsPerMinute = 382;
        private string _lightThemeName = "Github";
        private string _darkThemeName = "Github Dark Default";
        private bool _useSeparateThemeInDarkMode = true;

        public event EventHandler ThemeChanged;

        public ThemeService()
        {
            var appTheme = Application.Current.RequestedTheme;
            _isDarkTheme = appTheme == ApplicationTheme.Dark;
            
            // Load UseSystemAccentColor from settings
            var localSettings = ApplicationData.Current.LocalSettings;
            _useSystemAccentColor = localSettings.Values.ContainsKey("UseSystemAccentColor") 
                ? (bool)localSettings.Values["UseSystemAccentColor"] 
                : false;
            
            // Load PreviewTheme from settings
            _previewTheme = localSettings.Values.ContainsKey("PreviewTheme")
                ? (PreviewThemeType)(int)localSettings.Values["PreviewTheme"]
                : PreviewThemeType.Grey;

            _windowStyle = (AppWindowStyle)ReadInt(localSettings, "Appearance.WindowStyle", (int)_windowStyle, 0, 1);
            _fontSizeMode = (AppFontSizeMode)ReadInt(localSettings, "Appearance.FontSizeMode", (int)_fontSizeMode, 0, 1);
            _customFontSize = ReadInt(localSettings, "Appearance.CustomFontSize", _customFontSize, 12, 32);
            _zoomPercent = ReadInt(localSettings, "Appearance.ZoomPercent", _zoomPercent, 75, 200);
            _zoomWithCtrlMouseWheel = ReadBool(localSettings, "Appearance.ZoomWithCtrlMouseWheel", _zoomWithCtrlMouseWheel);
            _showStatusBar = ReadBool(localSettings, "Appearance.ShowStatusBar", _showStatusBar);
            _readingSpeedWordsPerMinute = ReadInt(localSettings, "Appearance.ReadingSpeedWordsPerMinute", _readingSpeedWordsPerMinute, 50, 1000);
            _lightThemeName = ReadString(localSettings, "Appearance.LightThemeName", _lightThemeName);
            _darkThemeName = ReadString(localSettings, "Appearance.DarkThemeName", _darkThemeName);
            _useSeparateThemeInDarkMode = ReadBool(localSettings, "Appearance.UseSeparateThemeInDarkMode", _useSeparateThemeInDarkMode);
        }

        public bool IsDarkTheme
        {
            get { return _isDarkTheme; }
            set
            {
                if (_isDarkTheme != value)
                {
                    _isDarkTheme = value;
                    ApplyThemeToRoot();
                    RaisePropertyChanged();
                    OnThemeChanged();
                }
            }
        }

        public bool UseSystemAccentColor
        {
            get { return _useSystemAccentColor; }
            set
            {
                if (_useSystemAccentColor != value)
                {
                    _useSystemAccentColor = value;
                    ApplicationData.Current.LocalSettings.Values["UseSystemAccentColor"] = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged("UseAppDefaultAccent");
                    RaisePropertyChanged("AccentBrush");
                    OnThemeChanged();
                }
            }
        }

        public bool UseAppDefaultAccent
        {
            get { return !_useSystemAccentColor; }
            set { if (value) UseSystemAccentColor = false; }
        }

        public PreviewThemeType PreviewTheme
        {
            get { return _previewTheme; }
            set
            {
                if (_previewTheme != value)
                {
                    _previewTheme = value;
                    ApplicationData.Current.LocalSettings.Values["PreviewTheme"] = (int)value;
                    RaisePropertyChanged();
                    RaisePropertyChanged("IsPreviewThemeGrey");
                    RaisePropertyChanged("IsPreviewThemeAMOLED");
                    RaisePropertyChanged("IsPreviewThemeTokyoNight");
                    OnThemeChanged();
                }
            }
        }

        // Helper properties for UI binding
        public bool IsPreviewThemeGrey
        {
            get { return _previewTheme == PreviewThemeType.Grey; }
            set { if (value) PreviewTheme = PreviewThemeType.Grey; }
        }

        public bool IsPreviewThemeAMOLED
        {
            get { return _previewTheme == PreviewThemeType.AMOLED; }
            set { if (value) PreviewTheme = PreviewThemeType.AMOLED; }
        }

        public bool IsPreviewThemeTokyoNight
        {
            get { return _previewTheme == PreviewThemeType.TokyoNight; }
            set { if (value) PreviewTheme = PreviewThemeType.TokyoNight; }
        }

        public AppWindowStyle WindowStyle
        {
            get { return _windowStyle; }
            set
            {
                if (_windowStyle == value) return;
                _windowStyle = value;
                ApplicationData.Current.LocalSettings.Values["Appearance.WindowStyle"] = (int)value;
                RaisePropertyChanged();
                RaisePropertyChanged("WindowStyleIndex");
                RaisePropertyChanged("IsClassicWindowStyle");
                RaisePropertyChanged("IsUnibodyWindowStyle");
                OnThemeChanged();
            }
        }

        public int WindowStyleIndex
        {
            get { return (int)_windowStyle; }
            set { WindowStyle = (AppWindowStyle)Math.Max(0, Math.Min(1, value)); }
        }

        public bool IsClassicWindowStyle
        {
            get { return _windowStyle == AppWindowStyle.Classic; }
            set { if (value) WindowStyle = AppWindowStyle.Classic; }
        }

        public bool IsUnibodyWindowStyle
        {
            get { return _windowStyle == AppWindowStyle.Unibody; }
            set { if (value) WindowStyle = AppWindowStyle.Unibody; }
        }

        public AppFontSizeMode FontSizeMode
        {
            get { return _fontSizeMode; }
            set
            {
                if (_fontSizeMode == value) return;
                _fontSizeMode = value;
                ApplicationData.Current.LocalSettings.Values["Appearance.FontSizeMode"] = (int)value;
                RaisePropertyChanged();
                RaisePropertyChanged("FontSizeModeIndex");
                RaisePropertyChanged("IsFontSizeAuto");
                RaisePropertyChanged("IsFontSizeCustomized");
                OnThemeChanged();
            }
        }

        public int FontSizeModeIndex
        {
            get { return (int)_fontSizeMode; }
            set { FontSizeMode = (AppFontSizeMode)Math.Max(0, Math.Min(1, value)); }
        }

        public bool IsFontSizeAuto
        {
            get { return _fontSizeMode == AppFontSizeMode.Auto; }
            set { if (value) FontSizeMode = AppFontSizeMode.Auto; }
        }

        public bool IsFontSizeCustomized
        {
            get { return _fontSizeMode == AppFontSizeMode.Customized; }
            set { if (value) FontSizeMode = AppFontSizeMode.Customized; }
        }

        public int CustomFontSize
        {
            get { return _customFontSize; }
            set
            {
                var normalized = Math.Max(12, Math.Min(32, value));
                if (_customFontSize == normalized) return;
                _customFontSize = normalized;
                ApplicationData.Current.LocalSettings.Values["Appearance.CustomFontSize"] = normalized;
                RaisePropertyChanged();
                OnThemeChanged();
            }
        }

        public int ZoomPercent
        {
            get { return _zoomPercent; }
            set
            {
                var normalized = Math.Max(75, Math.Min(200, value));
                if (_zoomPercent == normalized) return;
                _zoomPercent = normalized;
                ApplicationData.Current.LocalSettings.Values["Appearance.ZoomPercent"] = normalized;
                RaisePropertyChanged();
                RaisePropertyChanged("ZoomPercentIndex");
                OnThemeChanged();
            }
        }

        public int ZoomPercentIndex
        {
            get
            {
                for (var i = 0; i < ZoomValues.Length; i++)
                {
                    if (ZoomValues[i] == _zoomPercent) return i;
                }
                return 2;
            }
            set
            {
                var index = Math.Max(0, Math.Min(ZoomValues.Length - 1, value));
                ZoomPercent = ZoomValues[index];
            }
        }

        public bool ZoomWithCtrlMouseWheel
        {
            get { return _zoomWithCtrlMouseWheel; }
            set
            {
                if (_zoomWithCtrlMouseWheel == value) return;
                _zoomWithCtrlMouseWheel = value;
                ApplicationData.Current.LocalSettings.Values["Appearance.ZoomWithCtrlMouseWheel"] = value;
                RaisePropertyChanged();
            }
        }

        public bool ShowStatusBar
        {
            get { return _showStatusBar; }
            set
            {
                if (_showStatusBar == value) return;
                _showStatusBar = value;
                ApplicationData.Current.LocalSettings.Values["Appearance.ShowStatusBar"] = value;
                RaisePropertyChanged();
            }
        }

        public int ReadingSpeedWordsPerMinute
        {
            get { return _readingSpeedWordsPerMinute; }
            set
            {
                var normalized = Math.Max(50, Math.Min(1000, value));
                if (_readingSpeedWordsPerMinute == normalized) return;
                _readingSpeedWordsPerMinute = normalized;
                ApplicationData.Current.LocalSettings.Values["Appearance.ReadingSpeedWordsPerMinute"] = normalized;
                RaisePropertyChanged();
            }
        }

        public string LightThemeName
        {
            get { return _lightThemeName; }
            set
            {
                var normalized = value ?? string.Empty;
                if (_lightThemeName == normalized) return;
                _lightThemeName = normalized;
                ApplicationData.Current.LocalSettings.Values["Appearance.LightThemeName"] = normalized;
                RaisePropertyChanged();
            }
        }

        public string DarkThemeName
        {
            get { return _darkThemeName; }
            set
            {
                var normalized = value ?? string.Empty;
                if (_darkThemeName == normalized) return;
                _darkThemeName = normalized;
                ApplicationData.Current.LocalSettings.Values["Appearance.DarkThemeName"] = normalized;
                RaisePropertyChanged();
            }
        }

        public bool UseSeparateThemeInDarkMode
        {
            get { return _useSeparateThemeInDarkMode; }
            set
            {
                if (_useSeparateThemeInDarkMode == value) return;
                _useSeparateThemeInDarkMode = value;
                ApplicationData.Current.LocalSettings.Values["Appearance.UseSeparateThemeInDarkMode"] = value;
                RaisePropertyChanged();
            }
        }

        public void ResetZoom()
        {
            ZoomPercent = 100;
        }

        public void ResetReadingSpeed()
        {
            ReadingSpeedWordsPerMinute = 382;
        }

        public global::Windows.UI.Xaml.Media.SolidColorBrush AccentBrush
        {
            get
            {
                if (_useSystemAccentColor)
                {
#if WINDOWS_PHONE_APP
                    // Windows Phone 8.1: Use PhoneAccentBrush from theme resources
                    var accentBrush = Application.Current.Resources["PhoneAccentBrush"] as global::Windows.UI.Xaml.Media.SolidColorBrush;
                    if (accentBrush != null)
                    {
                        return accentBrush;
                    }
                    // Fallback to default cyan
                    return new global::Windows.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 38, 212, 218));
#else
                    // Windows 8.1: Use SystemAccentColor from theme resources
                    try
                    {
                        // First try to get the accent color as a Color resource
                        if (Application.Current.Resources.ContainsKey("SystemAccentColor"))
                        {
                            var accentColor = (global::Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
                            return new global::Windows.UI.Xaml.Media.SolidColorBrush(accentColor);
                        }
                        // Fallback to default cyan
                        return new global::Windows.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 38, 212, 218));
                    }
                    catch
                    {
                        // Fallback to default cyan if resource access fails
                        return new global::Windows.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 38, 212, 218));
                    }
#endif
                }
                else
                {
                    // Default cyan color #26d4da
                    return new global::Windows.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 38, 212, 218));
                }
            }
        }

        public void ApplyThemeToRoot()
        {
            var root = Window.Current.Content as FrameworkElement;
            if (root != null)
            {
                root.RequestedTheme = _isDarkTheme ? ElementTheme.Dark : ElementTheme.Light;
            }
        }

        public string GetPreviewBackground()
        {
            if (!_isDarkTheme)
            {
                return "#FFFFFF"; // Light mode always white
            }
            
            switch (_previewTheme)
            {
                case PreviewThemeType.AMOLED:
                    return "#000000";
                case PreviewThemeType.TokyoNight:
                    return "#1a1b26";
                case PreviewThemeType.Grey:
                default:
                    return "#1d1d1d";
            }
        }

        public string BuildCss()
        {
            var background = GetPreviewBackground();
            var foreground = _isDarkTheme ? "#F3F3F3" : "#1A1A1A";
            var accent = _isDarkTheme ? "#63B0F2" : "#0078D7";
            var border = _isDarkTheme ? "#2D2D2D" : "#E0E0E0";
            var codeBackground = _isDarkTheme ? "#2D2D2D" : "#F5F5F5";
            var codeAccent = _isDarkTheme ? "#00E5FF" : "#00ACC1";
            var baseFontSize = _fontSizeMode == AppFontSizeMode.Customized ? _customFontSize : 16;
            var effectiveFontSize = Math.Max(10, Math.Min(40, baseFontSize * _zoomPercent / 100));

            return
                @"<style>
                    body { font-family:'Segoe UI','Helvetica Neue',sans-serif; padding:32px; margin:0; background:" + background + @"; color:" + foreground + @"; line-height:1.6; font-size:" + effectiveFontSize + @"px; }
                    h1,h2,h3,h4 { margin-top:24px; margin-bottom:12px; font-weight:600; }
                    p { margin: 12px 0; line-height:1.6; }
                    a { color:" + accent + @"; text-decoration:none; }
                    a:hover { text-decoration:underline; }
                    img { max-width:100%; height:auto; display:block; margin:12px 0; }
                    pre { padding:12px; overflow-x:auto; border-radius:4px; }
                    code { font-family:'Consolas','Courier New',monospace; }
                    ul { padding-left:20px; }
                    li { margin:6px 0; }
                    table { width:100%; border-collapse:collapse; margin:12px 0; }
                    th, td { border:1px solid " + border + @"; padding:8px; text-align:left; }
                    blockquote { border-left:4px solid " + accent + @"; padding-left:12px; margin:12px 0; color:" + foreground + @"; }
                </style>";
        }

        private static bool ReadBool(ApplicationDataContainer localSettings, string key, bool defaultValue)
        {
            object raw;
            if (!localSettings.Values.TryGetValue(key, out raw) || raw == null) return defaultValue;
            bool parsed;
            return bool.TryParse(raw.ToString(), out parsed) ? parsed : defaultValue;
        }

        private static int ReadInt(ApplicationDataContainer localSettings, string key, int defaultValue, int min, int max)
        {
            object raw;
            if (!localSettings.Values.TryGetValue(key, out raw) || raw == null) return defaultValue;
            int parsed;
            return int.TryParse(raw.ToString(), out parsed) ? Math.Max(min, Math.Min(max, parsed)) : defaultValue;
        }

        private static string ReadString(ApplicationDataContainer localSettings, string key, string defaultValue)
        {
            object raw;
            return localSettings.Values.TryGetValue(key, out raw) && raw != null ? raw.ToString() : defaultValue;
        }

        protected virtual void OnThemeChanged()
        {
            var handler = ThemeChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
