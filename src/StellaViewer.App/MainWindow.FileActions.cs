using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private Action<string> copyText = Clipboard.SetText;
    private Func<string, bool, Task> revealFile = ExplorerService.RevealAsync;
    private Func<string, bool, Task> openAssociated = ExplorerService.OpenAssociatedAsync;
    private static string? EntryPath(object? entry) => entry switch {
        FileEntry image => image.Path, LocationEntry location => location.Path, FavoriteItem favorite=>favorite.Path, _ => null
    };
    private string? ActionPath(object sender) => sender is MenuItem { Tag: string path } ? path
        : EntryPath(ImageList.SelectedItem) ?? (model.Folder.Length > 0 ? model.Folder : null);
    private void PreserveSelectionOnRightClick(object sender, MouseButtonEventArgs e)
    {
        // Opening a location item's menu must not navigate into that folder.
        if (sender is FrameworkElement { DataContext: FileEntry or LocationEntry or FavoriteItem } element) {
            var list = element.DataContext is LocationEntry ? Locations : element.DataContext is FileEntry ? ImageList : null;
            if (list is not null && !list.SelectedItems.Contains(element.DataContext)) {
                selectingBatch = true; try { list.SelectedItem = element.DataContext; } finally { selectingBatch = false; PublishSelection(); }
            }
            e.Handled = true;
        }
    }
    private void FileContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var path = EntryPath((menu.PlacementTarget as FrameworkElement)?.DataContext);
        var chosen = OperationSelection(path);
        var clicked = (menu.PlacementTarget as FrameworkElement)?.DataContext;
        menu.Tag = chosen.Length == 1 && clicked is not null ? new object[] { clicked } : chosen;
        foreach (var item in menu.Items.OfType<MenuItem>()) { item.Tag = path; item.IsEnabled = path is not null; }
        var favoriteMenu=menu.Items.OfType<MenuItem>().Last();
        menu.Items.OfType<MenuItem>().First(item=>item.Header.ToString()=="送る（コピー）…").IsEnabled=((object[])menu.Tag).All(CanSend);
        menu.Items.OfType<MenuItem>().First(item=>item.Header.ToString()=="ゴミ箱へ移動…").IsEnabled=deleteEnabled&&!fileManagementBusy&&!loading&&!loadingLocations&&((object[])menu.Tag).All(IsPhysicalDeletable);
        var kind=FavoriteKindFor((menu.PlacementTarget as FrameworkElement)?.DataContext);
        favoriteMenu.CommandParameter=kind;favoriteMenu.IsEnabled=path is not null&&((object[])menu.Tag).All(entry => FavoriteKindFor(entry) is not null && EntryPath(entry) is string target && !BrowsePath.IsVirtual(target));
        favoriteMenu.Header=path is not null&&((object[])menu.Tag).All(entry => favoriteItems.Any(item=>StringComparer.OrdinalIgnoreCase.Equals(item.Path,EntryPath(entry))))?"お気に入りから削除":"お気に入りに登録";
        if (path is not null && ArchiveLocation.IsIdentity(path)) {
            var items = menu.Items.OfType<MenuItem>().ToArray();
            items[0].IsEnabled = false; items[3].IsEnabled = false;
        }
        if (path is not null && PdfLocation.IsIdentity(path)) {
            var items = menu.Items.OfType<MenuItem>().ToArray();
            items[0].IsEnabled = items[1].IsEnabled = items[3].IsEnabled = false;
        }
        foreach (var key in new[] { "PanelBackground", "PanelForeground", "DividerBrush", "HoverBrush", "SelectionBrush" })
            menu.Resources[key] = Resources[key];
        menu.Resources[typeof(MenuItem)] = Resources[typeof(MenuItem)];
        menu.Resources[MenuItem.SeparatorStyleKey] = Resources[MenuItem.SeparatorStyleKey];
        UiSizing.ConfigurePopup(menu, this);
        menu.ApplyTemplate(); ApplyMenuPopupTheme(menu);
    }
    private void CopyFullPathClicked(object sender, RoutedEventArgs e) => CopyFileText(sender, false);
    private void CopyFileNameClicked(object sender, RoutedEventArgs e) => CopyFileText(sender, true);
    private void CopyFileText(object sender, bool nameOnly)
    {
        if (ActionPath(sender) is not string path) { model.Status = "コピーする項目がありません。"; return; }
        try {
            if (PdfLocation.IsIdentity(path)) { model.Status = "PDFページのコピーは未対応です。"; return; }
            if (ArchiveLocation.Parse(path) is { } location) {
                if (!nameOnly) { model.Status = "書庫内項目のパスコピーは対象外です。"; return; }
                copyText(location.Name); model.Status = "ファイル名をコピーしました。"; return;
            }
            var full = Path.GetFullPath(path);
            var text = nameOnly ? Path.GetFileName(Path.TrimEndingDirectorySeparator(full)) : full;
            if (text.Length == 0) text = full;
            copyText(text); model.Status = nameOnly ? "ファイル名をコピーしました。" : "フルパスをコピーしました。";
        }
        catch (Exception error) { model.Status = "コピーできませんでした：" + error.Message; }
    }
    private async void RevealFileClicked(object sender, RoutedEventArgs e) => await RevealFileAsync(sender);
    private async void OpenAssociatedClicked(object sender, RoutedEventArgs e) => await OpenAssociatedAsync(sender);
    private async Task OpenAssociatedAsync(object sender)
    {
        if (ActionPath(sender) is not string path) { model.Status = "開く項目がありません。"; return; }
        try {
            if (BrowsePath.IsVirtual(path)) { model.Status = "書庫内項目・PDFページの関連付け起動は対象外です。"; return; }
            NetworkAccess.Validate(path, model.AllowNetwork);
            await openAssociated(Path.GetFullPath(path), model.AllowNetwork);
        }
        catch (Exception error) { model.Status = "関連付けで開けませんでした：" + error.Message; }
    }
    private async Task RevealFileAsync(object sender)
    {
        if (ActionPath(sender) is not string path) { model.Status = "表示する項目がありません。"; return; }
        try {
            path = BrowsePath.PhysicalPath(path);
            NetworkAccess.Validate(path, model.AllowNetwork);
            await revealFile(Path.GetFullPath(path), model.AllowNetwork);
        }
        catch (Exception error) { model.Status = "エクスプローラーで表示できませんでした：" + error.Message; }
    }
}
