using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private Task registrationDrop = Task.CompletedTask;
    private string? listDragPath;
    private Point listDragStart;

    private static string[] DroppedPaths(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop)
        && data.GetData(DataFormats.FileDrop) is string[] paths ? paths : [];
    private void RegistrationDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedPaths(e.Data).Length > 0
            ? (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : e.AllowedEffects & DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }
    private async void RegistrationDropped(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var paths = DroppedPaths(e.Data);
        if (paths.Length == 0 || closed || closingInProgress) return;
        var foldersOnly = ReferenceEquals(sender, DirectoryShortcutsButton) || ReferenceEquals(sender, DirectoryShortcutsPanel);
        await (registrationDrop = RegisterDroppedPathsAsync(paths, foldersOnly));
    }
    private async Task RegisterDroppedPathsAsync(string[] paths, bool foldersOnly)
    {
        var count = 0; var errors = new List<string>();
        async Task Register()
        {
            foreach (var original in paths.Distinct(StringComparer.OrdinalIgnoreCase)) {
                if (closed || closingInProgress) break;
                try {
                    if (BrowsePath.IsVirtual(original)) throw new NotSupportedException("書庫内項目・PDFページは登録対象外です。");
                    var allowed = model.AllowNetwork;
                    var found = await Task.Run(() => {
                        var target = WindowsShortcut.Resolve(original, allowed);
                        NetworkAccess.Validate(target, allowed);
                        var directory = Directory.Exists(target);
                        if (!directory && !File.Exists(target)) throw new FileNotFoundException("登録先が見つかりません。");
                        FavoriteKind? kind = directory ? FavoriteKind.Folder : FolderBrowser.IsImageFile(target) ? FavoriteKind.Image
                            : ArchiveSession.Supports(target) ? FavoriteKind.Archive : Path.GetExtension(target).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? FavoriteKind.Pdf : null;
                        if (kind is null || foldersOnly && !directory) throw new NotSupportedException(foldersOnly ? "フォルダだけ登録できます。" : "対応する画像・書庫・PDF・フォルダだけ登録できます。");
                        return (Path: Path.GetFullPath(target), Kind: kind.Value);
                    });
                    if (closed || closingInProgress) break;
                    NetworkAccess.Validate(found.Path, model.AllowNetwork);
                    if (foldersOnly) await directoryShortcutStore.AddAsync(found.Path);
                    else await favoritesStore.SetAsync(found.Path, found.Kind, true);
                    count++;
                }
                catch (Exception error) { errors.Add(Path.GetFileName(original) + "：" + error.Message); }
            }
        }
        try {
            if (foldersOnly) await (directorySaveTail = SaveAfterAsync(directorySaveTail, Register));
            else await (favoriteSaveTail = SaveAfterAsync(favoriteSaveTail, Register));
            if (foldersOnly) await RefreshChangedDirectoryShortcutsAsync();
            else await RefreshChangedFavoritesAsync();
            if (!closed) model.Status = $"{(foldersOnly ? "フォルダリンク" : "お気に入り")}に{count}件登録しました。"
                + (errors.Count == 0 ? "" : $" {errors.Count}件を登録できませんでした：" + string.Join(" / ", errors.Take(3)));
        }
        catch (Exception error) { if (!closed) model.Status = "登録できませんでした：" + error.Message; }
    }
    private void RegistrationSourceDown(object sender, MouseButtonEventArgs e)
    {
        listDragPath = null; listDragStart = e.GetPosition(this);
        for (var element = e.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, sender);
            element = element is Visual ? VisualTreeHelper.GetParent(element) : null) {
            if (element is FrameworkElement view && EntryPath(view.DataContext) is string path && !BrowsePath.IsVirtual(path)) {
                listDragPath = path; break;
            }
            if (element is TreeViewItem { Tag: string folder } && !BrowsePath.IsVirtual(folder)) { listDragPath = folder; break; }
        }
    }
    private void RegistrationSourceMove(object sender, MouseEventArgs e)
    {
        if (listDragPath is null || e.LeftButton != MouseButtonState.Pressed) { listDragPath = null; return; }
        var delta = e.GetPosition(this) - listDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var path = listDragPath; listDragPath = null;
        var paths = OperationSelection(path).Select(EntryPath).OfType<string>().Where(item => !BrowsePath.IsVirtual(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;
        e.Handled = true;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Link);
    }
}
