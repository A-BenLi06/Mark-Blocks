using System;
using System.ComponentModel;
using Windows.Storage;

namespace MetroMarkdownEditor.Services
{
    public enum ImageInsertAction
    {
        NoSpecialAction = 0,
        CopyToCurrentFolder = 1,
        CopyToAssetsFolder = 2,
        CopyToFileNameAssetsFolder = 3,
        UploadImage = 4,
        CopyToCustomFolder = 5
    }

    public enum ImageUploader
    {
        None = 0,
        PicGoCore = 1,
        PicList = 2,
        CustomCommand = 3
    }

    public sealed class ImageSettingsService : INotifyPropertyChanged
    {
        private const string Prefix = "Image.";

        private ImageInsertAction _insertAction = ImageInsertAction.NoSpecialAction;
        private bool _applyRulesToLocalImages = true;
        private bool _applyRulesToOnlineImages;
        private bool _allowYamlImageUploadSettings;
        private bool _useRelativePathIfPossible = true;
        private bool _addDotSlashForRelativePath;
        private bool _autoEscapeImageUrlWhenInsert = true;
        private ImageUploader _uploader = ImageUploader.None;
        private string _picListPath = @"C:\Program Files\PicList\PicList.exe";
        private string _customCommand = string.Empty;
        private string _customFolder = string.Empty;
        private string _picGoServerUrl = "http://127.0.0.1:36677";
        private string _picGoServerSecret = string.Empty;
        private int _cacheLimitMegabytes = 128;

        public int CacheLimitMegabytes
        {
            get { return _cacheLimitMegabytes; }
            set { SetValue(ref _cacheLimitMegabytes, Math.Max(0, Math.Min(4096, value)), "CacheLimitMegabytes"); }
        }

        public static ImageSettingsService Instance { get; } = new ImageSettingsService();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler SettingsChanged;

        private ImageSettingsService()
        {
            Load();
        }

        public ImageInsertAction InsertAction
        {
            get { return _insertAction; }
            set { SetValue(ref _insertAction, value, "InsertAction"); }
        }

        public int InsertActionIndex
        {
            get { return (int)_insertAction; }
            set { InsertAction = (ImageInsertAction)NormalizeIndex(value, 0, 5); }
        }

        public bool ApplyRulesToLocalImages
        {
            get { return _applyRulesToLocalImages; }
            set { SetValue(ref _applyRulesToLocalImages, value, "ApplyRulesToLocalImages"); }
        }

        public bool ApplyRulesToOnlineImages
        {
            get { return _applyRulesToOnlineImages; }
            set { SetValue(ref _applyRulesToOnlineImages, value, "ApplyRulesToOnlineImages"); }
        }

        public bool AllowYamlImageUploadSettings
        {
            get { return _allowYamlImageUploadSettings; }
            set { SetValue(ref _allowYamlImageUploadSettings, value, "AllowYamlImageUploadSettings"); }
        }

        public bool UseRelativePathIfPossible
        {
            get { return _useRelativePathIfPossible; }
            set { SetValue(ref _useRelativePathIfPossible, value, "UseRelativePathIfPossible"); }
        }

        public bool AddDotSlashForRelativePath
        {
            get { return _addDotSlashForRelativePath; }
            set { SetValue(ref _addDotSlashForRelativePath, value, "AddDotSlashForRelativePath"); }
        }

        public bool AutoEscapeImageUrlWhenInsert
        {
            get { return _autoEscapeImageUrlWhenInsert; }
            set { SetValue(ref _autoEscapeImageUrlWhenInsert, value, "AutoEscapeImageUrlWhenInsert"); }
        }

        public ImageUploader Uploader
        {
            get { return _uploader; }
            set { SetValue(ref _uploader, value, "Uploader"); }
        }

        public int UploaderIndex
        {
            get { return (int)_uploader; }
            set { Uploader = (ImageUploader)NormalizeIndex(value, 0, 3); }
        }

        public string PicListPath
        {
            get { return _picListPath; }
            set { SetValue(ref _picListPath, value ?? string.Empty, "PicListPath"); }
        }

        public string CustomCommand
        {
            get { return _customCommand; }
            set { SetValue(ref _customCommand, value ?? string.Empty, "CustomCommand"); }
        }

        public string CustomFolder
        {
            get { return _customFolder; }
            set { SetValue(ref _customFolder, value ?? string.Empty, "CustomFolder"); }
        }

        public string PicGoServerUrl
        {
            get { return _picGoServerUrl; }
            set { SetValue(ref _picGoServerUrl, value ?? string.Empty, "PicGoServerUrl"); }
        }

        public string PicGoServerSecret
        {
            get { return _picGoServerSecret; }
            set { SetValue(ref _picGoServerSecret, value ?? string.Empty, "PicGoServerSecret"); }
        }

        public bool ShouldUploadLocalImages
        {
            get
            {
                return false; // Legacy desktop uploaders are unsupported in the Store sandbox.
            }
        }

        private void Load()
        {
            _cacheLimitMegabytes = ReadInt("CacheLimitMegabytes", 128, 0, 4096);
            _insertAction = (ImageInsertAction)ReadInt("InsertAction", (int)_insertAction, 0, 5);
            _applyRulesToLocalImages = ReadBool("ApplyRulesToLocalImages", _applyRulesToLocalImages);
            _applyRulesToOnlineImages = ReadBool("ApplyRulesToOnlineImages", _applyRulesToOnlineImages);
            _allowYamlImageUploadSettings = ReadBool("AllowYamlImageUploadSettings", _allowYamlImageUploadSettings);
            _useRelativePathIfPossible = ReadBool("UseRelativePathIfPossible", _useRelativePathIfPossible);
            _addDotSlashForRelativePath = ReadBool("AddDotSlashForRelativePath", _addDotSlashForRelativePath);
            _autoEscapeImageUrlWhenInsert = ReadBool("AutoEscapeImageUrlWhenInsert", _autoEscapeImageUrlWhenInsert);
            _uploader = (ImageUploader)ReadInt("Uploader", (int)_uploader, 0, 3);
            _picListPath = ReadString("PicListPath", _picListPath);
            _customCommand = ReadString("CustomCommand", _customCommand);
            _customFolder = ReadString("CustomFolder", _customFolder);
            _picGoServerUrl = ReadString("PicGoServerUrl", _picGoServerUrl);
            _picGoServerSecret = ReadString("PicGoServerSecret", _picGoServerSecret);
            if (_insertAction == ImageInsertAction.UploadImage) _insertAction = ImageInsertAction.NoSpecialAction;
            _uploader = ImageUploader.None;
        }

        private void Save()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            settings[Prefix + "CacheLimitMegabytes"] = _cacheLimitMegabytes;
            settings[Prefix + "InsertAction"] = (int)_insertAction;
            settings[Prefix + "ApplyRulesToLocalImages"] = _applyRulesToLocalImages;
            settings[Prefix + "ApplyRulesToOnlineImages"] = _applyRulesToOnlineImages;
            settings[Prefix + "AllowYamlImageUploadSettings"] = _allowYamlImageUploadSettings;
            settings[Prefix + "UseRelativePathIfPossible"] = _useRelativePathIfPossible;
            settings[Prefix + "AddDotSlashForRelativePath"] = _addDotSlashForRelativePath;
            settings[Prefix + "AutoEscapeImageUrlWhenInsert"] = _autoEscapeImageUrlWhenInsert;
            settings[Prefix + "Uploader"] = (int)_uploader;
            settings[Prefix + "PicListPath"] = _picListPath ?? string.Empty;
            settings[Prefix + "CustomCommand"] = _customCommand ?? string.Empty;
            settings[Prefix + "CustomFolder"] = _customFolder ?? string.Empty;
            settings[Prefix + "PicGoServerUrl"] = _picGoServerUrl ?? string.Empty;
            settings[Prefix + "PicGoServerSecret"] = _picGoServerSecret ?? string.Empty;
        }

        private bool ReadBool(string key, bool defaultValue)
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            object raw;
            if (!settings.TryGetValue(Prefix + key, out raw) || raw == null) return defaultValue;
            bool parsed;
            return bool.TryParse(raw.ToString(), out parsed) ? parsed : defaultValue;
        }

        private int ReadInt(string key, int defaultValue, int min, int max)
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            object raw;
            if (!settings.TryGetValue(Prefix + key, out raw) || raw == null) return defaultValue;
            int parsed;
            return int.TryParse(raw.ToString(), out parsed) ? Math.Max(min, Math.Min(max, parsed)) : defaultValue;
        }

        private string ReadString(string key, string defaultValue)
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            object raw;
            return settings.TryGetValue(Prefix + key, out raw) && raw != null ? raw.ToString() : defaultValue;
        }

        private void SetValue<T>(ref T field, T value, string propertyName)
        {
            if (object.Equals(field, value)) return;
            field = value;
            Save();
            OnPropertyChanged(propertyName);
            RaiseCompanions(propertyName);
            OnSettingsChanged();
        }

        private void RaiseCompanions(string propertyName)
        {
            if (propertyName == "InsertAction") OnPropertyChanged("InsertActionIndex");
            else if (propertyName == "Uploader") OnPropertyChanged("UploaderIndex");

            if (propertyName == "InsertAction" || propertyName == "Uploader" || propertyName == "ApplyRulesToLocalImages")
            {
                OnPropertyChanged("ShouldUploadLocalImages");
            }
        }

        private static int NormalizeIndex(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnSettingsChanged()
        {
            var handler = SettingsChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
