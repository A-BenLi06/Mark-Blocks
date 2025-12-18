using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using MetroMarkdownEditor.Common;
using MetroMarkdownEditor.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Text;

#if WINDOWS_PHONE_APP
using Windows.ApplicationModel.Activation;
#endif

namespace MetroMarkdownEditor.ViewModels
{
    public class EditorViewModel : BaseViewModel
    {
        private DocumentViewModel _activeDocument;
        private EditorViewMode _viewMode = EditorViewMode.Split;
        private readonly ThemeService _themeService;
        private readonly RecentFileService _recentFiles;
        private readonly MarkdownRenderService _renderService = new MarkdownRenderService();
        private bool _suppressPreviewUpdate;
        private ElementTheme _currentTheme = ElementTheme.Light;
        private string _previewContent;
        private string _previewCss;
        private IReadOnlyList<MarkdownBlock> _previewBlocks = new List<MarkdownBlock>();
        private bool _isSaving;
        private string _saveStatusText;
        private List<OutlineItem> _outlineItems;
        private int _scrollToLineRequest = -1;
        private string _exportContent; // For WP8.1 continuation

        public EditorViewModel(ThemeService themeService, RecentFileService recentFiles)
        {
            _themeService = themeService;
            _recentFiles = recentFiles;
            OpenDocuments = new ObservableCollection<DocumentViewModel>();

            _themeService.ThemeChanged += (s, e) => UpdatePreview();

            NewCommand = new RelayCommand(async _ => await CreateNewAsync());
            OpenCommand = new RelayCommand(async _ => await OpenFromPickerAsync());
            SaveCommand = new RelayCommand(async _ => await SaveAsync());
            RemoveCommand = new RelayCommand(async _ => await RemoveActiveAsync());
            OpenRecentCommand = new RelayCommand(async o => await OpenRecentAsync(o as RecentFileItem));
            ApplyFormattingCommand = new RelayCommand(o => ApplyFormatting(o as string));
            ToggleViewModeCommand = new RelayCommand(_ => CycleViewMode());
            CloseDocumentCommand = new RelayCommand(o => CloseDocument(o as DocumentViewModel));
            ExportCommand = new RelayCommand(async o => await ExportAsync(o as string));
        }

        public ObservableCollection<DocumentViewModel> OpenDocuments { get; private set; }

        public DocumentViewModel ActiveDocument
        {
            get { return _activeDocument; }
            set
            {
                if (_activeDocument == value)
                {
                    return;
                }

                if (_activeDocument != null)
                {
                    _activeDocument.PropertyChanged -= OnDocumentPropertyChanged;
                }

                _activeDocument = value;

                if (_activeDocument != null)
                {
                    _activeDocument.PropertyChanged += OnDocumentPropertyChanged;
                }

                RaisePropertyChanged();
                UpdatePreview();
            }
        }

        public EditorViewMode ViewMode
        {
            get { return _viewMode; }
            set
            {
                if (_viewMode != value)
                {
                    _viewMode = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged("IsOutlineVisible");
                }
            }
        }

        public RelayCommand NewCommand { get; private set; }
        public RelayCommand OpenCommand { get; private set; }
        public RelayCommand SaveCommand { get; private set; }
        public RelayCommand RemoveCommand { get; private set; }
        public RelayCommand OpenRecentCommand { get; private set; }
        public RelayCommand ApplyFormattingCommand { get; private set; }
        public RelayCommand ToggleViewModeCommand { get; private set; }
        public RelayCommand CloseDocumentCommand { get; private set; }
        public RelayCommand ExportCommand { get; private set; }

        public string PreviewContent
        {
            get { return _previewContent; }
            private set
            {
                if (_previewContent != value)
                {
                    _previewContent = value;
                    RaisePropertyChanged();
                }
            }
        }

        public string PreviewCss
        {
            get { return _previewCss; }
            private set
            {
                if (_previewCss != value)
                {
                    _previewCss = value;
                    RaisePropertyChanged();
                }
            }
        }

        public IReadOnlyList<MarkdownBlock> PreviewBlocks
        {
            get { return _previewBlocks; }
            private set
            {
                if (!ReferenceEquals(_previewBlocks, value))
                {
                    _previewBlocks = value ?? new List<MarkdownBlock>();
                    RaisePropertyChanged();
                }
            }
        }

        public bool IsOutlineVisible
        {
            get { return ViewMode == EditorViewMode.Preview; }
        }

        public bool IsSaving
        {
            get { return _isSaving; }
            private set
            {
                if (_isSaving != value)
                {
                    _isSaving = value;
                    RaisePropertyChanged();
                }
            }
        }

        public string SaveStatusText
        {
            get { return _saveStatusText; }
            private set
            {
                if (_saveStatusText != value)
                {
                    _saveStatusText = value;
                    RaisePropertyChanged();
                }
            }
        }

        public List<OutlineItem> OutlineItems
        {
            get { return _outlineItems; }
            private set
            {
                if (_outlineItems != value)
                {
                    _outlineItems = value;
                    RaisePropertyChanged();
                }
            }
        }

        public int ScrollToLineRequest
        {
            get { return _scrollToLineRequest; }
            set
            {
                if (_scrollToLineRequest != value)
                {
                    _scrollToLineRequest = value;
                    RaisePropertyChanged();
                }
            }
        }

        public void SetTheme(ElementTheme theme)
        {
            _currentTheme = theme;
            UpdatePreview();
        }

        public async Task InitializeAsync()
        {
            if (!OpenDocuments.Any())
            {
                await CreateNewAsync();
            }
        }

        public async Task CreateNewAsync()
        {
            var document = new DocumentViewModel
            {
                Title = "Untitled",
                Content = string.Empty
            };

            OpenDocuments.Add(document);
            ActiveDocument = document;
            ViewMode = EditorViewMode.Split;
            await Task.FromResult<object>(null);
        }

        public async Task OpenFromPickerAsync()
        {
#if WINDOWS_PHONE_APP
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".markdown");
            picker.FileTypeFilter.Add(".txt");
            picker.PickSingleFileAndContinue();
            await Task.FromResult<object>(null);
#else
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".markdown");
            picker.FileTypeFilter.Add(".txt");
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadFileAsync(file);
            }
#endif
        }

        public async Task OpenRecentAsync(RecentFileItem item)
        {
            if (item == null)
            {
                return;
            }

            var file = await _recentFiles.GetFileAsync(item);
            if (file != null)
            {
                await LoadFileAsync(file, item.Token);
            }
        }

        public async Task SaveAsync()
        {
            if (ActiveDocument == null)
            {
                return;
            }

            IsSaving = true;
            SaveStatusText = "Saving...";
            var minDelay = Task.Delay(500); // 保证进度条至少显示 0.5 秒
            bool saved = false;

            try
            {
                if (ActiveDocument.File == null)
                {
                    var savePicker = new FileSavePicker
                    {
                        SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                        SuggestedFileName = ActiveDocument.Title
                    };
                    savePicker.FileTypeChoices.Add("Markdown", new[] { ".md", ".markdown" });
                    savePicker.FileTypeChoices.Add("Text", new[] { ".txt" });
#if WINDOWS_PHONE_APP
                    savePicker.PickSaveFileAndContinue();
                    await Task.FromResult<object>(null);
                    // WP8.1 挂起前直接返回，finally 会执行并重置 IsSaving，这是正确的
                    // 真正的保存和成功提示会在 HandleSavePickerContinuation 中处理
                    return;
#else
                    var target = await savePicker.PickSaveFileAsync();
                    if (target == null)
                    {
                        return;
                    }

                    await ActiveDocument.SaveAsync(target);
                    var recentItem = await _recentFiles.TouchAsync(target);
                    ActiveDocument.Token = recentItem != null ? recentItem.Token : null;
                    saved = true;
#endif
                }
                else
                {
                    await ActiveDocument.SaveAsync();
                    var recentItem = await _recentFiles.TouchAsync(ActiveDocument.File);
                    ActiveDocument.Token = recentItem != null ? recentItem.Token : ActiveDocument.Token;
                    saved = true;
                }

                if (saved)
                {
                    await minDelay; // 等待最小展示时间
                    SaveStatusText = "Success";
                    await Task.Delay(1000); // 显示 Success 文字 1 秒
                }
            }
            finally
            {
                IsSaving = false;
                SaveStatusText = null;
            }
        }

        public async Task AutoSaveAsync()
        {
            if (ActiveDocument == null || ActiveDocument.File == null || !ActiveDocument.IsDirty)
            {
                return;
            }

            IsSaving = true;
            try
            {
                await ActiveDocument.SaveAsync();
                var recentItem = await _recentFiles.TouchAsync(ActiveDocument.File);
                ActiveDocument.Token = recentItem != null ? recentItem.Token : ActiveDocument.Token;
            }
            finally
            {
                IsSaving = false;
            }
        }

        public async Task RemoveActiveAsync()
        {
            if (ActiveDocument == null)
            {
                return;
            }

            var index = OpenDocuments.IndexOf(ActiveDocument);
            OpenDocuments.Remove(ActiveDocument);

            if (OpenDocuments.Any())
            {
                ActiveDocument = OpenDocuments[Math.Max(0, Math.Min(index, OpenDocuments.Count - 1))];
            }
            else
            {
                await CreateNewAsync();
            }
        }

        public void CycleViewMode()
        {
            switch (ViewMode)
            {
                case EditorViewMode.Split:
                    ViewMode = EditorViewMode.Write;
                    break;
                case EditorViewMode.Write:
                    ViewMode = EditorViewMode.Preview;
                    break;
                default:
                    ViewMode = EditorViewMode.Split;
                    break;
            }
        }

        private async Task LoadFileAsync(StorageFile file, string token = null)
        {
            if (file == null)
            {
                return;
            }

            var existing = OpenDocuments.FirstOrDefault(d => string.Equals(d.Path, file.Path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActiveDocument = existing;
                return;
            }

            var document = new DocumentViewModel();
            await document.LoadAsync(file);
            var recentItem = await _recentFiles.TouchAsync(file);
            document.Token = recentItem != null ? recentItem.Token : token;

            OpenDocuments.Add(document);
            ActiveDocument = document;
        }

        private void OnDocumentPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Content")
            {
                if (_suppressPreviewUpdate)
                {
                    return;
                }
                UpdatePreview();
            }
        }

        private void UpdatePreview()
        {
            if (ActiveDocument == null)
            {
                return;
            }

            var markdown = ActiveDocument.Content ?? string.Empty;
            PreviewCss = _themeService.BuildCss();
            var rendered = _renderService.RenderMarkdown(markdown);
            PreviewBlocks = rendered.Blocks;
            PreviewContent = NormalizeImageSourcesInHtml(rendered.Html);
            ParseOutline(markdown);
        }

        private string ConvertMarkdownToHtml(string markdown)
        {
            var html = _renderService.ToHtml(markdown ?? string.Empty);
            return NormalizeImageSourcesInHtml(html);
        }

        private void ParseOutline(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                OutlineItems = null;
                return;
            }

            var items = new List<OutlineItem>();
            var lines = markdown.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("#"))
                {
                    int level = 0;
                    while (level < line.Length && line[level] == '#') level++;
                    
                    if (level > 0 && level <= 6)
                    {
                        items.Add(new OutlineItem(line.Substring(level).Trim(), level, i));
                    }
                }
            }
            OutlineItems = items;
        }

        private string NormalizeImageSource(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (Uri.IsWellFormedUriString(path, UriKind.Absolute))
            {
                return path;
            }

            try
            {
                var localFolderPath = ApplicationData.Current.LocalFolder.Path;
                if (!string.IsNullOrEmpty(localFolderPath) &&
                    path.StartsWith(localFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = path.Substring(localFolderPath.Length).TrimStart('\\', '/');
                    return "ms-appdata:///local/" + relative.Replace("\\", "/");
                }

                if (ActiveDocument != null && ActiveDocument.File != null)
                {
                    var baseFolder = Path.GetDirectoryName(ActiveDocument.File.Path);
                    if (!string.IsNullOrEmpty(baseFolder))
                    {
                        var combined = Path.Combine(baseFolder, path);
                        return "file:///" + combined.Replace("\\", "/");
                    }
                }
            }
            catch
            {
            }

            return path;
        }

        private string NormalizeImageSourcesInHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            return Regex.Replace(html, "(<img[^>]*src=\")([^\"]*)(\"[^>]*>)", m =>
            {
                var prefix = m.Groups[1].Value;
                var src = NormalizeImageSource(m.Groups[2].Value);
                var suffix = m.Groups[3].Value;
                return prefix + src + suffix;
            });
        }

        private string BuildHighlightCss()
        {
            return _currentTheme == ElementTheme.Dark
                ? "https://cdnjs.cloudflare.com/ajax/libs/highlight.js/9.18.5/styles/atom-one-dark.min.css"
                : "https://cdnjs.cloudflare.com/ajax/libs/highlight.js/9.18.5/styles/atom-one-light.min.css";
        }

        private string BuildHighlightScripts()
        {
            return "<script src=\"https://cdnjs.cloudflare.com/ajax/libs/highlight.js/9.18.5/highlight.min.js\"></script><script>hljs.initHighlightingOnLoad();</script>";
        }

        public string GenerateFullHtml()
        {
            var markdown = ActiveDocument != null ? ActiveDocument.Content : string.Empty;
            var html = new StringBuilder();
            html.Append("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            html.Append(_themeService.BuildCss());
            html.Append("<link rel=\"stylesheet\" href=\"" + BuildHighlightCss() + "\" />");
            html.Append("</head><body>");
            html.Append(ConvertMarkdownToHtml(markdown));
            html.Append(BuildHighlightScripts());
            html.Append("</body></html>");
            return html.ToString();
        }

        public async Task ExportAsync(string format)
        {
            if (ActiveDocument == null)
            {
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = ActiveDocument.Title
            };

            string fileType;
            if (string.Equals(format, "html", StringComparison.OrdinalIgnoreCase))
            {
                fileType = "HTML";
                picker.FileTypeChoices.Add(fileType, new[] { ".html" });
            }
            else if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            {
                fileType = "PDF";
                picker.FileTypeChoices.Add(fileType, new[] { ".pdf" });
            }
            else
            {
                fileType = "Markdown";
                picker.FileTypeChoices.Add(fileType, new[] { ".md", ".markdown" });
            }

            // Prepare content before calling picker, for both platforms
            string contentToSave;
            if (string.Equals(format, "html", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            {
                contentToSave = GenerateFullHtml();
            }
            else
            {
                contentToSave = ActiveDocument.Content ?? string.Empty;
            }

#if WINDOWS_PHONE_APP
            _exportContent = contentToSave;
            picker.PickSaveFileAndContinue();
            await Task.FromResult<object>(null);
#else
            var target = await picker.PickSaveFileAsync();
            if (target == null) return;

            await FileIO.WriteTextAsync(target, contentToSave);
            var recentItem = await _recentFiles.TouchAsync(target);
            ActiveDocument.Token = recentItem != null ? recentItem.Token : ActiveDocument.Token;
#endif
        }

        private void ApplyFormatting(string key)
        {
            if (ActiveDocument == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            switch (key)
            {
                case "H1":
                    ActiveDocument.Content += Environment.NewLine + "# ";
                    break;
                case "H2":
                    ActiveDocument.Content += Environment.NewLine + "## ";
                    break;
                case "H3":
                    ActiveDocument.Content += Environment.NewLine + "### ";
                    break;
                case "Checkbox":
                    ActiveDocument.Content += Environment.NewLine + "- [ ] ";
                    break;
                case "Link":
                    ActiveDocument.Content += Environment.NewLine + "[text](http://)";
                    break;
                case "Bold":
                    ActiveDocument.Content += "**bold**";
                    break;
            }
        }

        private void CloseDocument(DocumentViewModel document)
        {
            if (document == null)
            {
                return;
            }

            var index = OpenDocuments.IndexOf(document);
            OpenDocuments.Remove(document);
            if (OpenDocuments.Any())
            {
                ActiveDocument = OpenDocuments[Math.Max(0, Math.Min(index, OpenDocuments.Count - 1))];
            }
            else
            {
                var _ = CreateNewAsync();
            }
        }

        public void SetContentFromEditor(string text)
        {
            if (ActiveDocument == null)
            {
                return;
            }

            _suppressPreviewUpdate = true;
            ActiveDocument.Content = text ?? string.Empty;
            _suppressPreviewUpdate = false;
        }

        public void RefreshPreview()
        {
            UpdatePreview();
        }

#if WINDOWS_PHONE_APP
        public async Task HandleOpenPickerContinuation(FileOpenPickerContinuationEventArgs args)
        {
            if (args == null || args.Files == null || args.Files.Count == 0)
            {
                return;
            }

            var file = args.Files[0];
            await LoadFileAsync(file);
        }

        public async Task HandleSavePickerContinuation(FileSavePickerContinuationEventArgs args)
        {
            if (args == null || args.File == null)
            {
                _exportContent = null; // Also clear on cancellation
                return;
            }

            IsSaving = true;
            SaveStatusText = "Saving...";
            var minDelay = Task.Delay(500);

            // Use a local variable to capture the export content and reset the state field.
            // This prevents race conditions or unexpected behavior on subsequent saves.
            string contentForExport = _exportContent;
            _exportContent = null;

            if (contentForExport != null)
            {
                try 
                {
                // This is an EXPORT continuation. Write the pre-generated content.
                await FileIO.WriteTextAsync(args.File, contentForExport);
                }
                finally { IsSaving = false; } // Export usually doesn't need the full success cycle or handled differently
                return;
            }
            else
            {
                // This is a regular SAVE AS continuation.
                await ActiveDocument.SaveAsync(args.File);
            }
            
            var recentItem = await _recentFiles.TouchAsync(args.File);
            ActiveDocument.Token = recentItem != null ? recentItem.Token : null;

            await minDelay;
            SaveStatusText = "Success";
            await Task.Delay(1000);
            IsSaving = false;
            SaveStatusText = null;
        }
#endif
    }

    public class OutlineItem
    {
        public string Title { get; private set; }
        public int Level { get; private set; }
        public int LineNumber { get; private set; }

        public OutlineItem(string title, int level, int line)
        {
            Title = title;
            Level = level;
            LineNumber = line;
        }

        // View Helpers for Binding
        public Thickness Margin => new Thickness((Level - 1) * 24, 4, 0, 4);
        public double FontSize => Level == 1 ? 24 : (Level == 2 ? 20 : 16);
        public FontWeight FontWeight => Level == 1 ? FontWeights.Bold : (Level == 2 ? FontWeights.Normal : FontWeights.Light);
    }
}
