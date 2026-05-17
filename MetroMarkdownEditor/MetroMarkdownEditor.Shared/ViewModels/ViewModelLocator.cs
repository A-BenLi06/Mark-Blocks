using MetroMarkdownEditor.Services;

namespace MetroMarkdownEditor.ViewModels
{
    public class ViewModelLocator
    {
        public ViewModelLocator()
        {
            Theme = new ThemeService();
            RecentFiles = new RecentFileService();
            Background = new BackgroundService();
            MarkdownSettings = MarkdownSettingsService.Instance;
            EditorSettings = EditorSettingsService.Instance;
            Editor = new EditorViewModel(Theme, RecentFiles);
            Main = new MainViewModel(RecentFiles);
        }

        public ThemeService Theme { get; private set; }

        public RecentFileService RecentFiles { get; private set; }

        public BackgroundService Background { get; private set; }

        public MarkdownSettingsService MarkdownSettings { get; private set; }

        public EditorSettingsService EditorSettings { get; private set; }

        public MainViewModel Main { get; private set; }

        public EditorViewModel Editor { get; private set; }
    }
}
