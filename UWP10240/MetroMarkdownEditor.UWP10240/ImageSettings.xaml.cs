using MetroMarkdownEditor.Services;
using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.Windows
{
    public sealed partial class ImageSettings : SettingsFlyout
    {
        public ImageSettings()
        {
            InitializeComponent();
            DataContext = ImageSettingsService.Instance;
            CacheLimitBox.Text = ImageSettingsService.Instance.CacheLimitMegabytes.ToString();
            Loaded += async (sender, args) => await RefreshCacheSizeAsync();
        }

        private async Task RefreshCacheSizeAsync()
        {
            try
            {
                var size = await ImageCacheService.Cache.GetSizeAsync();
                CacheSizeText.Text = string.Format("Used: {0:F2} MB / {1} MB", size / 1048576.0, ImageSettingsService.Instance.CacheLimitMegabytes);
            }
            catch { CacheSizeText.Text = "Cache size unavailable."; }
        }

        private async void RefreshCacheSize_Click(object sender, RoutedEventArgs e)
        {
            await RefreshCacheSizeAsync();
        }

        private async void ApplyCacheLimit_Click(object sender, RoutedEventArgs e)
        {
            int limit;
            if (!int.TryParse(CacheLimitBox.Text, out limit) || limit < 0 || limit > 4096)
            { CacheStatusText.Text = "Enter a whole number from 0 to 4096."; return; }
            try
            {
                ImageSettingsService.Instance.CacheLimitMegabytes = limit;
                await ImageCacheService.Cache.TrimAsync();
                CacheStatusText.Text = "Limit saved.";
                await RefreshCacheSizeAsync();
            }
            catch { CacheStatusText.Text = "Could not resize the cache. Try again."; }
        }

        private async void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ImageCacheService.Cache.ClearAsync();
                CacheStatusText.Text = "Cache cleared. Displayed images remain until the preview reloads.";
                await RefreshCacheSizeAsync();
            }
            catch { CacheStatusText.Text = "Could not clear the cache. Try again."; }
        }
    }
}
