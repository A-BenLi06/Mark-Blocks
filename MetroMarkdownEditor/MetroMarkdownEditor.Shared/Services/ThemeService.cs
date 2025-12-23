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

    public class ThemeService : BaseViewModel
    {
        private bool _isDarkTheme;
        private bool _useSystemAccentColor;
        private PreviewThemeType _previewTheme;

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
                    RaisePropertyChanged("AccentBrush");
                    OnThemeChanged();
                }
            }
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

        private string GetPreviewBackground()
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

            return
                @"<style>
                    body { font-family:'Segoe UI','Helvetica Neue',sans-serif; padding:32px; margin:0; background:" + background + @"; color:" + foreground + @"; line-height:1.6; }
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
