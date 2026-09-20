using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MetroMarkdownEditor.Services
{
    public sealed class ImageCacheEntry
    {
        public string Key;
        public long Bytes;
        public DateTimeOffset Written;
    }

    public interface IImageCacheStore
    {
        Task<string> ReadAsync(string url);
        Task WriteAsync(string url, string data);
        Task<IReadOnlyList<ImageCacheEntry>> ListAsync();
        Task DeleteAsync(string key);
    }

    // Only preview resources are cached. Markdown always retains the online URL.
    public sealed class OnlineImageCache
    {
        private readonly IImageCacheStore store;
        private readonly HttpClient client;
        private readonly Func<long> limit;
        private readonly SemaphoreSlim storageGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim downloadGate = new SemaphoreSlim(2, 2);
        private readonly Dictionary<string, Task<string>> pending = new Dictionary<string, Task<string>>();
        private int generation;
        public OnlineImageCache(IImageCacheStore store, HttpClient client, Func<long> limit)
        { this.store = store; this.client = client; this.limit = limit; }

        public Task<string> GetAsync(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https")
                || !string.IsNullOrEmpty(uri.UserInfo) || limit() <= 0) return Task.FromResult<string>(null);
            lock (pending)
            {
                Task<string> existing;
                if (pending.TryGetValue(url, out existing)) return existing;
                // Yield prevents completion/removal before the task is registered.
                var requestedGeneration = generation;
                var task = Task.Run(() => GetAndRemoveAsync(url, requestedGeneration));
                pending[url] = task;
                return task;
            }
        }

        private async Task<string> GetAndRemoveAsync(string url, int requestedGeneration)
        {
            await Task.Yield();
            try
            {
                await storageGate.WaitAsync();
                try
                {
                    var cached = await store.ReadAsync(url);
                    if (!string.IsNullOrEmpty(cached)) return cached;
                }
                finally { storageGate.Release(); }
                await downloadGate.WaitAsync();
                string data;
                try
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                    {
                        response.EnsureSuccessStatusCode();
                        var type = response.Content.Headers.ContentType;
                        var mime = type == null ? "" : type.MediaType.ToLowerInvariant();
                        if (!new[] { "image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp", "image/x-icon", "image/vnd.microsoft.icon", "image/svg+xml" }.Contains(mime)) return null;
                        var maxBytes = Math.Min(16 * 1024 * 1024L, Math.Max(0, limit() * 3 / 4 - 128));
                        if (maxBytes <= 0 || response.Content.Headers.ContentLength > maxBytes) return null;
                        using (var input = await response.Content.ReadAsStreamAsync())
                        using (var output = new MemoryStream())
                        {
                            var buffer = new byte[32768];
                            int count;
                            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) != 0)
                            {
                                if (output.Length + count > maxBytes) return null;
                                output.Write(buffer, 0, count);
                            }
                            if (output.Length == 0) return null;
                            data = "data:" + mime + ";base64," + Convert.ToBase64String(output.ToArray());
                        }
                        // Honor explicit no-store responses, but still display them.
                        if (response.Headers.CacheControl != null && response.Headers.CacheControl.NoStore) return data;
                    }
                }
                finally { downloadGate.Release(); }
                await storageGate.WaitAsync();
                try
                {
                    if (requestedGeneration == generation && data.Length + 3 <= limit())
                    {
                        await TrimLockedAsync(Math.Max(0, limit() - data.Length - 3));
                        await store.WriteAsync(url, data);
                        data = await store.ReadAsync(url) ?? data;
                    }
                }
                finally { storageGate.Release(); }
                return data;
            }
            catch { return null; } // WebView falls back to the original URL.
            finally { lock (pending) pending.Remove(url); }
        }

        private async Task TrimLockedAsync(long target)
        {
            var entries = await store.ListAsync();
            var bytes = entries.Sum(x => x.Bytes);
            foreach (var entry in entries.OrderBy(x => x.Written))
            {
                if (bytes <= target) break;
                await store.DeleteAsync(entry.Key);
                bytes -= entry.Bytes;
            }
        }

        public async Task<long> GetSizeAsync()
        {
            await storageGate.WaitAsync();
            try { return (await store.ListAsync()).Sum(x => x.Bytes); }
            finally { storageGate.Release(); }
        }
        public async Task TrimAsync()
        {
            await storageGate.WaitAsync();
            try { await TrimLockedAsync(limit()); }
            finally { storageGate.Release(); }
        }
        public async Task ClearAsync()
        {
            await storageGate.WaitAsync();
            try { Interlocked.Increment(ref generation); await TrimLockedAsync(0); }
            finally { storageGate.Release(); }
        }
    }
}
