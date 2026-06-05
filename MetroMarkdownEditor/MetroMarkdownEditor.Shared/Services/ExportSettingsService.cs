using System;
using System.ComponentModel;
using Windows.Storage;

namespace MetroMarkdownEditor.Services
{
    public enum ExportDefaultFolder
    {
        Auto = 0,
        SameFolderWithCurrentFile = 1,
        CustomLocation = 2
    }

    public sealed class ExportSettingsService : INotifyPropertyChanged
    {
        private const string Prefix = "Export.";

        private ExportDefaultFolder _defaultFolder = ExportDefaultFolder.Auto;
        private string _customFolder = string.Empty;
        private string _pandocPath = string.Empty;
        private bool _openExportedFileLocation;

        public static ExportSettingsService Instance { get; } = new ExportSettingsService();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler SettingsChanged;

        private ExportSettingsService()
        {
            Load();
        }

        public ExportDefaultFolder DefaultFolder
        {
            get { return _defaultFolder; }
            set { SetValue(ref _defaultFolder, value, "DefaultFolder"); }
        }

        public int DefaultFolderIndex
        {
            get { return (int)_defaultFolder; }
            set { DefaultFolder = (ExportDefaultFolder)NormalizeIndex(value, 0, 2); }
        }

        public string CustomFolder
        {
            get { return _customFolder; }
            set { SetValue(ref _customFolder, value ?? string.Empty, "CustomFolder"); }
        }

        public string PandocPath
        {
            get { return _pandocPath; }
            set { SetValue(ref _pandocPath, value ?? string.Empty, "PandocPath"); }
        }

        public bool OpenExportedFileLocation
        {
            get { return _openExportedFileLocation; }
            set { SetValue(ref _openExportedFileLocation, value, "OpenExportedFileLocation"); }
        }

        private void Load()
        {
            _defaultFolder = (ExportDefaultFolder)ReadInt("DefaultFolder", (int)_defaultFolder, 0, 2);
            _customFolder = ReadString("CustomFolder", _customFolder);
            _pandocPath = ReadString("PandocPath", _pandocPath);
            _openExportedFileLocation = ReadBool("OpenExportedFileLocation", _openExportedFileLocation);
        }

        private void Save()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            settings[Prefix + "DefaultFolder"] = (int)_defaultFolder;
            settings[Prefix + "CustomFolder"] = _customFolder ?? string.Empty;
            settings[Prefix + "PandocPath"] = _pandocPath ?? string.Empty;
            settings[Prefix + "OpenExportedFileLocation"] = _openExportedFileLocation;
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
            if (propertyName == "DefaultFolder") OnPropertyChanged("DefaultFolderIndex");
            OnSettingsChanged();
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
