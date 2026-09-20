using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.Services
{
    public static class ImageCacheService
    {
        public static readonly OnlineImageCache Cache = new OnlineImageCache(new LocalStore(), new HttpClient(),
            () => ImageSettingsService.Instance.CacheLimitMegabytes * 1024L * 1024L);

        public static async Task HandleRequestAsync(WebView view, string message)
        {
            const string prefix = "md-image-cache:";
            if (message == null || !message.StartsWith(prefix, StringComparison.Ordinal) || message.Length > 8192) return;
            var url = message.Substring(prefix.Length);
            var data = await Cache.GetAsync(url);
            // Non-persistable/no-store responses use the normal browser path;
            // never send a multi-megabyte data URI through the UI script bridge.
            var source = data != null && data.StartsWith("/image/", StringComparison.Ordinal) ? data : url;
            try { await view.InvokeScriptAsync("__mdImageReady", new[] { url, source }); }
            catch { /* Navigation can replace the requesting document. */ }
        }

        private sealed class LocalStore : IImageCacheStore
        {
            private Task<StorageFolder> FolderAsync()
            { return ApplicationData.Current.LocalFolder.CreateFolderAsync("OnlineImageCache", CreationCollisionOption.OpenIfExists).AsTask(); }
            private static string Key(string url)
            {
                var hash = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256);
                return CryptographicBuffer.EncodeToHexString(hash.HashData(CryptographicBuffer.ConvertStringToBinary(url, BinaryStringEncoding.Utf8))) + ".cache";
            }
            public async Task<string> ReadAsync(string url)
            {
                try
                {
                    var file = await (await FolderAsync()).GetFileAsync(Key(url));
                    // Read only the MIME prefix, not the entire base64 payload.
                    using (var stream = await file.OpenStreamForReadAsync())
                    using (var reader = new System.IO.StreamReader(stream))
                    {
                        var chars = new char[96];
                        var count = await reader.ReadAsync(chars, 0, chars.Length);
                        var header = new string(chars, 0, count);
                        var types = new[] { "png", "jpeg", "gif", "webp", "bmp", "x-icon", "vnd.microsoft.icon", "svg+xml" };
                        var extensions = new[] { "png", "jpg", "gif", "webp", "bmp", "ico", "ico", "svg" };
                        for (var i = 0; i < types.Length; i++)
                            if (header.StartsWith("data:image/" + types[i] + ";base64,", StringComparison.Ordinal))
                                return "/image/" + Key(url) + "." + extensions[i];
                        return null;
                    }
                }
                catch (System.IO.FileNotFoundException) { return null; }
            }
            public async Task WriteAsync(string url, string data)
            {
                var folder = await FolderAsync();
                var temp = await folder.CreateFileAsync(Key(url) + ".tmp", CreationCollisionOption.ReplaceExisting);
                try
                {
                    await FileIO.WriteTextAsync(temp, data);
                    await temp.RenameAsync(Key(url), NameCollisionOption.ReplaceExisting);
                }
                catch { await temp.DeleteAsync(); throw; }
            }
            public async Task<IReadOnlyList<ImageCacheEntry>> ListAsync()
            {
                var entries = new List<ImageCacheEntry>();
                foreach (var file in await (await FolderAsync()).GetFilesAsync())
                {
                    var props = await file.GetBasicPropertiesAsync();
                    entries.Add(new ImageCacheEntry { Key = file.Name, Bytes = (long)props.Size, Written = props.DateModified });
                }
                return entries;
            }
            public async Task DeleteAsync(string key)
            { await (await (await FolderAsync()).GetFileAsync(key)).DeleteAsync(); }
        }
    }
}
