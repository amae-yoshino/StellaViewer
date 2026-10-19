using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyPdfAsync(string imagePath, string screenshotPath)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        var root = Path.Combine(Path.GetDirectoryName(imagePath)!, "pdf-checks");
        var path = Path.Combine(root, "pages.pdf");
        var probe = new MainWindow(false) { WatchFoldersEnabled = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
        try {
            probe.Show(); await probe.OpenAsync(root); await probe.activeDisplay;
            probe.Locations.SelectedItem = probe.model.Locations.Single(e => e.Path == path);
            await probe.navigation; await probe.activeDisplay;
            Check(probe.model.HasImage && probe.model.Entries.Count == 3 && probe.pdfSession?.PageCount == 3
                && probe.decoded?.Width == 288, "PDF opens from physical folder/archive list at 144 dpi");
            Check(probe.folderMonitor is null && probe.model.LocationFolder == root && probe.model.Details.Contains("元PDF容量")
                && probe.model.Details.Contains("元PDF更新日時") && !probe.model.Details.Contains("stella-pdf:"),
                "PDF page details identify document metadata without using virtual path for directory monitor");
            var first = (FileEntry)probe.ImageList.SelectedItem;
            await probe.ExecuteViewerCommandAsync(StellaViewer.Core.ViewerCommand.Last); await probe.activeDisplay;
            Check(PdfLocation.Parse(((FileEntry)probe.ImageList.SelectedItem).Path)?.Page == 3, "PDF last command opens final listed page");
            await probe.ExecuteViewerCommandAsync(StellaViewer.Core.ViewerCommand.First); await probe.activeDisplay;
            Check(((FileEntry)probe.ImageList.SelectedItem).Path == first.Path, "PDF first command opens initial listed page");
            probe.Move(1); await probe.activeDisplay;
            var second = (FileEntry)probe.ImageList.SelectedItem;
            Check(PdfLocation.Parse(second.Path)?.Page == 2 && probe.model.HasImage, "PDF next page renders page 2");
            Check(probe.TryJumpToPage(3), "PDF document page number jump accepted"); await probe.activeDisplay;
            Check(PdfLocation.Parse(((FileEntry)probe.ImageList.SelectedItem).Path)?.Page == 3, "PDF page jump selects document page 3");
            probe.Move(1); await probe.activeDisplay;
            Check(((FileEntry)probe.ImageList.SelectedItem).Path == first.Path, "PDF last page wraps to first in single mode");
            Check(!probe.TryJumpToPage(0) && !probe.TryJumpToPage(4), "PDF page jump rejects out-of-range document pages");
            await probe.SetSpreadModeAsync(true);
            Check(probe.firstPage?.Bitmap is not null && probe.secondPage?.Bitmap is not null, "PDF spread renders two pages");
            probe.Move(1); await probe.activeDisplay;
            Check(probe.firstPage?.Entry.Name == "ページ 3" && probe.secondPage is null && probe.model.HasImage,
                "Odd PDF final spread uses blank second page");
            await probe.SetSpreadModeAsync(false);
            probe.SelectedImageSort = 2; await probe.FilterAsync(); await probe.activeDisplay;
            Check(PdfLocation.Parse(((FileEntry)probe.ImageList.SelectedItem).Path)?.Page == 3, "PDF source size sorting keeps page selection and natural page order");
            probe.SearchBox.Text = "2"; await probe.FilterAsync(); await probe.activeDisplay;
            Check(probe.model.Entries.Count == 1 && ((FileEntry)probe.ImageList.SelectedItem).Path == second.Path && probe.model.HasImage,
                "PDF search filters page names and selects first match");
            Check(probe.TryJumpToPage(1) && !probe.TryJumpToPage(2), "Filtered PDF jump uses search result index");
            probe.SearchBox.Text = "no-match"; await probe.FilterAsync();
            Check(!probe.model.HasImage && probe.model.Entries.Count == 0, "PDF no-match search clears viewport");
            probe.SearchBox.Text = ""; await probe.FilterAsync(); await probe.activeDisplay;
            probe.TryJumpToPage(2); await probe.activeDisplay;
            probe.ImageAddress.BeginEdit();
            Check(Descendants(probe.ImageAddress).OfType<TextBox>().Single().Text == path, "PDF breadcrumb editor contains physical document path");
            var request = probe.openRequestVersion;
            probe.ImageAddress.SubmitAddress(second.Path); await probe.navigation;
            Check(probe.openRequestVersion == request, "PDF internal page identities are not editable addresses");
            probe.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var preview = Descendants(probe.ListArea).OfType<Thumbnail>().FirstOrDefault(t => t.FilePath == second.Path);
            for (var i = 0; preview?.Source is null && i < 50; i++) await Task.Delay(50);
            Check(preview?.Source is not null && preview.PdfSession is not null, "PDF page thumbnails render through active document session");
            var thumbnails = Descendants(probe.ListArea).OfType<Thumbnail>().Where(t => t.IsVisible).ToArray();
            for (var i = 0; thumbnails.Any(t => t.Source is null) && i < 50; i++) await Task.Delay(50);
            Check(thumbnails.Length == 3 && thumbnails.All(t => t.Source is not null), "All visible PDF thumbnails eventually finish rendering");
            string? revealed = null, copied = null, associated = null;
            probe.revealFile = (value, _) => { revealed = value; return Task.CompletedTask; };
            probe.copyText = value => copied = value;
            probe.openAssociated = (value, _) => { associated = value; return Task.CompletedTask; };
            var menu = (ContextMenu)probe.Resources["FileContextMenu"];
            menu.PlacementTarget = new ListBoxItem { DataContext = probe.ImageList.SelectedItem };
            probe.FileContextMenuOpened(menu, new());
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Check(!items[0].IsEnabled && !items[1].IsEnabled && items[2].IsEnabled && !items[3].IsEnabled,
                "PDF page menu enables document reveal without offering virtual file operations");
            probe.CopyFullPathClicked(items[0], new()); await probe.OpenAssociatedAsync(items[3]); await probe.RevealFileAsync(items[2]);
            Check(copied is null && associated is null && revealed == path, "PDF page external actions resolve document without exposing internal identity");
            probe.UpdateStatus();
            var content = (FrameworkElement)probe.Content;
            var rendered = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(content); SaveRender(rendered, screenshotPath + ".pdf.png");
            await probe.UpAsync(); await probe.activeDisplay;
            Check(probe.model.Folder == root && probe.model.SelectedLocationPath == path, "Up from PDF returns to parent and selects document without reopening");
            await probe.GoBackAsync(); await probe.activeDisplay;
            Check(((FileEntry)probe.ImageList.SelectedItem).Path == second.Path && probe.model.HasImage, "PDF back restores document and selected page");
            await probe.GoForwardAsync(); await probe.activeDisplay;
            Check(probe.model.Folder == root, "PDF forward restores physical parent");
            await probe.OpenAsync(second.Path); await probe.activeDisplay;
            await probe.ReloadAsync(); await probe.activeDisplay;
            Check(((FileEntry)probe.ImageList.SelectedItem).Path == second.Path && probe.model.HasImage, "F5 rebuilds PDF document and preserves page");
            await probe.OpenAsync(new PdfLocation(path, 99).Identity); await probe.activeDisplay;
            Check(((FileEntry)probe.ImageList.SelectedItem).Path == first.Path && probe.model.Status.Contains("見つからない"), "Missing saved PDF page falls back to first with notice");
            var heldImage = probe.model.Image;
            await probe.OpenAsync(Path.Combine(root, "protected.pdf"));
            Check(ReferenceEquals(heldImage, probe.model.Image) && probe.model.Status.Contains("パスワード"), "Password protected PDF reports unsupported and retains previous display");
            await probe.OpenAsync(Path.Combine(root, "broken.pdf"));
            Check(ReferenceEquals(heldImage, probe.model.Image) && probe.model.Status.Contains("PDFを開けません"), "Broken PDF reports failure and retains previous display");
            var opening = probe.OpenAsync(path); var latest = probe.OpenAsync(imagePath);
            await Task.WhenAll(opening, latest); await probe.activeDisplay;
            Check(probe.model.HasImage && probe.model.Folder == Path.GetDirectoryName(imagePath) && probe.pdfSession is null,
                "Latest ordinary image request wins over PDF open");
            var storage = Path.Combine(root, "state-" + Guid.NewGuid().ToString("N"));
            var saved = new MainWindow(true, storage) { WatchFoldersEnabled = false, ConstrainStartupWindowToScreen = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            saved.Show(); await saved.OpenAsync(second.Path); await saved.activeDisplay; saved.Close(); await saved.closePersistence;
            var restored = new MainWindow(true, storage) { WatchFoldersEnabled = false, ConstrainStartupWindowToScreen = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            try {
                restored.Show(); await restored.RestoreStartupAsync(); await restored.activeDisplay;
                Check((restored.ImageList.SelectedItem as FileEntry)?.Path == second.Path && restored.model.HasImage,
                    "Real close/restart persistence restores PDF page and renders again");
            }
            finally { restored.Close(); await restored.closePersistence; }
        }
        finally { probe.Close(); }
    }
}
