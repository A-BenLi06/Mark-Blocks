using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using Windows.Web;

namespace MetroMarkdownEditor.Services
{
    public sealed class PreviewResourceResolver : IUriToStreamResolver
    {
        private readonly string html;
        private PreviewResourceResolver(string html) { this.html = html; }
        public static void Navigate(WebView view, string html)
        {
            view.NavigateToLocalStreamUri(view.BuildLocalStreamUri("preview", "/index.html"), new PreviewResourceResolver(html));
        }
        public IAsyncOperation<IInputStream> UriToStreamAsync(Uri uri)
        {
            return Task.Run(async () =>
            {
                byte[] bytes;
                if (uri.AbsolutePath == "/index.html") bytes = Encoding.UTF8.GetBytes(html);
                else
                {
                    // KaTeX CSS uses relative font URLs. Serve only packaged fonts,
                    // never resolve arbitrary paths supplied by document HTML.
                    if (Regex.IsMatch(uri.AbsolutePath, @"^/fonts/KaTeX_[A-Za-z0-9_-]+\.(woff2?|ttf)$"))
                    {
                        var font = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/KaTex" + uri.AbsolutePath));
                        return (IInputStream)await font.OpenAsync(FileAccessMode.Read);
                    }
                    var match = Regex.Match(uri.AbsolutePath, @"^/image/([a-fA-F0-9]{64}\.cache)\.(png|jpg|gif|webp|bmp|ico|svg)$");
                    if (!match.Success)
                    {
                        // WebView may probe favicon or relative document resources.
                        // An empty resource fails decoding without a managed exception
                        // (and without stopping the debugger on every ordinary probe).
                        System.Diagnostics.Debug.WriteLine("Preview resource unavailable: " + uri.AbsolutePath);
                        return (IInputStream)new InMemoryRandomAccessStream();
                    }
                    var folder = await ApplicationData.Current.LocalFolder.GetFolderAsync("OnlineImageCache");
                    var data = await FileIO.ReadTextAsync(await folder.GetFileAsync(match.Groups[1].Value));
                    var comma = data.IndexOf(',');
                    if (comma < 0 || !data.StartsWith("data:image/", StringComparison.Ordinal)) throw new IOException("Invalid cached image");
                    bytes = Convert.FromBase64String(data.Substring(comma + 1));
                }
                // The Windows 8.1 WebView also queries the returned object for
                // native random-access stream interfaces. A managed Stream
                // adapter can fail that query even though it is IInputStream.
                var stream = new InMemoryRandomAccessStream();
                try
                {
                    using (var writer = new DataWriter(stream))
                    {
                        writer.WriteBytes(bytes);
                        await writer.StoreAsync();
                        writer.DetachStream();
                    }
                    stream.Seek(0);
                    // WebView owns the stream lifetime after this return.
                    return (IInputStream)stream;
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }).AsAsyncOperation();
        }
    }
}
