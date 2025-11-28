using MetroMarkdownEditor.Services;

namespace MetroMarkdownEditor.ViewModels
{
    public enum EditorLaunchMode
    {
        New,
        OpenPicker,
        Recent
    }

    public class EditorNavigationRequest
    {
        public EditorLaunchMode Mode { get; set; }

        public RecentFileItem RecentFile { get; set; }
    }
}
