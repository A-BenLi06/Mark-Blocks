using MetroMarkdownEditor.Services;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.Windows
{
    public sealed partial class ExportSettings : SettingsFlyout
    {
        public ExportSettings()
        {
            InitializeComponent();
            DataContext = ExportSettingsService.Instance;
        }
    }
}
