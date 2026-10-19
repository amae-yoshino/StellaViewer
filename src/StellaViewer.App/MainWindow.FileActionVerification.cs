using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyFileActionsAsync(string imagePath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var previousCopy = copyText; var previousReveal = revealFile; var previousAssociated = openAssociated;
        string? copied = null, revealed = null; bool? allowed = null;
        copyText = text => copied = text;
        revealFile = (path, network) => { revealed = path; allowed = network; return Task.CompletedTask; };
        string? associated = null;
        openAssociated = (path, _) => { associated = path; return Task.CompletedTask; };
        try {
            await OpenAsync(imagePath); await activeDisplay; UpdateLayout();
            var image = model.Image; var selection = ImageList.SelectedItem; var scale = zoom;
            var entries = new object[] {
                new FileEntry(Path.Combine(Path.GetDirectoryName(imagePath)!, "画像 space, 日本語.png"), "画像 space, 日本語.png", 0, default),
                new LocationEntry(Path.Combine(Path.GetDirectoryName(imagePath)!, "フォルダ space"), "フォルダ space", true),
                new LocationEntry(Path.Combine(Path.GetDirectoryName(imagePath)!, "書庫.zip"), "書庫.zip", false),
                new LocationEntry(Path.Combine(Path.GetDirectoryName(imagePath)!, "文書.pdf"), "文書.pdf", false)
            };
            foreach (var entry in entries) {
                var row = new ListBoxItem { DataContext = entry, Style = (Style)Resources["FileItemStyle"] };
                Check(row.ContextMenu is not null, "Names and details expose file context menu through item style");
                var menu = (ContextMenu)Resources["FileContextMenu"]; menu.PlacementTarget = row;
                FileContextMenuOpened(menu, new());
                var items = menu.Items.OfType<MenuItem>().ToArray();
                var path = EntryPath(entry)!;
                Check(items.Length == 7 && items.Take(4).All(item => item.IsEnabled && (string?)item.Tag == path),
                    "Context menu targets clicked image, folder, archive or PDF independently of selected image");
                CopyFullPathClicked(items[0], new()); Check(copied == Path.GetFullPath(path), "Full path copy preserves spaces, commas and Unicode");
                CopyFileNameClicked(items[1], new()); Check(copied == Path.GetFileName(path), "Name copy uses clicked entry name");
                await RevealFileAsync(items[2]); Check(revealed == Path.GetFullPath(path) && allowed == model.AllowNetwork,
                    "Explorer action receives exact physical path and current network permission");
                await OpenAssociatedAsync(items[3]); Check(associated == Path.GetFullPath(path), "Associated application action receives clicked physical path");
                row.DataContext = entries[0];
                Check(items.All(item => (string?)item.Tag == path), "Open menu keeps target snapshot when recycled row changes");
                var rightClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent };
                row.RaiseEvent(rightClick); Check(rightClick.Handled, "Right click does not select and navigate a location row");
            }
            Check(ReferenceEquals(image, model.Image) && ReferenceEquals(selection, ImageList.SelectedItem) && zoom == scale,
                "File actions preserve displayed image, selection and zoom");
            CopyFullPathClicked(new MenuItem(), new()); Check(copied == Path.GetFullPath(imagePath), "File menu copies displayed image path");
            copyText = _ => throw new IOException("clipboard busy");
            CopyFullPathClicked(new MenuItem(), new()); Check(model.Status.Contains("clipboard busy"), "Clipboard failure is reported without changing image");
            revealed = null;
            await RevealFileAsync(new MenuItem { Tag = @"\\stella-test.invalid\share\image.png" });
            Check(revealed is null && model.Status.Contains("ネットワーク"), "Explorer action respects disabled network permission before launching");
            associated = null;
            await OpenAssociatedAsync(new MenuItem { Tag = @"\\stella-test.invalid\share\image.png" });
            Check(associated is null && model.Status.Contains("ネットワーク"), "Associated application respects network permission before launch");
            openAssociated = (_, _) => throw new IOException("association unavailable");
            await OpenAssociatedAsync(new MenuItem { Tag = imagePath });
            Check(model.Status.Contains("association unavailable"), "Associated application failure is reported");
            revealFile = (_, _) => throw new IOException("shell unavailable");
            await RevealFileAsync(new MenuItem { Tag = imagePath }); Check(model.Status.Contains("shell unavailable"), "Explorer failure is reported");
            try {
                await ExplorerService.RevealAsync(Path.Combine(Path.GetDirectoryName(imagePath)!, Guid.NewGuid() + ".missing"));
                throw new InvalidOperationException("Missing target was accepted");
            }
            catch (FileNotFoundException) { }
            try {
                await ExplorerService.OpenAssociatedAsync(Path.Combine(Path.GetDirectoryName(imagePath)!, Guid.NewGuid() + ".missing"));
                throw new InvalidOperationException("Missing associated target was accepted");
            }
            catch (FileNotFoundException) { }
            foreach (var name in new[] { "TileTemplate", "LocationTileTemplate" }) {
                var content = new ContentControl { Content = entries[name == "TileTemplate" ? 0 : 1], ContentTemplate = (DataTemplate)Resources[name] };
                WindowSurface.Children.Add(content); UpdateLayout();
                try { Check(Descendants(content).OfType<Button>().Any(button => button.ContextMenu is not null), "Tile exposes file context menu"); }
                finally { WindowSurface.Children.Remove(content); }
            }
        }
        finally { copyText = previousCopy; revealFile = previousReveal; openAssociated = previousAssociated; }
    }
}
