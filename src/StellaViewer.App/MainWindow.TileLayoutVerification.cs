using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyTileLayoutAsync()
    {
        var probe = new MainWindow(false) { ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000, Width = 1500, WatchFoldersEnabled = false };
        async Task Settle() { probe.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); probe.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        try {
            probe.Show();
            for (var i = 0; i < 101; i++) { probe.model.Entries.Add(new FileEntry($"missing-{i}.png", $"Image {i}", 0, DateTime.MinValue)); probe.model.Locations.Add(new LocationEntry($"missing-folder-{i}", $"Folder {i}", true)); }
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { ListMode = 2, LocationListMode = 2 });
            foreach (var scale in new[] { 100, 125 }) {
                await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { UiSize = new(scale, 14) });
                foreach (var treeWeight in new[] { 1d, 2 }) {
                probe.FolderColumns.ColumnDefinitions[0].Width = new GridLength(treeWeight, GridUnitType.Star);
                foreach (var width in new[] { 248d, 352, 360, 464, 472, 584, 592, 360, 248 }) {
                    probe.MainGrid.ColumnDefinitions[0].Width = new GridLength(width); await Settle();
                    foreach (var list in new[] { probe.TileList, probe.LocationTileList }) {
                        var viewport = Descendants(list).OfType<ScrollContentPresenter>().First();
                        var tileWidth = ReferenceEquals(list, probe.TileList) ? probe.model.ImageThumbnails.TileWidth : probe.model.LocationThumbnails.TileWidth;
                        var columns = ReferenceEquals(list, probe.TileList) ? probe.tileColumns : probe.locationTileColumns;
                        var expected = Math.Max(1, (int)Math.Floor((viewport.ActualWidth + .01) / tileWidth));
                        if (columns != expected) throw new InvalidOperationException($"Tile columns do not fill actual viewport: {list.Name}, scale={scale}, pane={width}, viewport={viewport.ActualWidth}, tile={tileWidth}, expected={expected}, actual={columns}");
                        var row = Descendants(list).OfType<ListBoxItem>().First();
                        var tiles = Descendants(row).OfType<Button>().ToArray();
                        if (tiles.Length > 1 && tiles[^1].TransformToAncestor(row).Transform(new Point(tiles[^1].ActualWidth, 0)).X > viewport.ActualWidth + .1)
                            throw new InvalidOperationException("Last tile clips after resizing: " + list.Name);
                    }
                    if (!probe.model.TileRows.SelectMany(row => row).SequenceEqual(probe.model.Entries) || !probe.model.LocationTileRows.SelectMany(row => row).SequenceEqual(probe.model.Locations))
                        throw new InvalidOperationException("Resizing must preserve all entries and order");
                }
                }
            }
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
