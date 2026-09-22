using MetroMarkdownEditor.Services;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.Windows
{
    public sealed partial class DevSettings : SettingsFlyout
    {
        public DevSettings()
        {
            InitializeComponent();
            DataContext = DevSettingsService.Instance;
        }
    }
}
