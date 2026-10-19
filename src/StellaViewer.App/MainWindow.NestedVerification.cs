using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyNestedArchivesAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
        var root = Path.Combine(Path.GetDirectoryName(imagePath)!, "nested-checks");
        var path = Path.Combine(root, "outer.zip");
        var inner = new ArchiveLocation(path, "books/inner.zip", false);
        var leaf = new ArchiveLocation(inner.Identity, "children/leaf.cbz", false);
        var selected = new ArchiveLocation(leaf.Identity, "sub/10.png", false).Identity;
        var document = new ArchiveLocation(leaf.Identity, "sub/book.pdf", false);
        var pdfPage = new PdfLocation(document.Identity, 2).Identity;
        var storage = Path.Combine(root, "ui-" + Guid.NewGuid().ToString("N"));
        var probe = new MainWindow(false, storage) { WatchFoldersEnabled = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
        async Task SelectLocation(string name) {
            var target=probe.model.Locations.Single(i => i.Name == name);
            await probe.OpenAsync(target.Path); await probe.activeDisplay;
        }
        try {
            probe.Show(); await probe.OpenAsync(path); await probe.activeDisplay;
            var store = await probe.GetTemporaryStoreAsync();
            Check(store.UsedBytes == 0, "Listing nested archive does not expand it automatically");
            await SelectLocation("books"); await SelectLocation("inner.zip");
            Check(probe.archiveSession?.SourcePath == inner.Identity && probe.model.HasImage && probe.model.Entries.Single().Name == "1.png",
                "Explicit nested archive selection opens its direct image list");
            var innerBytes = store.UsedBytes;
            await SelectLocation("children"); await SelectLocation("leaf.cbz"); await SelectLocation("sub");
            Check(probe.model.Entries.Select(i => i.Name).SequenceEqual(new[] { "2.png", "10.png" }) && probe.model.Locations.Single().Kind == "PDF",
                "Second nested archive separates image list from contained PDF");
            probe.ImageList.SelectedItem = probe.model.Entries.Single(i => i.Path == selected); await probe.activeDisplay;
            probe.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var labels = Descendants(probe.ImageAddress).OfType<Button>().Select(i => i.Content?.ToString()).ToArray();
            Check(labels.Contains("outer.zip") && labels.Contains("inner.zip") && labels.Contains("leaf.cbz") && labels.Contains("10.png"),
                "Nested breadcrumbs display every archive and selected image");
            Check(!probe.model.Details.Contains("stella-archive:") && !probe.model.Details.Contains("Temp"), "Nested details never reveal temporary or internal source paths");
            probe.ImageAddress.BeginEdit();
            Check(Descendants(probe.ImageAddress).OfType<TextBox>().Single().Text == path, "Nested address editor uses outer physical archive");
            probe.ImageAddress.SubmitAddress(selected);
            await probe.UpAsync(); await probe.activeDisplay;
            Check(probe.model.Folder == new ArchiveLocation(leaf.Identity).Identity, "Up from nested subfolder reaches that archive root");
            await probe.UpAsync(); await probe.activeDisplay;
            Check(probe.model.Folder == leaf.Folder.Identity && probe.model.SelectedLocationPath == leaf.Identity,
                "Up from nested archive root returns to containing outer folder and selects archive");
            for (var i = 0; store.UsedBytes != innerBytes && i < 50; i++) await Task.Delay(50);
            Check(store.UsedBytes == innerBytes, "Returning outward releases child archive while retaining parent input");
            await probe.GoBackAsync(); await probe.activeDisplay;
            Check(probe.model.Folder == new ArchiveLocation(leaf.Identity).Identity, "Back restores nested archive root");
            await probe.GoBackAsync(); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Path == selected && probe.model.HasImage, "Back restores selected image inside nested subfolder");
            await probe.ReloadAsync(); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Path == selected && probe.model.HasImage, "Nested F5 rebuilds chain while preserving selected image");
            await SelectLocation("book.pdf");
            Check(probe.model.HasImage && probe.pdfSession?.SourcePath == document.Identity, "PDF opens from nested archive list");
            probe.Move(1); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Path == pdfPage, "Nested PDF page navigation uses logical source hierarchy");
            probe.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var content = (FrameworkElement)probe.Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content); SaveRender(bitmap, screenshotPath + ".nested-pdf.png");
            await probe.UpAsync(); await probe.activeDisplay;
            Check(probe.model.Folder == document.Folder.Identity && probe.model.SelectedLocationPath == document.Identity, "Nested PDF Up returns to same nested internal folder");
            await probe.GoBackAsync(); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Path == pdfPage && probe.model.HasImage, "Back restores nested PDF page");
            await probe.ReloadAsync(); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Path == pdfPage && probe.model.HasImage, "Nested PDF F5 reopens all source levels");
            var missing = new ArchiveLocation(new ArchiveLocation(path, "books/missing.zip", false).Identity, "1.png", false);
            await probe.OpenAsync(missing.Identity); await probe.activeDisplay;
            Check(probe.model.Folder == new ArchiveLocation(path, "books").Identity && probe.model.Status.Contains("見つからない"),
                "Missing nested archive restoration falls back to containing outer folder");
            await probe.OpenAsync(selected); await probe.activeDisplay;
            var competing = probe.OpenAsync(pdfPage); var latest = probe.OpenAsync(imagePath);
            await Task.WhenAll(competing, latest); await probe.activeDisplay;
            Check(probe.model.HasImage && probe.model.Folder == Path.GetDirectoryName(imagePath), "Latest physical image request wins over nested PDF opening");
            for (var i = 0; store.UsedBytes > 0 && i < 60; i++) await Task.Delay(50);
            Check(store.UsedBytes == 0, "Leaving nested browsing releases entire archive chain");
            foreach (var target in new[] { selected, pdfPage }) {
                var state = Path.Combine(storage, "saved-" + Guid.NewGuid().ToString("N"));
                var saved = new MainWindow(true, state) { WatchFoldersEnabled = false, ConstrainStartupWindowToScreen = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
                saved.Show(); await saved.OpenAsync(target); await saved.activeDisplay;
                Check(saved.model.HasImage, "Saving nested source begins with decoded image");
                var savedStore = await saved.GetTemporaryStoreAsync(); saved.Close(); await saved.closePersistence;
                Check(!Directory.Exists(savedStore.DirectoryPath), "Nested normal close removes every temporary input");
                var restored = new MainWindow(true, state) { WatchFoldersEnabled = false, ConstrainStartupWindowToScreen = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
                try {
                    restored.Show(); await restored.RestoreStartupAsync(); await restored.activeDisplay;
                    Check((restored.ImageList.SelectedItem as FileEntry)?.Path == target && restored.model.HasImage, "Restart reconstructs nested image or PDF source and selected page");
                }
                finally { restored.Close(); await restored.closePersistence; }
            }
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
