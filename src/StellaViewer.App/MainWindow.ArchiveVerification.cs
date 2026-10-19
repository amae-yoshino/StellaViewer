using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyArchivesAsync(string imagePath, string screenshotPath)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        var archivePath = Path.Combine(Path.GetDirectoryName(imagePath)!, "archive-checks", "book.cbz");
        var probe = new MainWindow(false) { WatchFoldersEnabled = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
        try {
            probe.Show();
            await probe.OpenAsync(imagePath); await probe.activeDisplay;
            await probe.OpenAsync(archivePath); await probe.activeDisplay;
            Check(probe.model.HasImage && ArchiveLocation.Parse(probe.model.Folder)?.EntryPath == ""
                && probe.model.Entries.Count == 3, "Archive opens root images without flattening folders");
            Check(probe.model.Locations.Any(i => i.Name == "日本語") && probe.folderMonitor is null, "Archive lists child folders without starting physical directory watcher");
            var rootImage = (FileEntry)probe.ImageList.SelectedItem;
            await probe.ExecuteViewerCommandAsync(StellaViewer.Core.ViewerCommand.Last); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry) == probe.model.Entries[^1]
                && probe.model.Message.StartsWith("読み込み失敗"), "Archive last selects broken final entry and reports failure without skipping it");
            await probe.ExecuteViewerCommandAsync(StellaViewer.Core.ViewerCommand.First); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry) == probe.model.Entries[0] && probe.model.HasImage, "Archive first command opens initial listed image");
            var folder = probe.model.Locations.Single(i => i.Name == "日本語");
            probe.Locations.SelectedItem = folder; await probe.navigation; await probe.activeDisplay;
            Check(probe.model.HasImage && probe.model.Entries.Select(i => i.Name).SequenceEqual(new[] { "2.png", "10.png" })
                && ArchiveLocation.Parse(probe.model.Folder)?.EntryPath == "日本語", "Archive folder selection opens only direct images in natural order");
            Check(!probe.model.Details.Contains("stella-archive:") && probe.model.Details.Contains("日本語"), "Details display readable archive hierarchy without internal identity");
            probe.Move(1); await probe.activeDisplay;
            var page = (FileEntry)probe.ImageList.SelectedItem;
            Check(page.Name == "10.png" && probe.model.HasImage, "Archive page navigation decodes selected image");
            probe.SelectedImageSort = 2; await probe.FilterAsync();
            Check(((FileEntry)probe.ImageList.SelectedItem).Path == page.Path, "Archive sort keeps selected page");
            await probe.SetSpreadModeAsync(true);
            Check(probe.firstPage?.Bitmap is not null && probe.secondPage?.Bitmap is not null, "Archive spread decodes two independent entry streams");
            await probe.SetSpreadModeAsync(false);
            probe.ImageAddress.BeginEdit();
            var editor = Descendants(probe.ImageAddress).OfType<TextBox>().Single();
            Check(editor.Text == archivePath, "Archive address editor contains only physical archive path");
            var request = probe.openRequestVersion;
            probe.ImageAddress.SubmitAddress(page.Path); await probe.navigation;
            Check(probe.openRequestVersion == request, "Direct input rejects internal archive identities");
            var saved = probe.CaptureSavedState();
            var resolved = ViewerStateStore.ResolveStartup(saved.View, null, true);
            Check(resolved.Path == page.Path, "Captured state preserves archive page identity");
            await probe.UpAsync(); await probe.activeDisplay;
            Check(ArchiveLocation.Parse(probe.model.Folder)?.EntryPath == "", "Up moves to archive parent folder");
            await probe.GoBackAsync(); await probe.activeDisplay;
            Check(((FileEntry)probe.ImageList.SelectedItem).Path == page.Path, "Back restores archive folder and selected page");
            await probe.GoForwardAsync(); await probe.activeDisplay;
            Check(ArchiveLocation.Parse(probe.model.Folder)?.EntryPath == "", "Forward restores archive root");
            await probe.UpAsync(); await probe.activeDisplay;
            Check(probe.model.Folder == Path.GetDirectoryName(archivePath) && probe.model.SelectedLocationPath == archivePath,
                "Up from archive root selects outer physical archive without reopening it");
            await probe.OpenAsync(resolved.Path!); await probe.activeDisplay;
            Check(((FileEntry)probe.ImageList.SelectedItem).Path == page.Path && probe.model.HasImage, "Saved archive page reopens through normal startup target route");
            await probe.ReloadAsync(); await probe.activeDisplay;
            Check(((FileEntry)probe.ImageList.SelectedItem).Path == page.Path && probe.model.HasImage, "Archive reload rebuilds catalog while preserving page");
            var missing = new ArchiveLocation(archivePath, "日本語/missing.png", false);
            await probe.OpenAsync(missing.Identity); await probe.activeDisplay;
            Check(((FileEntry)probe.ImageList.SelectedItem).Name == "2.png" && probe.model.Status.Contains("見つからない"), "Missing archive page falls back to folder first with notice");
            string? copied = null, revealed = null, associated = null;
            probe.copyText = value => copied = value;
            probe.revealFile = (value, _) => { revealed = value; return Task.CompletedTask; };
            probe.openAssociated = (value, _) => { associated = value; return Task.CompletedTask; };
            var row = new ListBoxItem { DataContext = probe.ImageList.SelectedItem };
            var menu = (ContextMenu)probe.Resources["FileContextMenu"]; menu.PlacementTarget = row;
            probe.FileContextMenuOpened(menu, new());
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Check(!items[0].IsEnabled && items[1].IsEnabled && items[2].IsEnabled && !items[3].IsEnabled,
                "Archive menu disables internal path copy and associated application");
            probe.CopyFullPathClicked(items[0], new()); Check(copied is null, "Archive path copy cannot expose internal identity");
            probe.CopyFileNameClicked(items[1], new()); Check(copied == "2.png", "Archive file name copy remains available");
            await probe.RevealFileAsync(items[2]); Check(revealed == archivePath, "Explorer action selects outer archive");
            await probe.OpenAssociatedAsync(items[3]); Check(associated is null, "Internal associated application action cannot extract or launch item");
            probe.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var preview = Descendants(probe.ListArea).OfType<Thumbnail>().FirstOrDefault(t => t.FilePath == ((FileEntry)probe.ImageList.SelectedItem).Path);
            for (var i = 0; preview?.Source is null && i < 40; i++) await Task.Delay(50);
            Check(preview?.Source is not null, $"Archive image list thumbnails decode entry streams: mode={probe.ListModeBox.SelectedIndex}, matches={Descendants(probe.ListArea).OfType<Thumbnail>().Count()}, loaded={preview?.IsLoaded}, provider={preview?.ArchiveSession is not null}, error={preview?.ToolTip}");
            var content = (FrameworkElement)probe.Content;
            var rendered = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(content); SaveRender(rendered, screenshotPath + ".archive.png");
            probe.SearchBox.Text = "no-match"; await probe.FilterAsync();
            Check(probe.model.Entries.Count == 0 && !probe.model.HasImage, "Archive search with no matches clears image");
            probe.SearchBox.Text = ""; await probe.FilterAsync(); await probe.activeDisplay;
            probe.SelectedImageSort = 0; await probe.FilterAsync();
            await probe.OpenAsync(archivePath); await probe.activeDisplay;
            probe.Locations.SelectedItem = probe.model.Locations.Single(i => i.Name == "nested.zip"); await probe.navigation;
            Check(probe.model.Status.Contains("書庫を開けません") && probe.model.HasImage, "Malformed nested archive retains existing display with error notice");
            await probe.OpenAsync(new ArchiveLocation(archivePath, "broken.png", false).Identity); await probe.activeDisplay;
            Check(!probe.model.HasImage && probe.model.Message.Contains("読み込み失敗"), "Broken archive page displays decode failure");
            probe.Move(1); await probe.activeDisplay;
            Check(probe.model.HasImage, "Page navigation remains available after archive decode failure");
            var fastArchive = probe.OpenAsync(archivePath);
            var latest = probe.OpenAsync(imagePath);
            await Task.WhenAll(fastArchive, latest); await probe.activeDisplay;
            Check(probe.model.Folder == Path.GetDirectoryName(imagePath) && probe.model.HasImage && probe.archiveSession is null,
                "Latest normal folder request wins over archive navigation");
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
