using MetroMarkdownEditor.Services;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.Windows
{
    public sealed partial class ImageSettings : SettingsFlyout
    {
        public ImageSettings()
        {
            InitializeComponent();
            DataContext = ImageSettingsService.Instance;
        }
    }
}
