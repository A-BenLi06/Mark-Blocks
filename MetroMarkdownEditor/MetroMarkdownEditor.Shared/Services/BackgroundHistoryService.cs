using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace MetroMarkdownEditor.Services
{
    [DataContract]
    public class BackgroundHistoryItem
    {
        [DataMember]
        public string FileName { get; set; }

        [DataMember]
        public string OriginalName { get; set; }

        [DataMember]
        public DateTime AddedTime { get; set; }

        /// <summary>
        /// Returns the local path as ms-appdata URI for BitmapImage consumption
        /// </summary>
        public string LocalPath
        {
            get
            {
                if (string.IsNullOrEmpty(FileName)) return null;
                // Use ms-appdata:///local/ URI scheme for proper BitmapImage loading
                return "ms-appdata:///local/Backgrounds/" + FileName;
            }
        }
    }

    public class BackgroundHistoryService
    {
        public const int MaxHistoryCount = 10;
        private const string HistoryFileName = "background_history.json";
        private const string BackgroundsFolderName = "Backgrounds";

        private static BackgroundHistoryService _instance;
        public static BackgroundHistoryService Instance
        {
            get { return _instance ?? (_instance = new BackgroundHistoryService()); }
        }

        public ObservableCollection<BackgroundHistoryItem> History { get; private set; }

        public BackgroundHistoryService()
        {
            History = new ObservableCollection<BackgroundHistoryItem>();
        }

        public async Task LoadHistoryAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await GetFileIfExistsAsync(folder, HistoryFileName);
                if (file != null)
                {
                    using (var stream = await file.OpenStreamForReadAsync())
                    {
                        var serializer = new DataContractJsonSerializer(typeof(BackgroundHistoryItem[]));
                        var items = serializer.ReadObject(stream) as BackgroundHistoryItem[];
                        if (items != null)
                        {
                            History.Clear();
                            foreach (var item in items)
                            {
                                History.Add(item);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore load errors
            }
        }

        public async Task SaveHistoryAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync(HistoryFileName, CreationCollisionOption.ReplaceExisting);
                using (var stream = await file.OpenStreamForWriteAsync())
                {
                    var serializer = new DataContractJsonSerializer(typeof(BackgroundHistoryItem[]));
                    serializer.WriteObject(stream, History.ToArray());
                }
            }
            catch
            {
                // Ignore save errors
            }
        }

        public async Task<string> AddImageAsync(StorageFile sourceFile)
        {
            if (sourceFile == null) return null;

            try
            {
                // Ensure Backgrounds folder exists
                var localFolder = ApplicationData.Current.LocalFolder;
                var backgroundsFolder = await localFolder.CreateFolderAsync(BackgroundsFolderName, CreationCollisionOption.OpenIfExists);

                // Generate unique filename
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(sourceFile.Name);

                // Copy file to local storage
                var copiedFile = await sourceFile.CopyAsync(backgroundsFolder, fileName, NameCollisionOption.ReplaceExisting);

                // Create history item
                var item = new BackgroundHistoryItem
                {
                    FileName = fileName,
                    OriginalName = sourceFile.Name,
                    AddedTime = DateTime.Now
                };

                // Add to history
                History.Insert(0, item);

                // Remove old items if exceeds limit
                while (History.Count > MaxHistoryCount)
                {
                    var oldItem = History.Last();
                    await RemoveImageFileAsync(oldItem);
                    History.Remove(oldItem);
                }

                await SaveHistoryAsync();

                return item.LocalPath;
            }
            catch
            {
                return null;
            }
        }

        public async Task RemoveImageAsync(BackgroundHistoryItem item)
        {
            if (item == null) return;

            await RemoveImageFileAsync(item);
            History.Remove(item);
            await SaveHistoryAsync();
        }

        private async Task RemoveImageFileAsync(BackgroundHistoryItem item)
        {
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                var backgroundsFolder = await GetFolderIfExistsAsync(localFolder, BackgroundsFolderName);
                if (backgroundsFolder != null)
                {
                    var file = await GetFileIfExistsAsync(backgroundsFolder, item.FileName);
                    if (file != null)
                    {
                        await file.DeleteAsync();
                    }
                }
            }
            catch
            {
                // Ignore delete errors
            }
        }

        /// <summary>
        /// Helper method to get a file if it exists (TryGetItemAsync is not available on WP8.1)
        /// </summary>
        private async Task<StorageFile> GetFileIfExistsAsync(StorageFolder folder, string fileName)
        {
            try
            {
                // Get files in the folder and find matching one
                var files = await folder.GetFilesAsync();
                foreach (var file in files)
                {
                    if (file.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
                return null;
            }
            catch
            {
                // Any error - return null
                return null;
            }
        }

        /// <summary>
        /// Helper method to get a folder if it exists (TryGetItemAsync is not available on WP8.1)
        /// </summary>
        private async Task<StorageFolder> GetFolderIfExistsAsync(StorageFolder folder, string folderName)
        {
            try
            {
                // Get folders in the folder and find matching one
                var folders = await folder.GetFoldersAsync();
                foreach (var subFolder in folders)
                {
                    if (subFolder.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        return subFolder;
                    }
                }
                return null;
            }
            catch
            {
                // Any error - return null
                return null;
            }
        }
    }
}

