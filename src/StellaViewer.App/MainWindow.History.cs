using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private sealed record HistoryEntry(string Folder, string? SelectedPath, string Query, int Sort, int ListMode, double Offset, string? LocationFolder=null, LocationScrollPosition? LocationScroll=null, string[]? LocationSelection=null, string? PrimaryLocation=null);
    private readonly Stack<HistoryEntry> history = new();
    private readonly Stack<HistoryEntry> forwardHistory = new();
    private bool restoringNavigation;
    private int historyRequestVersion;
    private HistoryEntry CaptureLocation()
    {
        var list = ListModeBox.SelectedIndex == 2 ? TileList : ImageList;
        return new(model.Folder, (ImageList.SelectedItem as FileEntry)?.Path, SearchBox.Text,
            SelectedImageSort, ListModeBox.SelectedIndex, FindScrollViewer(list)?.VerticalOffset ?? 0,model.LocationFolder,CaptureLocationScroll(),Locations.SelectedItems.OfType<LocationEntry>().Select(entry=>entry.Path).ToArray(),(Locations.SelectedItem as LocationEntry)?.Path);
    }
    private void UpdateHistoryButtons() { model.CanGoBack = history.Count > 0; model.CanGoForward = forwardHistory.Count > 0; }
    private void RememberLocation()
    {
        history.Push(CaptureLocation()); forwardHistory.Clear(); UpdateHistoryButtons();
    }
    private Task GoBackAsync() => RestoreHistoryAsync(history, forwardHistory);
    private Task GoForwardAsync() => RestoreHistoryAsync(forwardHistory, history);
    private async Task RestoreHistoryAsync(Stack<HistoryEntry> source, Stack<HistoryEntry> destination)
    {
        var historyRequest = ++historyRequestVersion;
        if (!source.TryPeek(out var previous)) return;
        try { NetworkAccess.Validate(BrowsePath.PhysicalPath(previous.Folder), model.AllowNetwork); }
        catch (NotSupportedException error) { model.Status = error.Message; return; }
        var request = openRequestVersion;
        var exists = await Task.Run(() => BrowsePath.IsVirtual(previous.Folder) ? File.Exists(BrowsePath.PhysicalPath(previous.Folder)) : Directory.Exists(previous.Folder));
        if (historyRequest != historyRequestVersion || request != openRequestVersion || closed || closingInProgress) return;
        source.Pop();
        UpdateHistoryButtons();
        if (!exists)
        { model.Status = "移動先が見つかりません：" + BrowsePath.DisplayPath(previous.Folder); return; }
        destination.Push(CaptureLocation()); UpdateHistoryButtons();
        restoringNavigation = true;
        try { SearchBox.Text = previous.Query; SelectedImageSort = previous.Sort; }
        finally { restoringNavigation = false; }
        var selectedExists = previous.SelectedPath is not null && await Task.Run(() => File.Exists(BrowsePath.PhysicalPath(previous.SelectedPath)));
        if (historyRequest != historyRequestVersion || request != openRequestVersion || closed || closingInProgress) return;
        var opening = OpenAsync(selectedExists
            ? previous.SelectedPath! : previous.Folder, recordHistory: false,updateLocations:false);
        var openingRequest = openRequestVersion;
        // Restore the browser independently of the image enumeration/decode.
        var locations = LoadLocationsAsync(previous.LocationFolder??previous.Folder,CancellationToken.None,previous.LocationScroll,
            preferCached:true,selectedPaths:previous.LocationSelection,primaryPath:previous.PrimaryLocation);
        await Task.WhenAll(opening,locations);
        if (openingRequest != openRequestVersion) return;
        var token = browse.Token;
        if (token.IsCancellationRequested || closed) return;
        await Dispatcher.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested || closed) return;
            if (ListModeBox.SelectedIndex == previous.ListMode)
                FindScrollViewer(ListModeBox.SelectedIndex == 2 ? TileList : ImageList)?.ScrollToVerticalOffset(previous.Offset);
            else ScrollSelection();
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } nested) return nested;
        return null;
    }
    private async void BackClick(object sender, RoutedEventArgs e) => await (navigation = GoBackAsync());
    private async void ForwardClick(object sender, RoutedEventArgs e) => await (navigation = GoForwardAsync());
    private async Task ReloadAsync()
    {
        if (!CanReadCurrentFolder()) return;
        if (PdfLocation.IsIdentity(model.Folder)) {
            var folder = model.Folder; var request = openRequestVersion;
            CancelPrefetch();
            await imageCache.InvalidateAsync(all.Select(entry => entry.Path).ToArray());
            if (closed || closingInProgress || request != openRequestVersion || model.Folder != folder || !CanReadCurrentFolder()) return;
            pdfSession?.Dispose();
            pdfSession = null;
            archiveSession?.Dispose();
            archiveSession = null;
            await OpenAsync((ImageList.SelectedItem as FileEntry)?.Path ?? model.Folder, recordHistory: false,updateLocations:model.LocationFolder==model.Folder);
            return;
        }
        if (ArchiveLocation.IsIdentity(model.Folder)) {
            var folder = model.Folder; var request = openRequestVersion;
            CancelPrefetch();
            await imageCache.InvalidateAsync(all.Select(entry => entry.Path).ToArray());
            if (closed || closingInProgress || request != openRequestVersion || model.Folder != folder || !CanReadCurrentFolder()) return;
            archiveSession?.Dispose();
            archiveSession = null;
            await OpenAsync((ImageList.SelectedItem as FileEntry)?.Path ?? model.Folder, recordHistory: false,updateLocations:model.LocationFolder==model.Folder);
            return;
        }
        if (model.Folder.Length > 0) {
            CancelPrefetch();
            await imageCache.InvalidateAsync(all.Select(entry => entry.Path).ToArray());
            if (closed || closingInProgress || !CanReadCurrentFolder()) return;
            var selected = (ImageList.SelectedItem as FileEntry)?.Path;
            var folder = model.Folder; var request = openRequestVersion;
            var exists = selected is not null && await Task.Run(() => File.Exists(selected));
            if (request != openRequestVersion || closed || closingInProgress) return;
            await OpenAsync(exists ? selected! : folder, recordHistory: false,updateLocations:model.LocationFolder==folder,reuseLocations:false);
        }
    }
    private async void ReloadClick(object sender, RoutedEventArgs e) => await ReloadAsync();
    private void PreviousPageClick(object sender, RoutedEventArgs e) => Move(-1);
    private void NextPageClick(object sender, RoutedEventArgs e) => Move(1);
    private void FirstPageClick(object sender, RoutedEventArgs e) => MoveToBoundary(false);
    private void LastPageClick(object sender, RoutedEventArgs e) => MoveToBoundary(true);
    private void MoveToBoundary(bool last)
    {
        if (!CanReadCurrentFolder() || model.Entries.Count == 0) return;
        SelectPageAt(last ? model.Entries.Count - 1 : 0);
    }
    private void FocusPageClick(object sender, RoutedEventArgs e)
    {
        if (ViewerToolbar.IsVisible) { PageBox.Focus(); PageBox.SelectAll(); }
        else ShowPageJump();
    }
    private void ExitClick(object sender, RoutedEventArgs e) => Close();
    private void InfoClick(object sender, RoutedEventArgs e) => InfoToggle.IsChecked = InfoToggle.IsChecked != true;
    private void ListModeMenuClick(object sender, RoutedEventArgs e)
    { if (sender is MenuItem { Tag: string value } && int.TryParse(value, out var mode)) ListModeBox.SelectedIndex = mode; }
    private async void OpenImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "画像・ショートカット|*.jpg;*.jpeg;*.png;*.apng;*.bmp;*.gif;*.webp;*.psd;*.clip;*.lnk|すべてのファイル|*.*" };
        if (dialog.ShowDialog(this) == true) await OpenAsync(dialog.FileName);
    }
    private void ShortcutsClick(object sender, RoutedEventArgs e) => ShowSettings(showShortcuts: true);
}
