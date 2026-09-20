using System;
using System.Threading.Tasks;
using System.Threading;
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
        private long _revision;
        private bool _hasPendingEditorChanges;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        public void MarkEditorChanged()
        {
            _revision++;
            _hasPendingEditorChanges = true;
            IsDirty = true;
        }

        public void CommitEditorText(string text)
        {
            Content = text;
            _hasPendingEditorChanges = false;
        }

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
                    _revision++;
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
            var readWatch = System.Diagnostics.Stopwatch.StartNew();
            var text = await FileIO.ReadTextAsync(file);
            Content = await Task.Run(() => EditorPerformancePolicy.NormalizeText(text));
            System.Diagnostics.Debug.WriteLine("Open/read+normalize: " + readWatch.ElapsedMilliseconds + " ms, chars=" + text.Length);
            IsDirty = false;
        }

        public async Task SaveAsync(StorageFile file = null)
        {
            await _saveLock.WaitAsync();
            try
            {
                var target = file ?? File;
                if (target == null)
                {
                    throw new InvalidOperationException("No file specified to save.");
                }

                var revision = _revision;
                var snapshot = Content ?? string.Empty;
                var settings = EditorSettingsService.Instance;
                var prettyIndentation = settings.PrettyIndentation;
                var indent = settings.IndentSizeOnSaveMode == EditorIndentSizeOnSave.Tab ? "\t" : new string(' ', settings.IndentSizeOnSave);
                var crlf = settings.DefaultLineEnding == EditorDefaultLineEnding.CRLF;
                var contentToSave = await Task.Run(() => EditorSettingsService.NormalizeContentForSave(snapshot, prettyIndentation, indent, crlf));
                await FileIO.WriteTextAsync(target, contentToSave);
                File = target;
                Title = target.Name;
                if (_revision == revision && !_hasPendingEditorChanges) IsDirty = false;
            }
            finally { _saveLock.Release(); }
        }
    }
}
