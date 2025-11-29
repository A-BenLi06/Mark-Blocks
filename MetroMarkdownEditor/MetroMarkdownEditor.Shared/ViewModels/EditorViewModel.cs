using System;
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
#if WINDOWS_PHONE_APP
using Windows.ApplicationModel.Activation;
#endif

namespace MetroMarkdownEditor.ViewModels
{
    public class EditorViewModel : BaseViewModel
    {
        private DocumentViewModel _activeDocument;
        private EditorViewMode _viewMode = EditorViewMode.Split;
        private string _previewHtml;
        private readonly ThemeService _themeService;
        private readonly RecentFileService _recentFiles;
        private readonly object _renderLock = new object();
        private bool _suppressPreviewUpdate;

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
                }
            }
        }

        public string PreviewHtml
        {
            get { return _previewHtml; }
            private set
            {
                if (_previewHtml != value)
                {
                    _previewHtml = value;
                    RaisePropertyChanged();
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
#else
                var target = await savePicker.PickSaveFileAsync();
                if (target == null)
                {
                    return;
                }

                await ActiveDocument.SaveAsync(target);
                var recentItem = await _recentFiles.TouchAsync(target);
                ActiveDocument.Token = recentItem != null ? recentItem.Token : null;
#endif
            }
            else
            {
                await ActiveDocument.SaveAsync();
                var recentItem = await _recentFiles.TouchAsync(ActiveDocument.File);
                ActiveDocument.Token = recentItem != null ? recentItem.Token : ActiveDocument.Token;
            }
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
                return;
            }

            await ActiveDocument.SaveAsync(args.File);
            var recentItem = await _recentFiles.TouchAsync(args.File);
            ActiveDocument.Token = recentItem != null ? recentItem.Token : null;
        }
#endif

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
            var html = new StringBuilder();
            html.Append("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            html.Append(_themeService.BuildCss());
            html.Append(BuildHighlightAssets());
            html.Append("</head><body>");
            html.Append(ConvertMarkdownToHtml(markdown));
            html.Append("</body></html>");
            lock (_renderLock)
            {
                PreviewHtml = html.ToString();
            }
        }

        private string ConvertMarkdownToHtml(string markdown)
        {
            var encoded = markdown.Replace("\r", string.Empty);
            var lines = encoded.Split('\n');
            var builder = new StringBuilder();
            var inCode = false;
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("```"))
                {
                    if (!inCode)
                    {
                        builder.Append("<pre><code>");
                        inCode = true;
                    }
                    else
                    {
                        builder.Append("</code></pre>");
                        inCode = false;
                    }

                    continue;
                }

                if (inCode)
                {
                    builder.Append(System.Net.WebUtility.HtmlEncode(line));
                    builder.Append("<br/>");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    builder.Append("<p></p>");
                    continue;
                }

                if (line.StartsWith("### "))
                {
                    builder.AppendFormat("<h3>{0}</h3>", Encode(line.Substring(4)));
                }
                else if (line.StartsWith("## "))
                {
                    builder.AppendFormat("<h2>{0}</h2>", Encode(line.Substring(3)));
                }
                else if (line.StartsWith("# "))
                {
                    builder.AppendFormat("<h1>{0}</h1>", Encode(line.Substring(2)));
                }
                else if (line.StartsWith("- [ ]"))
                {
                    builder.AppendFormat("<p><input type='checkbox' disabled /> {0}</p>", Encode(line.Substring(5).Trim()));
                }
                else if (line.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendFormat("<p><input type='checkbox' checked disabled /> {0}</p>", Encode(line.Substring(5).Trim()));
                }
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    builder.AppendFormat("<p>&bull; {0}</p>", Encode(line.Substring(2).Trim()));
                }
                else if (IsImageLine(line))
                {
                    var imageHtml = BuildImageTag(line);
                    if (!string.IsNullOrEmpty(imageHtml))
                    {
                        builder.Append(imageHtml);
                    }
                }
                else
                {
                    builder.AppendFormat("<p>{0}</p>", Encode(line));
                }
            }

            if (inCode)
            {
                builder.Append("</code></pre>");
            }

            return builder.ToString();
        }

        private static string Encode(string text)
        {
            var result = System.Net.WebUtility.HtmlEncode(text);
            return Regex.Replace(result, @"\[(.*?)\]\((.*?)\)", "<a href='$2'>$1</a>");
        }

        private bool IsImageLine(string line)
        {
            return Regex.IsMatch(line, @"!\[(.*?)\]\((.*?)\)");
        }

        private string BuildImageTag(string line)
        {
            var match = Regex.Match(line, @"!\[(.*?)\]\((.*?)\)");
            if (!match.Success)
            {
                return null;
            }

            var alt = System.Net.WebUtility.HtmlEncode(match.Groups[1].Value);
            var src = NormalizeImageSource(match.Groups[2].Value);
            if (string.IsNullOrEmpty(src))
            {
                return null;
            }

            return string.Format("<p><img alt=\"{0}\" src=\"{1}\" /></p>", alt, src);
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

                // If relative, try to resolve against the active document folder.
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
                // ignore path normalization errors
            }

            return path;
        }

        private string BuildHighlightAssets()
        {
            var sb = new StringBuilder();
            sb.Append("<style>");
            sb.Append("pre { background:#1E1E1E; color:#D4D4D4; padding:12px; border-radius:4px; overflow-x:auto; }");
            sb.Append("code { font-family:'Consolas','Courier New',monospace; }");
            sb.Append(".hljs-keyword { color:#569CD6; }");
            sb.Append(".hljs-string { color:#CE9178; }");
            sb.Append(".hljs-number { color:#B5CEA8; }");
            sb.Append(".hljs-built_in { color:#4EC9B0; }");
            sb.Append(".hljs-comment { color:#6A9955; }");
            sb.Append(".hljs-title { color:#DCDCAA; }");
            sb.Append("</style>");

            // Minimal highlight.js (core) snippet for offline use.
            sb.Append("<script>");
            sb.Append("!function(e){\"use strict\";var t=Object.create(null),n={exports:t};(function(){function e(t){return t instanceof Map?t.clear=t.delete=t.set=function(){throw new Error(\"map is read-only\")}:t instanceof Set&&(t.add=t.clear=t.delete=function(){throw new Error(\"set is read-only\")}),Object.freeze(t),Object.getOwnPropertyNames(t).forEach((function(n){var r=t[n],i=typeof r;\"object\"!==i&&\"function\"!==i||Object.isFrozen(r)||e(r)})),t}function r(t){return t?(t._original||t):(Object.keys(t).forEach((function(n){var r=t[n];\"object\"==typeof r&&null!==r&&(t[n]=r.label)})),t)}var i=/\\b(A[0-9]+)\\b/;function o(e){return e?s(e):null}function s(e){var t=e.expression;return\"string\"==typeof t?t:t.join(\"\")};n.exports={highlight:function(n,o){var s={code:o},a=document.createElement(\"pre\");a.innerHTML=o;var l=a.innerText||a.textContent||\"\";return s.value=a.innerHTML,s.language=n,s}},e(n.exports)}());})();");
            sb.Append("document.addEventListener('DOMContentLoaded', function(){ if(window.hljs && hljs.highlightAll){ hljs.highlightAll(); } });");
            sb.Append("</script>");
            return sb.ToString();
        }

        public string GenerateFullHtml()
        {
            var markdown = ActiveDocument != null ? ActiveDocument.Content : string.Empty;
            var html = new StringBuilder();
            html.Append("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            html.Append(_themeService.BuildCss());
            html.Append(BuildHighlightAssets());
            html.Append("</head><body>");
            html.Append(ConvertMarkdownToHtml(markdown));
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

            if (string.Equals(format, "html", StringComparison.OrdinalIgnoreCase))
            {
                picker.FileTypeChoices.Add("HTML", new[] { ".html" });
            }
            else if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            {
                picker.FileTypeChoices.Add("PDF", new[] { ".pdf" });
            }
            else
            {
                picker.FileTypeChoices.Add("Markdown", new[] { ".md", ".markdown" });
            }

#if WINDOWS_PHONE_APP
            picker.PickSaveFileAndContinue();
            return;
#else
            var target = await picker.PickSaveFileAsync();
            if (target == null)
            {
                return;
            }

            string contentToSave;
            if (string.Equals(format, "html", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            {
                contentToSave = GenerateFullHtml();
            }
            else
            {
                contentToSave = ActiveDocument.Content ?? string.Empty;
            }

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
    }
}
