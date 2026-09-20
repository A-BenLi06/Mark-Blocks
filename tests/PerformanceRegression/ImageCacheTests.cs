using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MetroMarkdownEditor.Services;

static class ImageCacheTests
{
    sealed class Store : IImageCacheStore
    {
        public Dictionary<string, string> Data = new Dictionary<string, string>();
        public bool ResourceUrls;
        readonly Dictionary<string, DateTimeOffset> dates = new Dictionary<string, DateTimeOffset>();
        public Task<string> ReadAsync(string url) { string s; return Task.FromResult(Data.TryGetValue(url, out s) ? (ResourceUrls ? "/image/example.cache.png" : s) : null); }
        public Task WriteAsync(string url, string data) { Data[url] = data; dates[url] = DateTimeOffset.UtcNow; return Task.CompletedTask; }
        public Task DeleteAsync(string key) { Data.Remove(key); return Task.CompletedTask; }
        public Task<IReadOnlyList<ImageCacheEntry>> ListAsync()
        { return Task.FromResult<IReadOnlyList<ImageCacheEntry>>(Data.Select(p => new ImageCacheEntry { Key = p.Key, Bytes = p.Value.Length + 3, Written = dates[p.Key] }).ToList()); }
    }
    sealed class Handler : HttpMessageHandler
    {
        public int Requests;
        public TaskCompletionSource<bool> Started;
        public TaskCompletionSource<bool> Release;
        public string Mime = "image/png";
        public bool NoStore;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            if (Started != null) Started.TrySetResult(true);
            if (Release != null) await Release.Task;
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[100]) };
            result.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(Mime);
            result.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = NoStore };
            return result;
        }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS image cache: " + message); }
    public static async Task RunAsync()
    {
        var store = new Store(); var handler = new Handler(); long limit = 400;
        var cache = new OnlineImageCache(store, new HttpClient(handler), () => limit);
        var a = "https://example.test/a.png";
        var first = await cache.GetAsync(a);
        Check(first.StartsWith("data:image/png;base64,"), "download becomes local display source");
        Check(await cache.GetAsync(a) == first && handler.Requests == 1, "second request reads disk store without network");
        var reopened = new OnlineImageCache(store, new HttpClient(handler), () => limit);
        Check(await reopened.GetAsync(a) == first && handler.Requests == 1, "new cache instance reuses stored image");
        await cache.GetAsync("https://example.test/b.png");
        await cache.GetAsync("https://example.test/c.png");
        Check(await cache.GetSizeAsync() <= limit && !store.Data.ContainsKey(a), "capacity evicts oldest entries including encoding overhead");
        limit = 0; await cache.TrimAsync();
        var oldRequests = handler.Requests;
        Check(await cache.GetSizeAsync() == 0 && await cache.GetAsync(a) == null && handler.Requests == oldRequests, "zero clears and disables native caching");
        limit = 400;
        handler.Started = new TaskCompletionSource<bool>(); handler.Release = new TaskCompletionSource<bool>();
        var pending1 = cache.GetAsync(a); await handler.Started.Task;
        var pending2 = cache.GetAsync(a);
        Check(object.ReferenceEquals(pending1, pending2), "concurrent identical requests are coalesced");
        await cache.ClearAsync(); handler.Release.SetResult(true);
        await Task.WhenAll(pending1, pending2);
        Check(await cache.GetSizeAsync() == 0, "clear prevents in-flight download repopulating cache");
        handler.Release = null; handler.Started = null;
        handler.Mime = "text/html";
        Check(await cache.GetAsync(a) == null && await cache.GetSizeAsync() == 0, "non-image response is not cached");
        handler.Mime = "image/png"; handler.NoStore = true;
        Check(await cache.GetAsync(a) != null && await cache.GetSizeAsync() == 0, "no-store image displays without persisting");
        handler.NoStore = false; limit = 100;
        Check(await cache.GetAsync(a) == null && await cache.GetSizeAsync() == 0, "oversize image bypasses cache");
        Check(await cache.GetAsync("file:///secret") == null, "only http and https use cache");
        var resourceStore = new Store { ResourceUrls = true };
        var resourceCache = new OnlineImageCache(resourceStore, new HttpClient(new Handler()), () => 400);
        Check(await resourceCache.GetAsync(a) == "/image/example.cache.png", "new download returns a short resource URL after persistence");
        Check(await resourceCache.GetAsync(a) == "/image/example.cache.png", "cache hit preserves resource URL without image bridge payload");
    }
}
