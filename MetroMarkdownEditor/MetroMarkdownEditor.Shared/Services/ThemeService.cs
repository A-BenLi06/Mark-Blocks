using System;
using MetroMarkdownEditor.ViewModels;
using Windows.UI.Xaml;

namespace MetroMarkdownEditor.Services
{
    public class ThemeService : BaseViewModel
    {
        private bool _isDarkTheme;

        public event EventHandler ThemeChanged;

        public ThemeService()
        {
            var appTheme = Application.Current.RequestedTheme;
            _isDarkTheme = appTheme == ApplicationTheme.Dark;
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

        public void ApplyThemeToRoot()
        {
            var root = Window.Current.Content as FrameworkElement;
            if (root != null)
            {
                root.RequestedTheme = _isDarkTheme ? ElementTheme.Dark : ElementTheme.Light;
            }
        }

        public string BuildCss()
        {
            var background = _isDarkTheme ? "#1E1E1E" : "#FFFFFF";
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
                    pre { background:" + codeBackground + @"; color:" + foreground + @"; padding:12px; overflow-x:auto; border:1px solid " + border + @"; border-radius:4px; }
                    code { font-family:'Consolas','Courier New',monospace; color:" + codeAccent + @"; }
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
