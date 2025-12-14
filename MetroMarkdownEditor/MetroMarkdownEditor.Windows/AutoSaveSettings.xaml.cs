using MetroMarkdownEditor.Services;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.Windows
{
    public sealed partial class AutoSaveSettings : SettingsFlyout
    {
        public AutoSaveSettings()
        {
            InitializeComponent();
            DataContext = AutoSaveService.Instance;
        }
    }
}
