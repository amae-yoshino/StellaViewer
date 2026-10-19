using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private Task navigation = Task.CompletedTask;
    private CancellationTokenSource locationListing = new();
    private bool loadingLocations, sortingLocations;
    private async Task LoadLocationsAsync(string folder, CancellationToken token, LocationScrollPosition? restorePosition = null, bool preferCached = false, string[]? selectedPaths = null, string? primaryPath = null)
    {
        token.ThrowIfCancellationRequested();
        RememberLocationScroll();
        RememberLocationListing();
        if(selectedPaths is null&&primaryPath is null&&model.LocationFolder==folder){
            selectedPaths=Locations.SelectedItems.OfType<LocationEntry>().Select(entry=>entry.Path).ToArray();
            primaryPath=(Locations.SelectedItem as LocationEntry)?.Path;
        }
        var restoreSelection=selectedPaths is not null||primaryPath is not null;
        var interactionVersion=locationInteractionVersion;
        var scrollPosition = restorePosition is not null && restorePosition.Mode == LocationModeBox.SelectedIndex && restorePosition.Sort == SelectedLocationSort
            ? restorePosition : SavedLocationScroll(folder);
        var scrollVersion = ++locationScrollVersion;
        restoringLocationScroll = scrollPosition is not null;
        locationListing.Cancel(); locationListing.Dispose();
        locationListing = new CancellationTokenSource();
        token = locationListing.Token;
        var key = SnapshotKey(folder);
        var cached = preferCached && !BrowsePath.IsVirtual(folder) ? locationSnapshots.GetValueOrDefault(key) : null;
        loadingLocations = true;
        physicalLocationListingComplete = false;
        var hideHidden = hideHiddenFolders;
        var cachedShown = false;
        model.LocationsStatus = "フォルダ・書庫を読み込み中…";
        try
        {
            NetworkAccess.Validate(BrowsePath.PhysicalPath(folder), model.AllowNetwork);
            if (cached is not null) {
                var sameFolder=model.LocationFolder==folder;
                model.LocationArchiveSession?.Dispose();model.LocationArchiveSession=null;model.LocationFolder=folder;
                sortingLocations=true;
                try {
                    var changed=!sameFolder||!model.Locations.SequenceEqual(cached);
                    if(!sameFolder)model.Locations.ReplaceAll(cached);
                    else if(changed)model.Locations.Reconcile(cached,entry=>entry.Path);
                    RestoreLocationSelection(selectedPaths,primaryPath);SyncLocationTiles(changed);
                }
                finally { sortingLocations=false; }
                loadingLocations=false;physicalLocationListingComplete=true;listedHiddenFolders=hideHidden;cachedShown=true;
                model.LocationsStatus=$"フォルダ・書庫（{cached.Length:N0}件・更新確認中）";
                await RestoreLocationScrollAsync(folder,scrollPosition,scrollVersion,token);
                if(token.IsCancellationRequested||closed||scrollVersion!=locationScrollVersion)return;
                locationRefresh=RefreshLocationSnapshotAsync(folder,key,scrollVersion,token);
                return;
            }
            sortingLocations=true;
            try {model.Locations.Clear();model.SelectedLocationPath=null;SyncLocationTiles(true);}
            finally {sortingLocations=false;}
            ArchiveSession? view = null;
            if (ArchiveLocation.Parse(folder) is {} archiveFolder) {
                var source = model.LocationArchiveSession is {} current && ArchiveLocation.SameSource(current.SourcePath,archiveFolder.ArchivePath) ? current
                    : archiveSession is {} displayed && ArchiveLocation.SameSource(displayed.SourcePath,archiveFolder.ArchivePath) ? displayed : null;
                view = source?.CreateView() ?? await OpenArchiveSourceAsync(archiveFolder.ArchivePath,token);
                if(token.IsCancellationRequested||closed){view.Dispose();return;}
            }
            model.LocationArchiveSession?.Dispose();model.LocationArchiveSession=view;model.LocationFolder=folder;
            if (PdfLocation.IsIdentity(folder)) {
                loadingLocations = false;
                model.LocationsStatus = "PDF（ページは下の一覧から選択）";
                return;
            }
            if (ArchiveLocation.Parse(folder) is { } location && model.LocationArchiveSession is { } session) {
                var items = await Task.Run(() => session.Locations(location.EntryPath), token);
                if (token.IsCancellationRequested) return;
                foreach (var item in items) model.Locations.Add(item);
                SyncLocationTiles(true); loadingLocations = false; await SortLocationsAsync();
                if (!token.IsCancellationRequested) model.LocationsStatus = $"フォルダ・書庫（{model.Locations.Count}件）";
                return;
            }
            await foreach (var batch in FolderBrowser.EnumerateLocationsAsync(folder, token, hideHidden, model.AllowNetwork))
            {
                if (token.IsCancellationRequested) return;
                foreach (var item in batch) model.Locations.Add(item);
                SyncLocationTiles(false);
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            }
            if (!token.IsCancellationRequested) {
                loadingLocations = false;
                await SortLocationsAsync();
                if (!token.IsCancellationRequested) {physicalLocationListingComplete=true;listedHiddenFolders=hideHidden;RememberLocationListing();}
                if (!token.IsCancellationRequested) model.LocationsStatus = $"フォルダ・書庫（{model.Locations.Count}件）";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested) model.LocationsStatus = error.Message; }
        finally {
            if (!token.IsCancellationRequested) loadingLocations = false;
            if (scrollVersion == locationScrollVersion && !cachedShown) {
                if(!token.IsCancellationRequested&&restoreSelection&&interactionVersion==locationInteractionVersion)RestoreLocationSelection(selectedPaths,primaryPath);
                await RestoreLocationScrollAsync(folder, scrollPosition, scrollVersion, token);
            }
        }
    }
    private async void LocationSelected(object sender, SelectionChangedEventArgs e)
    {
        if (selectingBatch) return;
        PublishSelection();
        if (!sortingLocations && Keyboard.Modifiers == ModifierKeys.None && Locations.SelectedIndex >= 0) locationSelectionAnchor = Locations.SelectedIndex;
        if (sortingLocations || Locations.SelectedItems.Count > 1 || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
        model.SelectedLocationPath = (Locations.SelectedItem as LocationEntry)?.Path;
        if (Locations.SelectedItem is LocationEntry entry) await (navigation = OpenAsync(entry.Path, updateLocations:false));
    }
    private async void LocationPointerDown(object sender, MouseButtonEventArgs e)
    {
        if(sender is not FrameworkElement { DataContext:LocationEntry entry })return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
        if(e.ClickCount==2){e.Handled=true;await(navigation=OpenAsync(entry.Path));}
        else if(sender is ListBoxItem&&ReferenceEquals(Locations.SelectedItem,entry))await(navigation=OpenAsync(entry.Path,updateLocations:false));
    }
    private async void LocationKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Enter)return;
        var entry=(Keyboard.FocusedElement as FrameworkElement)?.DataContext as LocationEntry??Locations.SelectedItem as LocationEntry;
        if(entry is null)return;
        e.Handled=true;await(navigation=OpenAsync(entry.Path));
    }
    private async void BreadcrumbNavigate(object? sender, PathNavigationEventArgs e) => await (navigation = OpenAsync(e.Path));
}
