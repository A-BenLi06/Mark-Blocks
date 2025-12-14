using MetroMarkdownEditor.Services;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.WindowsPhone
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
