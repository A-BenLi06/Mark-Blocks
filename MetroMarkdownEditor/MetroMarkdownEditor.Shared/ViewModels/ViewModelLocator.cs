using MetroMarkdownEditor.Services;

namespace MetroMarkdownEditor.ViewModels
{
    public class ViewModelLocator
    {
        public ViewModelLocator()
        {
            Theme = new ThemeService();
            RecentFiles = new RecentFileService();
            Editor = new EditorViewModel(Theme, RecentFiles);
            Main = new MainViewModel(RecentFiles);
        }

        public ThemeService Theme { get; private set; }

        public RecentFileService RecentFiles { get; private set; }

        public MainViewModel Main { get; private set; }

        public EditorViewModel Editor { get; private set; }
    }
}
