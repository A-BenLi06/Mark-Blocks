using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace MetroMarkdownEditor.Services
{
    public class RecentFileService
    {
        private const string SettingsKey = "RecentFileTokens";
        private const int MaxItems = 12;
        private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;

        public RecentFileService()
        {
            Items = new ObservableCollection<RecentFileItem>();
        }

        public ObservableCollection<RecentFileItem> Items { get; private set; }

        public async Task InitializeAsync()
        {
            Items.Clear();
            var raw = _settings.Values[SettingsKey] as string;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var tokens = raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                try
                {
                    var file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(token);
                    Items.Add(new RecentFileItem { Token = token, Name = file.Name, Path = file.Path });
                }
                catch
                {
                    // Ignore broken tokens.
                }
            }
        }

        public async Task<StorageFile> GetFileAsync(RecentFileItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Token))
            {
                return null;
            }

            try
            {
                return await StorageApplicationPermissions.FutureAccessList.GetFileAsync(item.Token);
            }
            catch
            {
                return null;
            }
        }

        public async Task<RecentFileItem> TouchAsync(StorageFile file)
        {
            if (file == null)
            {
                return null;
            }

            var existing = Items.FirstOrDefault(i => string.Equals(i.Path, file.Path, StringComparison.OrdinalIgnoreCase));
            string token;
            if (existing != null)
            {
                token = existing.Token;
                StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, file);
                MoveToTop(existing);
            }
            else
            {
                token = StorageApplicationPermissions.FutureAccessList.Add(file);
                var item = new RecentFileItem { Token = token, Name = file.Name, Path = file.Path };
                Items.Insert(0, item);
                Trim();
            }

            await PersistAsync();
            return Items.FirstOrDefault(i => i.Token == token);
        }

        public async Task RemoveAsync(RecentFileItem item)
        {
            if (item == null)
            {
                return;
            }

            if (Items.Contains(item))
            {
                Items.Remove(item);
                await PersistAsync();
            }
        }

        private void MoveToTop(RecentFileItem item)
        {
            if (item == null)
            {
                return;
            }

            if (Items.Remove(item))
            {
                Items.Insert(0, item);
            }
        }

        private void Trim()
        {
            while (Items.Count > MaxItems)
            {
                Items.RemoveAt(Items.Count - 1);
            }
        }

        private Task PersistAsync()
        {
            _settings.Values[SettingsKey] = string.Join("|", Items.Select(i => i.Token));
            return Task.FromResult(true);
        }
    }
}
