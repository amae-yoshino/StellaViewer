using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyWatchingAsync(string imagePath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        async Task Until(Func<bool> condition, string name) {
            for (var i = 0; i < 160; i++) {
                if (condition()) return;
                await Task.Delay(50);
            }
            throw new InvalidOperationException("Watch timeout: " + name + "; " + model.Status);
        }
        var saved = CurrentPreferences;
        var root = Path.GetDirectoryName(imagePath)!;
        var folder = Path.Combine(root, "watch-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var first = Path.Combine(folder, "page1.png"); var second = Path.Combine(folder, "page2.png");
        File.Copy(imagePath, first); File.Copy(Path.Combine(root, "spread-4", "page2.png"), second);
        var child = Path.Combine(folder, "child"); Directory.CreateDirectory(child);
        TreeViewItem? tree = null;
        try {
            SearchBox.Text = ""; SelectedImageSort = 0; ListModeBox.SelectedIndex = 1;
            await ApplyPreferencesAsync(CurrentPreferences with { SpreadMode = false, HideHiddenFolders = true });
            WatchFoldersEnabled = true;
            await OpenAsync(first); await activeDisplay; UpdateLayout();
            tree = FolderNode(folder); tree.Items.Clear(); tree.Items.Add(FolderNode(child)); Folders.Items.Add(tree);
            var originalCollection = model.Entries;
            var resets = 0;
            originalCollection.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) resets++; };
            var image = model.Image; var originalLength = displayedEntry!.Length;
            SetZoom(450); Rotation.Angle = 90;
            var originalZoom = zoom; var originalRotation = Rotation.Angle;
            await Until(() => Descendants(ListArea).OfType<Thumbnail>().Any(t => t.FilePath == first && t.Source is not null), "initial thumbnail");
            var oldThumbnail = Descendants(ListArea).OfType<Thumbnail>().First(t => t.FilePath == first).Source;
            File.Copy(Path.Combine(root, "grid.png"), first, true);
            await Until(() => changedDisplayedPaths.Contains(first) && model.Entries.First(e => e.Path == first).Length != originalLength, "updated image metadata");
            Check(ReferenceEquals(image, model.Image) && zoom == originalZoom && Rotation.Angle == originalRotation
                && decoded!.Width == 8 && model.Status.Contains("元ファイルが更新されています"), "External update keeps displayed pixels and transform");
            await Until(() => Descendants(ListArea).OfType<Thumbnail>().Any(t => t.FilePath == first && t.Source is not null && !ReferenceEquals(t.Source, oldThumbnail)), "updated visible thumbnail");
            Check(ReferenceEquals(originalCollection, model.Entries) && resets == 0, "Small watch changes update collection incrementally");
            await ReloadAsync(); await activeDisplay;
            Check(decoded!.Width == 400 && !model.Status.Contains("元ファイルが更新されています"), "F5 reads new image bytes and clears update notice");
            image = model.Image;
            ListModeBox.SelectedIndex = 2; UpdateLayout(); var tileResets = 0;
            model.TileRows.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) tileResets++; };
            File.Copy(imagePath, first, true);
            await Until(() => changedDisplayedPaths.Contains(first) && model.Entries.First(e => e.Path == first).Length == originalLength, "tile metadata update");
            Check(tileResets == 0 && ReferenceEquals(image, model.Image), "Small tile update retains rows and displayed image without resetting collection");
            ListModeBox.SelectedIndex = 1; UpdateLayout();
            var inserted = Path.Combine(folder, "page0.png"); File.Copy(imagePath, inserted);
            await Until(() => model.Entries.Any(e => e.Path == inserted), "new image");
            Check(ImageList.SelectedItem is FileEntry { Path: var selectedPath } && selectedPath == first && ReferenceEquals(image, model.Image)
                && model.Entries[0].Path == inserted, "External insertion keeps selected image and natural order");
            var renamed = Path.Combine(folder, "renamed.png"); File.Move(inserted, renamed);
            await Until(() => model.Entries.Any(e => e.Path == renamed) && model.Entries.All(e => e.Path != inserted), "rename image");
            var newChild = Path.Combine(folder, "new-child"); Directory.CreateDirectory(newChild);
            File.Copy(Path.Combine(root, "preview.cbz"), Path.Combine(folder, "book.cbz"));
            await Until(() => model.Locations.Any(e => e.Path == newChild) && model.Locations.Any(e => e.Name == "book.cbz"), "folder and archive insertion");
            Check(tree.Items.OfType<TreeViewItem>().Any(n => (string?)n.Tag == newChild), "Loaded folder tree reflects new child");
            var hidden = Path.Combine(folder, "hidden"); var before = watchApplyCount;
            Directory.CreateDirectory(hidden); File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
            await Until(() => watchApplyCount > before, "hidden folder event");
            Check(model.Locations.All(e => e.Path != hidden), "Hidden folder policy applies to live updates");
            Directory.Delete(newChild);
            await Until(() => model.Locations.All(e => e.Path != newChild), "folder removal");
            Check(tree.Items.OfType<TreeViewItem>().All(n => (string?)n.Tag != newChild), "Loaded folder tree removes deleted child");
            File.Delete(first);
            await Until(() => missingDisplayedPaths.Contains(first), "displayed image removal");
            Check(ReferenceEquals(image, model.Image) && model.Entries.Any(e => e.Path == first)
                && model.Status.Contains("元ファイルがありません"), "Deleted source remains displayed with notice");
            Move(1); await activeDisplay;
            Check(model.HasImage && model.Entries.All(e => e.Path != first) && (ImageList.SelectedItem as FileEntry)?.Path == second,
                "Navigating removes missing source without skipping next image");

            var onlyFolder = Path.Combine(folder, "only"); Directory.CreateDirectory(onlyFolder);
            var only = Path.Combine(onlyFolder, "only.png"); File.Copy(imagePath, only);
            await OpenAsync(only); await activeDisplay; image = model.Image;
            File.Delete(only); await Until(() => missingDisplayedPaths.Contains(only), "last source removal");
            File.Copy(Path.Combine(root, "grid.png"), only);
            await Until(() => !missingDisplayedPaths.Contains(only) && changedDisplayedPaths.Contains(only), "source reappearance");
            Check(ReferenceEquals(image, model.Image) && !model.Status.Contains("元ファイルがありません"), "Reappearing source keeps old pixels until reload");
            await ReloadAsync(); await activeDisplay; Check(decoded!.Width == 400, "F5 reads reappearing source");
            File.Delete(only); await Until(() => missingDisplayedPaths.Contains(only), "last source deleted again");
            Move(1); await activeDisplay;
            Check(model.Entries.Count == 0 && model.Image is null, "Moving beyond only missing source clears display");

            var goneFolder = Path.Combine(folder, "gone-folder"); Directory.CreateDirectory(goneFolder);
            var gone = Path.Combine(goneFolder, "gone.png"); File.Copy(imagePath, gone);
            await OpenAsync(gone); await activeDisplay; image = model.Image;
            folderMonitor!.Dispose(); folderMonitor = null;
            File.Delete(gone); Directory.Delete(goneFolder);
            QueueWatchBatch(watchInbox!, new([], true));
            await Until(() => missingDisplayedPaths.Contains(gone) && model.Status.Contains("閲覧フォルダがありません"), "folder removal recovery");
            Check(ReferenceEquals(image, model.Image) && model.Entries.Count == 1, "Removed folder keeps current pixels and missing source until navigation");

            await OpenAsync(second); await activeDisplay;
            var secondImage = model.Image; File.Delete(second);
            await Until(() => missingDisplayedPaths.Contains(second), "F5 missing source");
            await ReloadAsync(); await activeDisplay;
            Check(model.HasImage && (ImageList.SelectedItem as FileEntry)?.Path == renamed && !ReferenceEquals(secondImage, model.Image), "F5 on missing source reopens parent and first existing image");

            var spreadFolder = Path.Combine(folder, "spread"); Directory.CreateDirectory(spreadFolder);
            for (var i = 1; i <= 3; i++) File.Copy(Path.Combine(root, "spread-4", $"page{i}.png"), Path.Combine(spreadFolder, $"page{i}.png"));
            await OpenAsync(Path.Combine(spreadFolder, "page1.png")); await activeDisplay; await SetSpreadModeAsync(true);
            var partner = Path.Combine(spreadFolder, "page2.png"); image = model.Image;
            File.Delete(partner); await Until(() => missingDisplayedPaths.Contains(partner), "spread partner removal");
            Check(ReferenceEquals(image, model.Image) && secondPage?.Entry.Path == partner && model.Entries.Any(e => e.Path == partner), "Deleted spread partner stays displayed");
            Move(1); await activeDisplay;
            Check(model.Entries.All(e => e.Path != partner) && firstPage?.Entry.Name == "page3.png", "Spread navigation removes missing partner");

            var oldInbox = watchInbox!;
            await OpenAsync(renamed); await activeDisplay;
            QueueWatchBatch(oldInbox, new([partner], true)); await Task.Delay(250);
            Check(model.Folder == folder && model.Entries.All(e => Path.GetDirectoryName(e.Path) == folder), "Old folder notifications cannot change new view");
            folderMonitor!.Dispose(); folderMonitor = null;
            var recovered = Path.Combine(folder, "recovered.png"); File.Copy(imagePath, recovered);
            var inbox = watchInbox!;
            QueueWatchBatch(inbox, new([], true, "simulated buffer overflow"));
            await Until(() => model.Entries.Any(e => e.Path == recovered), "overflow recovery rescan");
            Check(model.Entries.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == model.Entries.Count,
                "Overflow recovery reconciles without duplicate entries");
            StopFolderMonitor(); Check(watchInbox is null && folderMonitor is null, "Stopping monitoring releases watcher");
        }
        finally {
            StopFolderMonitor(); WatchFoldersEnabled = false;
            if (tree is not null) Folders.Items.Remove(tree);
            await ApplyPreferencesAsync(saved); await OpenAsync(imagePath); await activeDisplay;
        }
    }
}
