using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private sealed class WatchInbox(string folder, CancellationToken token)
    {
        public readonly string Folder = folder;
        public readonly CancellationToken Token = token;
        public readonly object Gate = new();
        public readonly HashSet<string> Paths = new(StringComparer.OrdinalIgnoreCase);
        public bool Rescan, Scheduled;
        public string? Warning;
    }
    private FolderChangeMonitor? folderMonitor;
    private WatchInbox? watchInbox;
    private Task watchWork = Task.CompletedTask;
    private readonly HashSet<string> missingDisplayedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> changedDisplayedPaths = new(StringComparer.OrdinalIgnoreCase);
    private FileEntry? displayedEntry;
    private int displayRevision, watchApplyCount;
    private string? watchWarning;
    internal bool WatchFoldersEnabled { get; set; } = true;

    private async Task StartFolderMonitorAsync(string folder, CancellationToken token)
    {
        if (!WatchFoldersEnabled) return;
        var inbox = new WatchInbox(folder, token); watchInbox = inbox; watchWarning = null;
        var allowed = model.AllowNetwork;
        try {
            var monitor = await Task.Run(() => new FolderChangeMonitor(folder, batch => QueueWatchBatch(inbox, batch), allowed));
            if (!IsCurrent(inbox)) { monitor.Dispose(); return; }
            folderMonitor = monitor;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            if (IsCurrent(inbox)) watchWarning = "フォルダを監視できません。F5で再読み込みしてください：" + error.Message;
        }
    }
    private void StopFolderMonitor()
    {
        watchInbox = null; folderMonitor?.Dispose(); folderMonitor = null; watchWarning = null;
    }
    private bool IsCurrent(WatchInbox inbox) => ReferenceEquals(watchInbox, inbox) && !inbox.Token.IsCancellationRequested && !closed;
    private void QueueWatchBatch(WatchInbox inbox, FolderChangeBatch batch)
    {
        if (!IsCurrent(inbox)) return;
        lock (inbox.Gate) {
            inbox.Rescan |= batch.RescanRequired;
            inbox.Warning = batch.Warning ?? inbox.Warning;
            if (!inbox.Rescan) {
                inbox.Paths.UnionWith(batch.Paths);
                if (inbox.Paths.Count > 4096) inbox.Rescan = true;
            }
            if (inbox.Rescan) inbox.Paths.Clear();
        }
        ScheduleWatchDrain(inbox);
    }
    private void ScheduleWatchDrain(WatchInbox inbox)
    {
        lock (inbox.Gate) {
            if (inbox.Scheduled || !IsCurrent(inbox) || inbox.Paths.Count == 0 && !inbox.Rescan) return;
            inbox.Scheduled = true;
        }
        if (Dispatcher.HasShutdownStarted) return;
        try { Dispatcher.BeginInvoke(new Action(() => watchWork = DrainWatchAsync(inbox))); }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }
    private async Task DrainWatchAsync(WatchInbox inbox)
    {
        try {
            while (IsCurrent(inbox)) {
                if (loading || loadingLocations) { await Task.Delay(75, inbox.Token); continue; }
                FolderChangeBatch batch;
                lock (inbox.Gate) {
                    if (inbox.Paths.Count == 0 && !inbox.Rescan) return;
                    batch = new(inbox.Paths.ToArray(), inbox.Rescan, inbox.Warning);
                    inbox.Paths.Clear(); inbox.Rescan = false; inbox.Warning = null;
                }
                var hidden = hideHiddenFolders;
                var knownPaths = batch.RescanRequired ? all.Select(e => e.Path).Concat(StringComparer.OrdinalIgnoreCase.Equals(model.LocationFolder,inbox.Folder)?model.Locations.Select(e => e.Path):[]).ToArray() : [];
                FolderItemChange[] changes; string? readWarning = null;
                try {
                    changes = batch.RescanRequired
                        ? await FolderChanges.RescanAsync(inbox.Folder, knownPaths, hidden, inbox.Token, model.AllowNetwork)
                        : await FolderChanges.ReadAsync(inbox.Folder, batch.Paths, hidden, inbox.Token, model.AllowNetwork);
                }
                catch (DirectoryNotFoundException) {
                    changes = knownPaths.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => new FolderItemChange(path, null, null, true)).ToArray();
                    readWarning = "閲覧フォルダがありません。再作成された場合はF5で再読み込みしてください。";
                }
                if (!IsCurrent(inbox)) return;
                if (hidden != hideHiddenFolders) {
                    QueueWatchBatch(inbox, new([], true)); continue;
                }
                if (batch.Warning is not null) watchWarning = "監視通知の欠落を検出し、一覧を再確認しました。";
                if (readWarning is not null) watchWarning = readWarning;
                CancelPrefetch();
                await imageCache.InvalidateAsync(changes.Select(change => change.Path));
                if (IsCurrent(inbox)) await ApplyWatchChangesAsync(inbox, changes);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) {
            if (IsCurrent(inbox)) { watchWarning = "フォルダの変更を確認できません。F5で再読み込みしてください：" + error.Message; UpdateStatus(); }
        }
        finally {
            lock (inbox.Gate) inbox.Scheduled = false;
            ScheduleWatchDrain(inbox);
        }
    }

    private HashSet<string> ProtectedDisplayPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ImageList.SelectedItem is FileEntry selected) paths.Add(selected.Path);
        if (firstPage is not null) paths.Add(firstPage.Entry.Path);
        if (secondPage is not null) paths.Add(secondPage.Entry.Path);
        return paths;
    }

    private async Task ApplyWatchChangesAsync(WatchInbox inbox, FolderItemChange[] changes)
    {
        while (IsCurrent(inbox)) {
            if (loading || loadingLocations) { await Task.Delay(75, inbox.Token); continue; }
            var revision = displayRevision; var query = SearchBox.Text;
            var sort = SelectedImageSort; var locationSort = SelectedLocationSort;
            var protectedPaths = ProtectedDisplayPaths();
            var locationFolder=model.LocationFolder;
            var updateLocations=StringComparer.OrdinalIgnoreCase.Equals(locationFolder,inbox.Folder);
            var images = all.ToArray(); var locations = model.Locations.ToArray();
            var result = await Task.Run(() => {
                var imageMap = images.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
                var locationMap = locations.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
                foreach (var change in changes) {
                    inbox.Token.ThrowIfCancellationRequested();
                    if (change.Error is not null) continue;
                    if (change.Image is not null) imageMap[change.Path] = change.Image;
                    else if (!change.Missing || !protectedPaths.Contains(change.Path)) imageMap.Remove(change.Path);
                    if(updateLocations){
                        if (change.Location is not null) locationMap[change.Path] = change.Location;
                        else locationMap.Remove(change.Path);
                    }
                }
                var nextAll = imageMap.Values.ToList();
                var filtered = nextAll.Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
                var ordered = sort switch {
                    1 => filtered.OrderBy(e => e.Modified).ThenBy(e => e, FolderBrowser.NaturalComparer),
                    2 => filtered.OrderBy(e => e.Length).ThenBy(e => e, FolderBrowser.NaturalComparer),
                    _ => filtered.OrderBy(e => e, FolderBrowser.NaturalComparer)
                };
                return (All: nextAll, Images: ordered.ToArray(), Locations: FolderBrowser.SortLocations(locationMap.Values, locationSort),
                    Directories: FolderBrowser.SortLocations(locationMap.Values.Where(e => e.IsDirectory), 0));
            }, inbox.Token);
            if (!IsCurrent(inbox)) return;
            if (loading || loadingLocations || revision != displayRevision || query != SearchBox.Text || sort != SelectedImageSort || locationSort != SelectedLocationSort || locationFolder!=model.LocationFolder) continue;
            search.Cancel();
            locationSortVersion++;
            var selectedPath = (ImageList.SelectedItem as FileEntry)?.Path;
            var locationPath = (Locations.SelectedItem as LocationEntry)?.Path;
            var list = ListModeBox.SelectedIndex == 2 ? TileList : ImageList;
            var scroll = FindScrollViewer(list); var offset = scroll?.VerticalOffset ?? 0;
            updating = sortingLocations = true;
            try {
                all = result.All;
                model.Entries.Reconcile(result.Images, e => e.Path);
                model.Locations.Reconcile(result.Locations, e => e.Path);
                ImageList.SelectedItem = model.Entries.FirstOrDefault(e => string.Equals(e.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
                Locations.SelectedItem = model.Locations.FirstOrDefault(e => string.Equals(e.Path, locationPath, StringComparison.OrdinalIgnoreCase));
                model.SelectedLocationPath = (Locations.SelectedItem as LocationEntry)?.Path;
                if (ListModeBox.SelectedIndex == 2) SyncLiveTileRows(model.TileRows, model.Entries, tileColumns, e => e.Path);
                if (LocationModeBox.SelectedIndex == 2) SyncLiveTileRows(model.LocationTileRows, model.Locations, locationTileColumns, e => e.Path);
            }
            finally { updating = sortingLocations = false; }
            foreach (var change in changes.Where(c => c.Error is null && protectedPaths.Contains(c.Path))) {
                if (change.Missing) { missingDisplayedPaths.Add(change.Path); changedDisplayedPaths.Remove(change.Path); }
                else { missingDisplayedPaths.Remove(change.Path); changedDisplayedPaths.Add(change.Path); }
            }
            var error = changes.FirstOrDefault(c => c.Error is not null)?.Error;
            if (error is not null) watchWarning = "一部の項目の情報を取得できません：" + error;
            var dirtyPaths = changes.Select(c => c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            RefreshChangedThumbnails(ListArea, dirtyPaths); RefreshChangedThumbnails(LocationArea, dirtyPaths);
            RefreshWatchedTree(inbox.Folder, result.Directories);
            if (spreadEnabled) RebaseSpread();
            if (ImageList.SelectedItem is null && model.Entries.Count > 0) ImageList.SelectedIndex = 0;
            scroll?.ScrollToVerticalOffset(offset);
            model.ListingStatus = $"{model.Entries.Count:N0}件 / 全{all.Count:N0}件";
            model.LocationsStatus = $"フォルダ・書庫（{model.Locations.Count}件）";
            UpdateStatus(); watchApplyCount++; return;
        }
    }

    private static void RefreshChangedThumbnails(DependencyObject parent, HashSet<string> dirtyPaths)
    {
        if (parent is Thumbnail thumbnail && thumbnail.FilePath is string path && dirtyPaths.Contains(path)) thumbnail.Refresh();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) RefreshChangedThumbnails(VisualTreeHelper.GetChild(parent, i), dirtyPaths);
    }

    private static void SyncLiveTileRows<T>(LiveEntryCollection<T[]> rows, IReadOnlyList<T> entries, int columns, Func<T, string> path)
    {
        var previous = rows.ToDictionary(row => path(row[0]), StringComparer.OrdinalIgnoreCase);
        var next = new List<T[]>();
        for (var index = 0; index < entries.Count; index += columns) {
            var row = new T[Math.Min(columns, entries.Count - index)];
            for (var offset = 0; offset < row.Length; offset++) row[offset] = entries[index + offset];
            next.Add(previous.TryGetValue(path(row[0]), out var old) && old.SequenceEqual(row) ? old : row);
        }
        rows.Reconcile(next.ToArray(), row => path(row[0]));
    }

    private void RefreshWatchedTree(string folder, LocationEntry[] directories)
    {
        void Visit(ItemsControl parent) {
            foreach (var node in parent.Items.OfType<TreeViewItem>()) {
                if (node.Tag is string path && string.Equals(path.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) {
                    if (node.Items.Count == 1 && node.Items[0] is TreeViewItem { Tag: null or false }) return;
                    var previous = node.Items.OfType<TreeViewItem>().Where(n => n.Tag is string)
                        .ToDictionary(n => (string)n.Tag, StringComparer.OrdinalIgnoreCase);
                    var wanted = directories.Select(d => d.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    for (var i = node.Items.Count - 1; i >= 0; i--)
                        if (node.Items[i] is TreeViewItem { Tag: string child } && !wanted.Contains(child)) node.Items.RemoveAt(i);
                    for (var i = 0; i < directories.Length; i++) {
                        var childPath = directories[i].Path;
                        if (!previous.TryGetValue(childPath, out var child)) child = FolderNode(childPath);
                        var existing = node.Items.IndexOf(child);
                        if (existing == i) continue;
                        if (existing >= 0) node.Items.RemoveAt(existing);
                        node.Items.Insert(i, child);
                    }
                    return;
                }
                Visit(node);
            }
        }
        Visit(Folders);
    }

    private void AppendWatchNotice()
    {
        string? notice = missingDisplayedPaths.Count > 0 ? "元ファイルがありません"
            : changedDisplayedPaths.Count > 0 ? "元ファイルが更新されています（F5で再読み込み）" : null;
        if (notice is not null) { model.Status += "  " + notice; model.Details += "\n\n" + notice; }
        if (watchWarning is not null) model.Status += "  " + watchWarning;
    }

    private void PruneMissingEntries(string? selectedPath = null)
    {
        if (missingDisplayedPaths.Count == 0) return;
        selectedPath ??= (ImageList.SelectedItem as FileEntry)?.Path;
        search.Cancel(); all.RemoveAll(e => missingDisplayedPaths.Contains(e.Path));
        updating = true;
        try {
            for (var i = model.Entries.Count - 1; i >= 0; i--)
                if (missingDisplayedPaths.Contains(model.Entries[i].Path)) model.Entries.RemoveAt(i);
            ImageList.SelectedItem = model.Entries.FirstOrDefault(e => string.Equals(e.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
            SyncTiles(true);
        }
        finally { updating = false; }
        model.ListingStatus = $"{model.Entries.Count:N0}件 / 全{all.Count:N0}件";
    }

    private bool MovePastMissing(int delta)
    {
        if (missingDisplayedPaths.Count == 0) return false;
        var count = model.Entries.Count;
        var index = spreadEnabled
            ? SpreadPagination.GetPages(count, spreadAnchor, SpreadPagination.MoveGroup(count, spreadGroup, delta))!.Value.First
            : (Math.Max(0, ImageList.SelectedIndex) + delta + count) % count;
        FileEntry? target = null;
        for (var step = 0; step < count; step++) {
            var candidate = model.Entries[index];
            if (!missingDisplayedPaths.Contains(candidate.Path)) { target = candidate; break; }
            index = (index + (delta < 0 ? -1 : 1) + count) % count;
        }
        PruneMissingEntries(target?.Path); RebaseSpread();
        activeDisplay = LoadSelectedAsync(); ScrollSelection(); return true;
    }
}
