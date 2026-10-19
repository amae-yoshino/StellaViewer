using System.Windows;
using System.Windows.Controls;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private int locationTileColumns = 1;
    private int locationSortVersion;
    private async Task SortLocationsAsync()
    {
        if (loadingLocations) return;
        var version = ++locationSortVersion;
        var token = locationListing.Token;
        var entries = model.Locations.ToArray();
        var mode = SelectedLocationSort;
        var sorted = await Task.Run(() => FolderBrowser.SortLocations(entries, mode));
        if (token.IsCancellationRequested || closed || version != locationSortVersion) return;
        var selected = Locations.SelectedItem;
        var selectedPaths = Locations.SelectedItems.OfType<LocationEntry>().Select(item => item.Path).ToArray();
        sortingLocations = true;
        try {
            if(BrowsePath.IsVirtual(model.LocationFolder)){model.Locations.Clear();foreach(var entry in sorted)model.Locations.Add(entry);}
            else model.Locations.Reconcile(sorted,entry=>entry.Path);
            Locations.SelectedItem = selected;
            if (selectedPaths.Length > 1) RestoreBatchSelection(Locations, selectedPaths);
            SyncLocationTiles(true);
            ScrollLocationSelection();
        }
        finally { sortingLocations = false; }
    }
    private async void LocationSortingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || loadingPreferences) return;
        var mode = SelectedLocationSort;
        var saving = usePreferences ? SaveListPreferencesAsync(locationSort: mode) : Task.CompletedTask;
        try { await SortLocationsAsync(); await saving; }
        catch (Exception error) { model.Status = "フォルダ・書庫の並べ替えに失敗しました：" + error.Message; }
    }
    private void SyncLocationTiles(bool reset)
    {
        if (LocationModeBox.SelectedIndex != 2) { model.LocationTileRows.Clear(); return; }
        var columns = TileColumnCount(LocationTileList, LocationArea, model.LocationThumbnails.TileWidth);
        if (reset || columns != locationTileColumns) model.LocationTileRows.Clear();
        locationTileColumns = columns;
        var start = model.LocationTileRows.Count * columns;
        if (model.LocationTileRows.Count > 0 && model.LocationTileRows[^1].Length < columns) {
            start = (model.LocationTileRows.Count - 1) * columns;
            model.LocationTileRows.RemoveAt(model.LocationTileRows.Count - 1);
        }
        for (var index = start; index < model.Locations.Count; index += columns) {
            var row = new LocationEntry[Math.Min(columns, model.Locations.Count - index)];
            for (var offset = 0; offset < row.Length; offset++) row[offset] = model.Locations[index + offset];
            model.LocationTileRows.Add(row);
        }
    }
    private void ApplyLocationMode()
    {
        var tiles = LocationModeBox.SelectedIndex == 2;
        Locations.Visibility = tiles ? Visibility.Collapsed : Visibility.Visible;
        LocationTileList.Visibility = tiles ? Visibility.Visible : Visibility.Collapsed;
        Locations.ItemTemplate = (DataTemplate)Resources[LocationModeBox.SelectedIndex == 0 ? "LocationNameTemplate" : "LocationDetailTemplate"];
        SyncLocationTiles(true);
        ScrollLocationSelection();
        QueueTileLayout();
    }
    private void ScrollLocationSelection()
    {
        if (restoringLocationScroll) return;
        if (Locations.SelectedIndex < 0) return;
        if (LocationModeBox.SelectedIndex == 2) {
            var row = Locations.SelectedIndex / locationTileColumns;
            if (row < model.LocationTileRows.Count) LocationTileList.ScrollIntoView(model.LocationTileRows[row]);
        }
        else Locations.ScrollIntoView(Locations.SelectedItem);
    }
    private async void LocationModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || loadingPreferences) return;
        ApplyLocationMode();
        if (!usePreferences) return;
        var mode = LocationModeBox.SelectedIndex;
        try { await SaveListPreferencesAsync(locationMode: mode); }
        catch (Exception error) { model.Status = "フォルダ・書庫の表示方法を保存できませんでした：" + error.Message; }
    }
    private void LocationAreaChanged(object sender, SizeChangedEventArgs e)
    {
        model.UpdateLocationThumbnailWidth(e.NewSize.Width);
        if (!IsInitialized || LocationModeBox.SelectedIndex != 2) return;
        QueueTileLayout();
    }
    private void LocationTileClicked(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: LocationEntry entry }) {
        if (SelectTileBatch(Locations, entry, System.Windows.Input.Keyboard.Modifiers)) return;
        if (Locations.SelectedItems.Count > 1) { selectingBatch = true; try { Locations.UnselectAll(); } finally { selectingBatch = false; } }
        if(ReferenceEquals(Locations.SelectedItem,entry))navigation=OpenAsync(entry.Path,updateLocations:false);
        else Locations.SelectedItem = entry;
    } }
    private void LocationModeMenuClick(object sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: string value } && int.TryParse(value, out var mode)) LocationModeBox.SelectedIndex = mode; }
}
