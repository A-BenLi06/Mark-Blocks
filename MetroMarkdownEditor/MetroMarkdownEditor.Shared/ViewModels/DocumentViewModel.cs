using System;
using System.Threading.Tasks;
using MetroMarkdownEditor.Services;
using Windows.Storage;

namespace MetroMarkdownEditor.ViewModels
{
    public class DocumentViewModel : BaseViewModel
    {
        private StorageFile _file;
        private string _content = string.Empty;
        private bool _isDirty;
        private string _title = "Untitled";
        private string _token;

        public StorageFile File
        {
            get { return _file; }
            private set
            {
                _file = value;
                RaisePropertyChanged();
                RaisePropertyChanged("Path");
            }
        }

        public string Token
        {
            get { return _token; }
            set
            {
                _token = value;
                RaisePropertyChanged();
            }
        }

        public string Path
        {
            get { return File != null ? File.Path : string.Empty; }
        }

        public string Title
        {
            get { return _title; }
            set
            {
                if (_title != value)
                {
                    _title = value;
                    RaisePropertyChanged();
                }
            }
        }

        public string Content
        {
            get { return _content; }
            set
            {
                var newValue = value ?? string.Empty;
                if (_content != newValue)
                {
                    _content = newValue;
                    IsDirty = true;
                    RaisePropertyChanged();
                }
            }
        }

        public bool IsDirty
        {
            get { return _isDirty; }
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    RaisePropertyChanged();
                }
            }
        }

        public async Task LoadAsync(StorageFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException("file");
            }

            File = file;
            Title = file.Name;
            Content = await FileIO.ReadTextAsync(file);
            IsDirty = false;
        }

        public async Task SaveAsync(StorageFile file = null)
        {
            var target = file ?? File;
            if (target == null)
            {
                throw new InvalidOperationException("No file specified to save.");
            }

            var contentToSave = EditorSettingsService.Instance.NormalizeContentForSave(Content ?? string.Empty);
            await FileIO.WriteTextAsync(target, contentToSave);
            File = target;
            Title = target.Name;
            IsDirty = false;
        }
    }
}
