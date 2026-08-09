using MetroMarkdownEditor.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.Windows
{
    public sealed partial class EditorSettings : SettingsFlyout
    {
        public EditorSettings()
        {
            InitializeComponent();
            DataContext = EditorSettingsService.Instance;
        }

        private void TurnOffTypewriter_Click(object sender, RoutedEventArgs e)
        {
            EditorSettingsService.Instance.TurnOffTypewriterFocusMode();
        }
    }
}
