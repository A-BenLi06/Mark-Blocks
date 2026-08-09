using MetroMarkdownEditor.Services;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.Windows
{
    public sealed partial class MarkdownSettings : SettingsFlyout
    {
        public MarkdownSettings()
        {
            InitializeComponent();
            DataContext = MarkdownSettingsService.Instance;
        }
    }
}
