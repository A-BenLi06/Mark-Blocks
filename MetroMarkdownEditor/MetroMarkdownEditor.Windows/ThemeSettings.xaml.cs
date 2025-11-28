using MetroMarkdownEditor.ViewModels;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor
{
    public sealed partial class ThemeSettings : SettingsFlyout
    {
        public ThemeSettings()
        {
            InitializeComponent();

            if (DataContext == null)
            {
                var locator = App.Current.Resources["Locator"] as ViewModelLocator;
                if (locator != null)
                {
                    DataContext = locator.Theme;
                }
            }
        }
    }
}
