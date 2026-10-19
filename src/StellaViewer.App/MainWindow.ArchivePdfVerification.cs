using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyArchivePdfAsync(string imagePath, string screenshotPath)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        var root = Path.Combine(Path.GetDirectoryName(imagePath)!, "archive-pdf-checks");
        var path = Path.Combine(root, "book.zip");
        var storage = Path.Combine(root, "ui-" + Guid.NewGuid().ToString("N"));
        var probe = new MainWindow(false, storage) { WatchFoldersEnabled = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
        var document = new ArchiveLocation(path, "日本語/book.pdf", false);
        var page2 = new PdfLocation(document.Identity, 2).Identity;
        try {
            probe.Show(); await probe.OpenAsync(new ArchiveLocation(path, "日本語").Identity); await probe.activeDisplay;
            probe.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var preview = Descendants(probe.LocationArea).OfType<Thumbnail>().FirstOrDefault(t => t.FilePath == document.Identity);
            for (var i = 0; preview?.Source is null && i < 50; i++) await Task.Delay(50);
            Check(preview?.Source is not null, "Archive PDF first-page preview renders inside archive location list");
            probe.Locations.SelectedItem = probe.model.Locations.Single(i => i.Path == document.Identity);
            await probe.navigation; await probe.activeDisplay;
            Check(probe.model.HasImage && probe.model.Entries.Count == 3 && probe.pdfSession?.SourcePath == document.Identity,
                "Archive PDF opens from location selection and uses logical document path");
            probe.Move(1); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Path == page2 && probe.model.HasImage, "Archive PDF page forwarding selects second document page");
            Check(probe.model.Details.Contains("日本語 › book.pdf") && !probe.model.Details.Contains("stella-") && !probe.model.Details.Contains("Temp"),
                "Archive PDF details show archive hierarchy instead of temporary extraction path");
            probe.ImageAddress.BeginEdit();
            Check(Descendants(probe.ImageAddress).OfType<TextBox>().Single().Text == path, "Archive PDF address edit contains only outer physical archive");
            var request = probe.openRequestVersion;
            probe.ImageAddress.SubmitAddress(page2);
            Check(probe.openRequestVersion == request, "Archive PDF internal page address input stays disabled");
            probe.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var labels = Descendants(probe.ImageAddress).OfType<Button>().Select(i => i.Content?.ToString()).ToArray();
            Check(labels.Contains("book.zip") && labels.Contains("日本語") && labels.Contains("book.pdf") && labels.Contains("ページ 2"),
                "Archive PDF breadcrumb contains outer archive, folder, document and page");
            string? revealed = null;
            probe.revealFile = (value, _) => { revealed = value; return Task.CompletedTask; };
            await probe.RevealFileAsync(new MenuItem());
            Check(revealed == path, "Archive PDF Explorer action targets outer archive");
            probe.UpdateStatus();
            var content = (FrameworkElement)probe.Content;
            var rendered = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(content); SaveRender(rendered, screenshotPath + ".archive-pdf.png");
            await probe.SetSpreadModeAsync(true);
            Check(probe.firstPage?.Bitmap is not null && probe.secondPage?.Bitmap is not null, "Archive PDF spread uses active extracted document");
            await probe.SetSpreadModeAsync(false);
            await probe.ReloadAsync(); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Path == page2 && probe.model.HasImage, "Archive PDF reload re-extracts document and preserves selected page");
            await probe.UpAsync(); await probe.activeDisplay;
            Check(probe.model.Folder == document.Folder.Identity && probe.model.SelectedLocationPath == document.Identity && probe.model.HasImage,
                "Up from archive PDF returns to containing internal folder and selects PDF without reopening");
            await probe.GoBackAsync(); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Path == page2 && probe.model.HasImage, "Back restores archive PDF page");
            await probe.GoForwardAsync(); await probe.activeDisplay;
            Check(probe.model.Folder == document.Folder.Identity, "Forward restores internal archive folder");
            await probe.OpenAsync(new PdfLocation(new ArchiveLocation(path, "日本語/missing.pdf", false).Identity, 2).Identity); await probe.activeDisplay;
            Check(probe.model.Folder == document.Folder.Identity && probe.model.Status.Contains("見つからない"), "Missing restored archive PDF falls back to its internal parent with notice");
            await probe.OpenAsync(page2); await probe.activeDisplay;
            var outerRequest = probe.OpenAsync(page2); var latest = probe.OpenAsync(imagePath);
            await Task.WhenAll(outerRequest, latest); await probe.activeDisplay;
            Check(probe.model.Folder == Path.GetDirectoryName(imagePath) && probe.pdfSession is null && probe.model.HasImage,
                "Latest normal navigation wins over archive PDF opening");
            var store = await probe.GetTemporaryStoreAsync();
            for (var i = 0; store.UsedBytes > 0 && i < 50; i++) await Task.Delay(50);
            Check(store.UsedBytes == 0 && Directory.GetFiles(store.DirectoryPath, "*.pdf").Length == 0, "Leaving archive PDF releases expanded files after pending thumbnails finish");
            var saved = new MainWindow(true, Path.Combine(storage, "saved")) { WatchFoldersEnabled = false, ConstrainStartupWindowToScreen = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            saved.Show(); await saved.OpenAsync(page2); await saved.activeDisplay;
            var savedStore = await saved.GetTemporaryStoreAsync(); saved.Close(); await saved.closePersistence;
            Check(!Directory.Exists(savedStore.DirectoryPath), "Normal close removes archive PDF temporary owner directory");
            var restored = new MainWindow(true, Path.Combine(storage, "saved")) { WatchFoldersEnabled = false, ConstrainStartupWindowToScreen = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            try {
                restored.Show(); await restored.RestoreStartupAsync(); await restored.activeDisplay;
                Check((restored.ImageList.SelectedItem as FileEntry)?.Path == page2 && restored.model.HasImage,
                    "Restart restores archive PDF page by re-extracting from outer archive");
            }
            finally { restored.Close(); await restored.closePersistence; }
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
