using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyDataToolsAsync(string imagePath, string report)
    {
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
        var root = Path.Combine(Path.GetDirectoryName(imagePath)!, "data-tools-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var images = Path.Combine(root, "images"); Directory.CreateDirectory(images);
        for (var i = 1; i <= 4; i++) File.Copy(imagePath, Path.Combine(images, i + ".png"));
        for (var i = 1; i <= 3; i++) Directory.CreateDirectory(Path.Combine(root, "folder" + i));
        var probe = new MainWindow(false, root) { ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000, WatchFoldersEnabled = false };
        try {
            probe.Show(); await probe.OpenAsync(images); await probe.activeDisplay; await probe.prefetchWork;
            probe.SelectPageAt(0); await probe.activeDisplay;
            probe.SelectTileBatch(probe.ImageList, probe.model.Entries[2], ModifierKeys.Control);
            Check(probe.ImageList.SelectedItems.Count == 2 && probe.model.SelectedImagePaths.Count == 2, "Ctrl tile selection and highlights");
            probe.SelectTileBatch(probe.ImageList, probe.model.Entries[3], ModifierKeys.Shift);
            Check(probe.ImageList.SelectedItems.Count == 2 && probe.ImageList.SelectedItems.Contains(probe.model.Entries[2]), "Shift tile range uses anchor");
            probe.ListModeBox.SelectedIndex = 2;
            var paths = probe.ImageList.SelectedItems.OfType<FileEntry>().Select(item => item.Path).ToArray();
            Check(paths.Length == 2, "Mode switch keeps multiple selection");
            await probe.FilterAsync(); await probe.activeDisplay;
            Check(probe.ImageList.SelectedItems.Count == 2, "Sorting and filtering retain surviving selection");
            var target = probe.ImageList.SelectedItems.OfType<FileEntry>().First();
            var context = new ContextMenu { PlacementTarget = new Button { DataContext = target } };
            foreach (var title in new[] { "フルパスをコピー", "ファイル名をコピー", "エクスプローラーで表示", "関連付けで開く", "送る（コピー）…", "ゴミ箱へ移動…", "お気に入りに登録" }) context.Items.Add(new MenuItem { Header = title });
            probe.FileContextMenuOpened(context, new());
            Check(context.Tag is object[] { Length: 2 }, "Context menu captures selected group");
            var send = probe.CreateSendFilesWindow(target.Path, paths);
            Check(((ListBox)send.FindName("Sources")).SelectedItems.Count == 2, "Copy confirmation receives selected group"); send.Close();
            var deleting = probe.CreateDeleteWindow(target.Path, paths);
            Check(((ListBox)deleting.FindName("DeleteSources")).SelectedItems.Count == 2 && ((CheckBox)deleting.FindName("DeleteAcknowledge")).IsChecked != true, "Delete confirmation receives group and still requires acknowledgement"); deleting.Close();
            await probe.SetSelectedFavoritesAsync(context.Items.OfType<MenuItem>().Last());
            Check((await probe.favoritesStore.LoadAsync()).Length == 2, "Batch favorite registration");
            probe.Move(1); await probe.activeDisplay;
            Check(probe.ImageList.SelectedItems.Count == 1 && probe.model.HasImage, "Page navigation leaves batch selection");
            await probe.LoadLocationsAsync(root, default);
            probe.SelectTileBatch(probe.Locations, probe.model.Locations[0], ModifierKeys.Control);
            probe.SelectTileBatch(probe.Locations, probe.model.Locations[2], ModifierKeys.Control);
            Check(probe.Locations.SelectedItems.Count == 2, "Location tile multiple selection");
            await probe.SortLocationsAsync(); Check(probe.Locations.SelectedItems.Count == 2, "Location sorting preserves multiple selection");

            var oldImage = probe.model.Image;
            await probe.imageCache.ClearAsync(true, true);
            Check(probe.model.HasImage && ReferenceEquals(oldImage, probe.model.Image), "Cache clear retains displayed bitmap");
            await probe.RememberRecentPlaceAsync(root); await probe.RememberRecentPlaceAsync(images);
            await probe.RemoveRecentHistoryAsync(root); Check((await probe.recentLocationStore.LoadAsync()).All(item => item.Location != root), "Individual history deletion");
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { RecordRecentLocations = false, RecordViewingState = false });
            await probe.ClearRecentHistoryAsync(); await probe.RememberRecentPlaceAsync(root);
            Check((await probe.recentLocationStore.LoadAsync()).Length == 0, "Stopped history stays empty after opening another location");

            var importedPath = Path.Combine(root, "export.json");
            var imported = probe.CurrentPreferences with { Dark = true, ControlWheel = false, SortMode = 2, LocationSortMode = 1 };
            await SettingsTransfer.ExportAsync(importedPath, imported);
            var dialog = new SettingsWindow(probe.CurrentPreferences, next => probe.ApplyPreferencesAsync(next)) { Owner = probe, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
            probe.CopySettingsTheme(dialog);
            dialog.ConfigureDataTools(probe.imageCache.GetUsageAsync, probe.imageCache.ClearAsync, probe.ClearRecentHistoryAsync, probe.ClearSavedViewingStateAsync);
            try {
                dialog.Show(); await dialog.ImportSettingsAsync(importedPath);
                Check(!probe.CurrentPreferences.Dark && dialog.Values.Dark, "Import stages settings until Apply");
                Check(await dialog.ApplyChangesAsync() && probe.CurrentPreferences.Dark && !probe.controlWheel && probe.CurrentPreferences.LocationSortMode == 1, "Apply imported settings and sort modes");
                await dialog.RefreshCacheUsageAsync(); Check(((TextBlock)dialog.FindName("CacheUsageText")).Text.Contains("メモリ"), "Cache management usage in settings");
                probe.CopySettingsTheme(dialog);
                ((TabControl)dialog.FindName("SettingsTabs")).SelectedIndex = 8; dialog.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog); SaveRender(bitmap, report + ".settings-data.png");
            }
            finally { dialog.Close(); }
            var cancel = new SettingsWindow(probe.CurrentPreferences, next => probe.ApplyPreferencesAsync(next));
            await SettingsTransfer.ExportAsync(importedPath, imported with { Dark = false }); await cancel.ImportSettingsAsync(importedPath); cancel.Close();
            Check(probe.CurrentPreferences.Dark, "Closing imported settings without Apply leaves preferences unchanged");
            await ViewerStateStore.SaveAsync(new(), new(images), root);
            await probe.ClearSavedViewingStateAsync();
            Check(!probe.recordViewingState && ViewerStateStore.Load(root).View.Folder == "", "UI data action stops and clears saved viewing position");
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
