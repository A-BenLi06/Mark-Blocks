using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MetroMarkdownEditor.Common;
using MetroMarkdownEditor.Services;

namespace MetroMarkdownEditor.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly RecentFileService _recentFiles;

        public MainViewModel(RecentFileService recentFiles)
        {
            _recentFiles = recentFiles;
            NewCommand = new RelayCommand(_ => RequestNavigation(EditorLaunchMode.New));
            OpenCommand = new RelayCommand(_ => RequestNavigation(EditorLaunchMode.OpenPicker));
            OpenRecentCommand = new RelayCommand(item => RequestNavigation(EditorLaunchMode.Recent, item as RecentFileItem));
        }

        public event EventHandler<EditorNavigationRequest> NavigationRequested;

        public ObservableCollection<RecentFileItem> RecentFiles
        {
            get { return _recentFiles.Items; }
        }

        public RelayCommand NewCommand { get; private set; }

        public RelayCommand OpenCommand { get; private set; }

        public RelayCommand OpenRecentCommand { get; private set; }

        public Task InitializeAsync()
        {
            return _recentFiles.InitializeAsync();
        }

        private void RequestNavigation(EditorLaunchMode mode, RecentFileItem recent = null)
        {
            var handler = NavigationRequested;
            if (handler != null)
            {
                handler(this, new EditorNavigationRequest
                {
                    Mode = mode,
                    RecentFile = recent
                });
            }
        }
    }
}
