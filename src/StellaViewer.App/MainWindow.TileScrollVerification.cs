using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyTileScrollingAsync()
    {
        var probe = new MainWindow(false) { ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000, WatchFoldersEnabled = false };
        async Task Settle() { probe.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); probe.UpdateLayout(); }
        static double VisiblePosition(ListBox list)
        {
            // Virtualized extent estimates can change when resized rows are first realized.
            // Compare actual row positions instead of the estimated scrollbar offset.
            var row = Descendants(list).OfType<ListBoxItem>()
                .Select(item => (Item: item, Y: item.TransformToAncestor(list).Transform(new Point()).Y))
                .Where(item => item.Y + item.Item.ActualHeight > 0 && item.Y < list.ActualHeight)
                .OrderBy(item => item.Y).First();
            return list.ItemContainerGenerator.IndexFromContainer(row.Item) - row.Y / row.Item.ActualHeight;
        }
        try {
            probe.Show();
            for (var index = 0; index < 301; index++) {
                probe.model.Entries.Add(new FileEntry($"missing-{index}.png", $"Image {index}", 0, DateTime.MinValue));
                probe.model.Locations.Add(new LocationEntry($"missing-folder-{index}", $"Folder {index}", true));
            }
            probe.ListModeBox.SelectedIndex = 2; probe.ApplyListMode();
            probe.LocationModeBox.SelectedIndex = 2; probe.ApplyLocationMode();
            foreach (var size in new[] { 0, 1, 2, 3 }) {
                probe.model.ImageThumbnailSize = size; probe.model.LocationThumbnailSize = size;
                probe.SyncTiles(true); probe.SyncLocationTiles(true);
                foreach (var height in new[] { 40d, 80, 140, 240 }) {
                    foreach (var list in new[] { probe.TileList, probe.LocationTileList }) {
                        list.Height = height; await Settle();
                        var scroll = Descendants(list).OfType<ScrollViewer>().First();
                        scroll.ScrollToBottom(); await Settle();
                        Descendants(list).OfType<Button>().Last().Focus(); await Settle();
                        for (var turn = 0; turn < 6; turn++) {
                            var before = VisiblePosition(list);
                            var tile = Descendants(list).OfType<Button>().First(button => {
                                var y = button.TransformToAncestor(list).Transform(new Point()).Y;
                                return y + button.ActualHeight > 0 && y < list.ActualHeight;
                            });
                            if (turn % 2 == 0) tile.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                            else scroll.LineUp();
                            await Settle(); await Task.Delay(30); await Settle();
                            var after = VisiblePosition(list);
                            if (after >= before - .001)
                                throw new InvalidOperationException($"Tile wheel cannot move up: list={list.Name}, size={size}, height={height}, before={before}, after={after}, viewport={scroll.ViewportHeight}, extent={scroll.ExtentHeight}");
                        }
                        if (Descendants(list).OfType<ListBoxItem>().Count() >= 50)
                            throw new InvalidOperationException("Pixel scrolling must retain tile virtualization: " + list.Name);
                    }
                }
            }
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
