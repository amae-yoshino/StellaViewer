using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace StellaViewer.App;

public partial class MainWindow
{
    private bool tileLayoutQueued;

    private static double TileViewportWidth(ListBox list, FrameworkElement area)
    {
        var width = FindScrollViewer(list)?.ViewportWidth ?? 0;
        return double.IsFinite(width) && width > 0 ? width : Math.Max(1, area.ActualWidth - 24);
    }

    private static int TileColumnCount(ListBox list, FrameworkElement area, double tileWidth) =>
        Math.Max(1, (int)Math.Floor((TileViewportWidth(list, area) + .01) / tileWidth));

    private void TileViewportChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0) QueueTileLayout();
    }

    private void QueueTileLayout()
    {
        if (tileLayoutQueued) return;
        tileLayoutQueued = true;
        // SizeChanged can run before the ScrollViewer has its final viewport.
        _ = Dispatcher.InvokeAsync(() => {
            tileLayoutQueued = false;
            if (closed || closingInProgress) return;
            if (ListModeBox.SelectedIndex == 2 && TileColumnCount(TileList, ListArea, model.ImageThumbnails.TileWidth) != tileColumns) {
                SyncTiles(true); ScrollSelection();
            }
            if (LocationModeBox.SelectedIndex == 2 && TileColumnCount(LocationTileList, LocationArea, model.LocationThumbnails.TileWidth) != locationTileColumns) {
                SyncLocationTiles(true); ScrollLocationSelection();
            }
        }, DispatcherPriority.Loaded);
    }
}
