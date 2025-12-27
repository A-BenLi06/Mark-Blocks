using System;
using System.ComponentModel;
using Windows.Storage;

namespace MetroMarkdownEditor.Services
{
    /// <summary>
    /// Developer settings service for debug options
    /// </summary>
    public class DevSettingsService : INotifyPropertyChanged
    {
        private const string MermaidDebugKey = "DevMermaidDebugEnabled";
        private bool _mermaidDebugEnabled;

        public static DevSettingsService Instance { get; } = new DevSettingsService();

        public event PropertyChangedEventHandler PropertyChanged;

        private DevSettingsService()
        {
            Load();
        }

        /// <summary>
        /// Enable/disable Mermaid initialization debug output in preview
        /// </summary>
        public bool MermaidDebugEnabled
        {
            get { return _mermaidDebugEnabled; }
            set
            {
                if (_mermaidDebugEnabled != value)
                {
                    _mermaidDebugEnabled = value;
                    Save();
                    OnPropertyChanged("MermaidDebugEnabled");
                }
            }
        }

        private void Load()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            if (settings.ContainsKey(MermaidDebugKey))
            {
                bool enabled;
                if (bool.TryParse(settings[MermaidDebugKey]?.ToString(), out enabled))
                {
                    _mermaidDebugEnabled = enabled;
                }
            }
        }

        private void Save()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            settings[MermaidDebugKey] = _mermaidDebugEnabled;
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
