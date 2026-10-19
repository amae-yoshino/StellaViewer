using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private sealed record LocationScrollPosition(int Mode, int Sort, double Offset, string? AnchorPath = null, double AnchorTop = 0);
    private readonly Dictionary<(string Folder, int Mode, int Sort), LocationScrollPosition> locationScrollPositions = new();
    private long locationScrollVersion;
    private long locationInteractionVersion;
    private bool restoringLocationScroll;
    private static string LocationScrollKey(string folder) => BrowsePath.IsVirtual(folder) ? folder
        : Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)).ToUpperInvariant();
    private ListBox ActiveLocationList => LocationModeBox.SelectedIndex == 2 ? LocationTileList : Locations;
    private LocationScrollPosition? CaptureLocationScroll()
    {
        if (model.LocationFolder.Length == 0 || loadingLocations) return null;
        if (FolderBrowserPanel.IsVisible && FindScrollViewer(ActiveLocationList) is { } scroll) {
            var viewport = Descendants(scroll).OfType<ScrollContentPresenter>().FirstOrDefault();
            if (viewport is not null) {
                var first = Descendants(ActiveLocationList).OfType<ListBoxItem>()
                    .Where(row => ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(row), ActiveLocationList))
                    .Select(row => (Row: row, Top: row.TransformToAncestor(viewport).Transform(new Point()).Y))
                    .Where(item => item.Top + item.Row.ActualHeight > .1 && item.Top < viewport.ActualHeight)
                    .OrderBy(item => item.Top).FirstOrDefault();
                var path = first.Row?.DataContext switch { LocationEntry entry => entry.Path, LocationEntry[] entries => entries.FirstOrDefault()?.Path, _ => null };
                return new(LocationModeBox.SelectedIndex, SelectedLocationSort, scroll.VerticalOffset, path, first.Top);
            }
            return new(LocationModeBox.SelectedIndex, SelectedLocationSort, scroll.VerticalOffset);
        }
        return SavedLocationScroll(model.LocationFolder);
    }
    private LocationScrollPosition? SavedLocationScroll(string folder) => locationScrollPositions.GetValueOrDefault(
        (LocationScrollKey(folder), LocationModeBox.SelectedIndex, SelectedLocationSort));
    private void RememberLocationScroll()
    {
        if (CaptureLocationScroll() is not { } position) return;
        var key = (LocationScrollKey(model.LocationFolder), position.Mode, position.Sort);
        locationScrollPositions.Remove(key);
        if (locationScrollPositions.Count >= 256) locationScrollPositions.Remove(locationScrollPositions.Keys.First());
        locationScrollPositions.Add(key, position);
    }
    private void LocationScrollUserInput(object sender, RoutedEventArgs e) { ++locationInteractionVersion; restoringLocationScroll = false; }
    private async Task RestoreLocationScrollAsync(string folder, LocationScrollPosition? position, long version, CancellationToken token)
    {
        try {
            await Dispatcher.InvokeAsync(() => {
                if (closed || closingInProgress || token.IsCancellationRequested || version != locationScrollVersion || !restoringLocationScroll
                    || model.LocationFolder != folder || position is null || position.Mode != LocationModeBox.SelectedIndex || position.Sort != SelectedLocationSort) return;
                ActiveLocationList.UpdateLayout();
                // ScrollViewer clamps the offset if entries were removed or the viewport grew.
                var scroll = FindScrollViewer(ActiveLocationList);
                scroll?.ScrollToVerticalOffset(position.Offset);
                ActiveLocationList.UpdateLayout();
                if (scroll is not null && position.AnchorPath is string path) {
                    var comparison = BrowsePath.IsVirtual(path) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    var index = -1;
                    for (var i = 0; i < model.Locations.Count; i++) if (string.Equals(model.Locations[i].Path, path, comparison)) { index = i; break; }
                    if (index < 0) return;
                    var rowIndex = position.Mode == 2 ? index / locationTileColumns : index;
                    ActiveLocationList.ScrollIntoView(ActiveLocationList.Items[rowIndex]);
                    ActiveLocationList.UpdateLayout();
                    // Virtualized rows refine their height estimates during realization.
                    // Restore the visible item and its inset, not just an estimated offset.
                    for (var pass = 0; pass < 3; pass++) {
                        if (ActiveLocationList.ItemContainerGenerator.ContainerFromIndex(rowIndex) is not ListBoxItem row
                            || Descendants(scroll).OfType<ScrollContentPresenter>().FirstOrDefault() is not { } viewport) break;
                        var top = row.TransformToAncestor(viewport).Transform(new Point()).Y;
                        if (Math.Abs(top - position.AnchorTop) < .1) break;
                        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + top - position.AnchorTop);
                        ActiveLocationList.UpdateLayout();
                    }
                }
            }, DispatcherPriority.Loaded);
        }
        catch (TaskCanceledException) { }
        finally { if (version == locationScrollVersion) restoringLocationScroll = false; }
    }
}
