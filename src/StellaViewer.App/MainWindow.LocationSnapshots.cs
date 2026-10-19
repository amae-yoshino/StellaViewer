using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    // Metadata only; decoded images are owned by the separate image caches.
    private const int LocationSnapshotLimit = 16, LocationSnapshotEntryLimit = 100_000;
    private readonly record struct LocationSnapshotKey(string Folder, bool HideHidden, int Sort);
    private readonly Dictionary<LocationSnapshotKey, LocationEntry[]> locationSnapshots = new();
    private bool physicalLocationListingComplete, listedHiddenFolders;
    private int locationSnapshotEntries;
    private Task locationRefresh = Task.CompletedTask;
    private Func<CancellationToken, Task>? locationRefreshBarrier;

    private LocationSnapshotKey SnapshotKey(string folder) => new(LocationScrollKey(folder), hideHiddenFolders, SelectedLocationSort);
    private void RememberLocationListing()
    {
        if (!physicalLocationListingComplete || loadingLocations || model.LocationFolder.Length == 0 || BrowsePath.IsVirtual(model.LocationFolder)) return;
        StoreLocationSnapshot(new(LocationScrollKey(model.LocationFolder), listedHiddenFolders, SelectedLocationSort), model.Locations.ToArray());
    }
    private void StoreLocationSnapshot(LocationSnapshotKey key, LocationEntry[] entries)
    {
        if (locationSnapshots.Remove(key, out var old)) locationSnapshotEntries -= old.Length;
        if (entries.Length > LocationSnapshotEntryLimit) return;
        while (locationSnapshots.Count > 0 && (locationSnapshots.Count >= LocationSnapshotLimit || locationSnapshotEntries + entries.Length > LocationSnapshotEntryLimit)) {
            var oldest = locationSnapshots.First(); locationSnapshots.Remove(oldest.Key); locationSnapshotEntries -= oldest.Value.Length;
        }
        locationSnapshots.Add(key, entries); locationSnapshotEntries += entries.Length;
    }
    private void RestoreLocationSelection(string[]? paths, string? primary)
    {
        var comparer=BrowsePath.IsVirtual(model.LocationFolder)?StringComparer.Ordinal:StringComparer.OrdinalIgnoreCase;
        var chosen = (paths ?? []).ToHashSet(comparer);
        if (primary is not null) chosen.Add(primary);
        var matches = model.Locations.Where(entry => chosen.Contains(entry.Path)).ToArray();
        var wasSorting = sortingLocations; var wasSelecting = selectingBatch;
        sortingLocations = selectingBatch = true;
        try {
            Locations.UnselectAll();
            Locations.SelectedItem = matches.FirstOrDefault(entry => comparer.Equals(entry.Path, primary)) ?? matches.FirstOrDefault();
            foreach (var item in matches) if (!Locations.SelectedItems.Contains(item)) Locations.SelectedItems.Add(item);
            model.SelectedLocationPath = (Locations.SelectedItem as LocationEntry)?.Path;
            locationSelectionAnchor = Math.Max(0, Locations.SelectedIndex);
        }
        finally { sortingLocations = wasSorting; selectingBatch = wasSelecting; PublishSelection(); }
    }
    private async Task RefreshLocationSnapshotAsync(string folder, LocationSnapshotKey key, long version, CancellationToken token)
    {
        try {
            // Restore the cached viewport before starting metadata I/O and thumbnail completions.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background, token);
            if (locationRefreshBarrier is { } barrier) await barrier(token);
            var sortVersion=locationSortVersion;
            var entries = new List<LocationEntry>();
            await foreach (var batch in FolderBrowser.EnumerateLocationsAsync(folder, token, key.HideHidden, model.AllowNetwork)) entries.AddRange(batch);
            var sorted = await Task.Run(() => FolderBrowser.SortLocations(entries, key.Sort), token);
            if (token.IsCancellationRequested || closed || closingInProgress || version != locationScrollVersion || model.LocationFolder != folder) return;
            if (SelectedLocationSort != key.Sort || hideHiddenFolders != key.HideHidden) {model.LocationsStatus=$"フォルダ・書庫（{model.Locations.Count:N0}件）";return;}
            if(sortVersion!=locationSortVersion){
                RememberLocationListing();model.LocationsStatus=$"フォルダ・書庫（{model.Locations.Count:N0}件）";return;
            }
            if (!model.Locations.SequenceEqual(sorted)) {
                var position = CaptureLocationScroll();
                var selected = Locations.SelectedItems.OfType<LocationEntry>().Select(entry => entry.Path).ToArray();
                var primary = (Locations.SelectedItem as LocationEntry)?.Path;
                var wasSorting = sortingLocations; sortingLocations = true;
                restoringLocationScroll = position is not null;
                try {
                    model.Locations.Reconcile(sorted, entry => entry.Path);
                    RestoreLocationSelection(selected, primary);
                    if (LocationModeBox.SelectedIndex == 2) SyncLiveTileRows(model.LocationTileRows,model.Locations,locationTileColumns,entry=>entry.Path);
                }
                finally { sortingLocations = wasSorting; }
                await RestoreLocationScrollAsync(folder, position, version, token);
            }
            if(token.IsCancellationRequested||closed||version!=locationScrollVersion||model.LocationFolder!=folder)return;
            StoreLocationSnapshot(key, sorted);
            model.LocationsStatus = $"フォルダ・書庫（{model.Locations.Count:N0}件）";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) {
            if (!closed && !token.IsCancellationRequested && version == locationScrollVersion)
                model.LocationsStatus = "一覧の更新を確認できません：" + error.Message;
        }
    }
}
