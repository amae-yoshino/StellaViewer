using System.Windows;
using System.Windows.Controls;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private int tileColumns = 1;
    private bool loadingPreferences = true;
    private void SyncTiles(bool reset)
    {
        if (ListModeBox.SelectedIndex != 2) { model.TileRows.Clear(); return; }
        var columns = TileColumnCount(TileList, ListArea, model.ImageThumbnails.TileWidth);
        if (reset || columns != tileColumns) model.TileRows.Clear();
        tileColumns = columns;
        var start = model.TileRows.Count * columns;
        if (model.TileRows.Count > 0 && model.TileRows[^1].Length < columns)
        {
            start = (model.TileRows.Count - 1) * columns;
            model.TileRows.RemoveAt(model.TileRows.Count - 1);
        }
        for (var index = start; index < model.Entries.Count; index += columns)
        {
            var row = new FileEntry[Math.Min(columns, model.Entries.Count - index)];
            for (var offset = 0; offset < row.Length; offset++) row[offset] = model.Entries[index + offset];
            model.TileRows.Add(row);
        }
    }
    private void ApplyListMode()
    {
        var tiles = ListModeBox.SelectedIndex == 2;
        ImageList.Visibility = tiles ? Visibility.Collapsed : Visibility.Visible;
        TileList.Visibility = tiles ? Visibility.Visible : Visibility.Collapsed;
        ImageList.ItemTemplate = (DataTemplate)Resources[ListModeBox.SelectedIndex == 0 ? "NameTemplate" : "DetailTemplate"];
        SyncTiles(true);
        ScrollSelection();
        QueueTileLayout();
    }
    private async void ListModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || loadingPreferences) return;
        ApplyListMode();
        if (!usePreferences) return;
        var mode = ListModeBox.SelectedIndex;
        try { await SaveListPreferencesAsync(imageMode: mode); }
        catch (Exception error) { model.Status = "表示方法を保存できませんでした：" + error.Message; }
    }
    private void ListAreaChanged(object sender, SizeChangedEventArgs e)
    {
        model.UpdateImageThumbnailWidth(e.NewSize.Width);
        if (!IsInitialized || ListModeBox.SelectedIndex != 2) return;
        QueueTileLayout();
    }
    private void TileClicked(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: FileEntry entry }) { if (!SelectTileBatch(ImageList, entry, System.Windows.Input.Keyboard.Modifiers)) SelectPageAt(model.Entries.IndexOf(entry)); } }
    private void ScrollSelection()
    {
        if (ImageList.SelectedIndex < 0) return;
        if (ListModeBox.SelectedIndex == 2)
        {
            var row = ImageList.SelectedIndex / tileColumns;
            if (row < model.TileRows.Count) TileList.ScrollIntoView(model.TileRows[row]);
        }
        else ImageList.ScrollIntoView(ImageList.SelectedItem);
    }
}
