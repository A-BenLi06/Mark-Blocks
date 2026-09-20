// Only the OS boundary is replaced. Tests compile the production renderer,
// settings, save state machine, diff builder and normalization code unchanged.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MetroMarkdownEditor.Services
{
    // HTML fixture extraction only; the native local-stream resolver is built
    // against the real 8.1 SDK and still requires device verification.
    public static class PreviewResourceResolver
    {
        public static void Navigate(Windows.UI.Xaml.Controls.WebView view, string html) { view.NavigateToString(html); }
    }
}

namespace Windows.UI.Xaml
{
    public enum ElementTheme { Default, Light, Dark }
    public struct Thickness { public Thickness(double a, double b, double c, double d) { } }
}
namespace Windows.UI.Xaml.Controls
{
    public class WebView
    {
        public string Html;
        public void NavigateToString(string html) { Html = html; }
        public Task<string> InvokeScriptAsync(string name, IEnumerable<string> args) { return Task.FromResult("ok"); }
    }
}
namespace Windows.Storage
{
    public class StorageFile
    {
        public string Path;
        public string Name { get { return System.IO.Path.GetFileName(Path); } }
        public static Task<StorageFile> GetFileFromApplicationUriAsync(Uri uri)
        {
            return Task.FromResult(new StorageFile { Path = System.IO.Path.Combine(Program.SharedRoot, uri.AbsolutePath.TrimStart('/')) });
        }
    }
    public static class FileIO
    {
        public static Func<StorageFile, string, Task> WriteOverride;
        public static Task<string> ReadTextAsync(StorageFile file) { return File.ReadAllTextAsync(file.Path); }
        public static Task WriteTextAsync(StorageFile file, string text)
        {
            return WriteOverride != null ? WriteOverride(file, text) : File.WriteAllTextAsync(file.Path, text);
        }
    }
    public class Settings { public IDictionary<string, object> Values = new Dictionary<string, object>(); }
    public class ApplicationData
    {
        public static ApplicationData Current = new ApplicationData();
        public Settings LocalSettings = new Settings();
    }
}
