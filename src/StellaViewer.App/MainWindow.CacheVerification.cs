using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyCacheAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var saved = CurrentPreferences;
        try {
            await ApplyPreferencesAsync(CurrentPreferences with { Cache = new(16, 0, 0), SpreadMode = false });
            await imageCache.InvalidateAsync([imagePath]);
            await OpenAsync(imagePath); await activeDisplay;
            var image = model.Image; var selected = ImageList.SelectedItem; var zoomBefore = zoom; var count = imageCache.Decodes;
            await LoadSelectedAsync();
            Check(imageCache.Decodes == count && imageCache.MemoryHits > 0, "Repeated display uses memory cache");
            image = model.Image;
            var dialog = new SettingsWindow(CurrentPreferences, ApplyPreferencesAsync) {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false
            };
            dialog.Show(); ((TabControl)dialog.FindName("SettingsTabs")).SelectedIndex = 7;
            ((TextBox)dialog.FindName("MemoryCacheMiB")).Text = "-1";
            Check(!await dialog.ApplyChangesAsync(), "Invalid cache capacity does not apply");
            ((TextBox)dialog.FindName("MemoryCacheMiB")).Text = "0";
            Check(await dialog.ApplyChangesAsync() && dialog.IsVisible && imageCache.MemoryBytes == 0,
                "Memory zero applies and releases reusable pixels without closing settings");
            Check(ReferenceEquals(image, model.Image) && ReferenceEquals(selected, ImageList.SelectedItem) && zoom == zoomBefore,
                "Changing cache capacity preserves displayed image, selection and scale");
            dialog.UpdateLayout(); var content = (FrameworkElement)dialog.Content; var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); SaveRender(bitmap, screenshotPath + ".cache-settings.png");
            dialog.ResetValue("cache"); Check(dialog.Values.Cache == new ImageCacheSettings(), "Cache reset restores approved defaults"); dialog.Close();
            Check(cacheSettings.MemoryMiB == 0, "Cancel discards cache edits after Apply");
            count = imageCache.Decodes; await LoadSelectedAsync();
            Check(imageCache.Decodes == count + 1, "Display continues decoding with reusable cache disabled");
            await ApplyPreferencesAsync(CurrentPreferences with { Cache = new(16, 0, 2) });
            await OpenAsync(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(imagePath)!, "spread-5", "page3.png")); await activeDisplay; await prefetchWork;
            Check(imageCache.MemoryCount >= 4 && imageCache.MemoryBytes <= cacheSettings.MemoryBytes, "Neighbor prefetch stays within memory budget");
            count = imageCache.Decodes; Move(1); await activeDisplay;
            Check(imageCache.Decodes == count, "Page turn reuses prefetched neighbor without decoding");
            await ReloadAsync(); await activeDisplay;
            Check(imageCache.Decodes > count, "F5 bypasses cached pages");
        }
        finally { await ApplyPreferencesAsync(saved); await OpenAsync(imagePath); await activeDisplay; }
    }
}
