using System;
using System.ComponentModel;
using Windows.Storage;

namespace MetroMarkdownEditor.Services
{
    public class AutoSaveService : INotifyPropertyChanged
    {
        private const string EnabledKey = "AutoSaveEnabled";
        private const string FrequencyKey = "AutoSaveFrequencyMinutes";
        private bool _isEnabled = true;
        private int _frequencyMinutes = 5;

        public static AutoSaveService Instance { get; } = new AutoSaveService();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler SettingsChanged;

        private AutoSaveService()
        {
            Load();
        }

        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    Save();
                    OnPropertyChanged("IsEnabled");
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public int FrequencyMinutes
        {
            get { return _frequencyMinutes; }
            set
            {
                var normalized = Math.Max(1, Math.Min(60, value));
                if (_frequencyMinutes != normalized)
                {
                    _frequencyMinutes = normalized;
                    Save();
                    OnPropertyChanged("FrequencyMinutes");
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void Load()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            if (settings.ContainsKey(EnabledKey))
            {
                bool enabled;
                if (bool.TryParse(settings[EnabledKey]?.ToString(), out enabled))
                {
                    _isEnabled = enabled;
                }
            }

            if (settings.ContainsKey(FrequencyKey))
            {
                int minutes;
                if (int.TryParse(settings[FrequencyKey]?.ToString(), out minutes))
                {
                    _frequencyMinutes = Math.Max(1, Math.Min(60, minutes));
                }
            }
        }

        private void Save()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            settings[EnabledKey] = _isEnabled;
            settings[FrequencyKey] = _frequencyMinutes;
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
