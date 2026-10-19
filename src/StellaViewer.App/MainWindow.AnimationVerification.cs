using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyAnimationAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var root = Path.Combine(Path.GetDirectoryName(imagePath)!, "animation");
        var probe = new MainWindow(false) { WatchFoldersEnabled = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
        try {
            probe.Show();
            foreach (var extension in new[] { "gif", "webp", "png" }) {
                await probe.OpenAsync(Path.Combine(root, "animated." + extension)); await probe.activeDisplay;
                Check(probe.animationBitmaps.Length == 3 && probe.animationTimer?.IsEnabled == true, extension + " starts automatic playback");
                var first = probe.model.Image;
                var wait = Stopwatch.StartNew();
                while (ReferenceEquals(first, probe.model.Image) && wait.ElapsedMilliseconds < 2000) await Task.Delay(25);
                Check(!ReferenceEquals(first, probe.model.Image), extension + " timer presents another frame");
                probe.RotateRight(probe, new()); probe.SetZoom(300);
                var rotation = probe.Rotation.Angle; var scale = probe.zoom;
                probe.loupeEnabled = true; probe.loupePointerInside = true; probe.loupePoint = new(100,100);
                probe.ApplyAnimationFrame((probe.animationFrame + 1) % 3);
                Check(probe.Rotation.Angle == rotation && probe.zoom == scale && probe.LoupeLayer.Visibility == Visibility.Visible,
                    extension + " frame changes preserve zoom, rotation and loupe");
            }
            await probe.SetSpreadModeAsync(true);
            await probe.OpenAsync(Path.Combine(root, "spread", "1.gif")); await probe.activeDisplay;
            Check(!probe.animationSecondPage && probe.animationBitmaps.Length == 3, "Spread animates earlier page when both are animated");
            var secondBitmap = probe.secondPage!.Bitmap; var firstBitmap = probe.firstPage!.Bitmap;
            probe.ApplyAnimationFrame((probe.animationFrame + 1) % 3);
            Check(!ReferenceEquals(firstBitmap, probe.firstPage.Bitmap) && ReferenceEquals(secondBitmap, probe.secondPage.Bitmap), "Spread changes only one page");
            await probe.OpenAsync(Path.Combine(root, "spread", "3.png")); await probe.activeDisplay;
            Check(probe.animationSecondPage && probe.animationBitmaps.Length == 3, "Spread animates later page when earlier page is static");
            probe.animationTimer!.Stop(); probe.ApplyAnimationFrame(1); probe.loupeEnabled = false; probe.RefreshLoupe(); probe.SetZoom(1000); probe.UpdateLayout();
            var content = (FrameworkElement)probe.Content; var snapshot = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            snapshot.Render(content); SaveRender(snapshot, screenshotPath + ".animation-spread.png");
            await probe.SetSpreadModeAsync(false);
            await probe.OpenAsync(Path.Combine(root, "static.png")); await probe.activeDisplay;
            var still = probe.model.Image; await Task.Delay(200);
            Check(probe.animationBitmaps.Length == 0 && probe.animationTimeline is null && probe.animationTimer?.IsEnabled != true
                && ReferenceEquals(still, probe.model.Image), "Static image releases old animation and remains unchanged");
            probe.AnimationLimits = new(PixelBytes: 128);
            await probe.OpenAsync(Path.Combine(root, "animated.gif")); await probe.activeDisplay;
            Check(probe.model.HasImage && probe.animationBitmaps.Length == 0 && probe.model.Status.Contains("先頭フレームを静止表示"), "Animation limit keeps still frame with persistent notice");
            probe.SetZoom(200); Check(probe.model.Status.Contains("メモリ上限"), "Zoom does not erase animation limit notice");
            probe.AnimationLimits = new();
            await probe.ReloadAsync(); await probe.activeDisplay;
            Check(probe.animationBitmaps.Length == 3, "Reload recreates animation after limit changes");
            var oldOpen = probe.OpenAsync(Path.Combine(root, "animated.png"));
            await probe.OpenAsync(Path.Combine(root, "static.png")); await oldOpen; await probe.activeDisplay;
            Check(probe.animationBitmaps.Length == 0 && (probe.ImageList.SelectedItem as FileEntry)?.Name == "static.png", "Stale animation request does not replace newer image");
            var zipPath = Path.Combine(root, "animations.zip");
            await probe.OpenAsync(new ArchiveLocation(zipPath, "2.png", false).Identity); await probe.activeDisplay;
            Check(probe.animationBitmaps.Length == 3 && probe.animationTimer?.IsEnabled == true, "Archive APNG plays automatically");
            var store = await probe.GetTemporaryStoreAsync();
            Check(store.UsedBytes == 0, "Archive animation source is removed after frames are decoded");
            await probe.OpenAsync(new ArchiveLocation(zipPath, "3.png", false).Identity); await probe.activeDisplay;
            Check(probe.animationBitmaps.Length == 0 && store.UsedBytes == 0, "Archive static PNG stays still without temporary extraction");
            var nested = new ArchiveLocation(Path.Combine(root, "outer.zip"), "inner.zip", false);
            await probe.OpenAsync(new ArchiveLocation(nested.Identity, "1.gif", false).Identity); await probe.activeDisplay;
            Check(probe.animationBitmaps.Length == 3 && probe.archiveSession?.SourcePath == nested.Identity, "Nested archive GIF plays while retaining ancestor source");
            probe.Close(); await probe.TemporaryCleanup;
            Check(probe.animationBitmaps.Length == 0 && probe.animationTimer?.IsEnabled != true && store.UsedBytes == 0, "Closing stops animation and releases temporary source chain");
        }
        finally { if (!probe.closed) { probe.Close(); await probe.TemporaryCleanup; } }
    }
}
