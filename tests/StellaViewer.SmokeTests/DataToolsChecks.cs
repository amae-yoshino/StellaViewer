using System.Text.Json;
using StellaViewer.Infrastructure;

internal static class DataToolsChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
        async Task Reject(Func<Task> action, string label) { try { await action(); } catch (Exception error) when (error is InvalidDataException or JsonException) { Check(true, label); return; } throw new Exception(label); }
        var root = Path.Combine(Path.GetFullPath(output), "data-tools-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var cacheDir = Path.Combine(root, "cache"); var cache = new DecodedImageCache(new MagickImageDecoder(), cacheDir); cache.Configure(new(8, 8, 0));
        var image = Path.Combine(output, "sample.png"); var pixels = await cache.GetAsync(image, default);
        var usage = await cache.GetUsageAsync(); Check(usage.MemoryBytes == pixels.BgraPixels.Length && usage.MemoryCount == 1 && usage.DiskBytes > usage.MemoryBytes && usage.DiskCount == 1, "Cache management reports actual memory and disk usage");
        var unrelated = Path.Combine(cacheDir, "keep.txt"); File.WriteAllText(unrelated, "keep");
        await cache.ClearAsync(true, false); usage = await cache.GetUsageAsync(); Check(usage.MemoryBytes == 0 && usage.DiskCount == 1, "Memory clear preserves disk cache");
        Check((await cache.GetAsync(image, default)).BgraPixels.SequenceEqual(pixels.BgraPixels) && cache.DiskHits == 1, "Memory clear preserves pixels already held by caller");
        await cache.ClearAsync(false, true); usage = await cache.GetUsageAsync(); Check(usage.MemoryCount == 1 && usage.DiskBytes == 0 && File.Exists(unrelated), "Disk clear preserves memory and unrelated files");
        await cache.ClearAsync(true, true); usage = await cache.GetUsageAsync(); Check(usage.MemoryBytes == 0 && usage.DiskCount == 0, "Both cache pools can be cleared");
        var before = cache.Decodes; await cache.GetAsync(image, default); Check(cache.Decodes == before + 1, "Cache remains functional after clearing");
        var settings = new ViewerPreferences(Dark: true, ListMode: 2, SortMode: 2, LocationSortMode: 1) { RecordRecentLocations = false, RecordViewingState = false, Cache = new(64, 128, 3), Keyboard = new StellaViewer.Core.KeyboardSettings().Add(StellaViewer.Core.ViewerCommand.Settings, new("F2")) };
        var exported = Path.Combine(root, "settings.json"); await SettingsTransfer.ExportAsync(exported, settings); var imported = await SettingsTransfer.ImportAsync(exported);
        Check(imported.Dark && imported.ListMode == 2 && imported.LocationSortMode == 1 && imported.Cache == settings.Cache && imported.Keyboard.AllFor(StellaViewer.Core.ViewerCommand.Settings).Count == 1 && !imported.RecordRecentLocations && !imported.RecordViewingState, "Settings transfer preserves viewer options and shortcuts");
        var text = File.ReadAllText(exported); Check(!text.Contains("last-view") && !text.Contains(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase), "Settings export excludes browsing locations");
        File.WriteAllText(exported, "{\"Application\":\"StellaViewer\",\"Version\":999,\"Settings\":{}}"); await Reject(() => SettingsTransfer.ImportAsync(exported), "Future settings format rejected");
        File.WriteAllText(exported, "{}"); await Reject(() => SettingsTransfer.ImportAsync(exported), "Unrelated JSON rejected");
        File.WriteAllText(exported, "not json"); await Reject(() => SettingsTransfer.ImportAsync(exported), "Broken import rejected");
        using (var file = File.Open(exported, FileMode.Create)) file.SetLength(4 * 1024 * 1024 + 1); await Reject(() => SettingsTransfer.ImportAsync(exported), "Oversized import rejected before parsing");
        var prefPath = Path.Combine(root, "viewer-preferences.json"); await PreferencesStore.SaveChangeAsync(recordRecentLocations: false, recordViewingState: false, filePath: prefPath);
        Check(!PreferencesStore.Load(prefPath).RecordRecentLocations && !PreferencesStore.Load(prefPath).RecordViewingState, "Independent history recording preferences persist");
        var historyPath = Path.Combine(root, "recent.json"); var history = new RecentLocationStore(historyPath); var peer = new RecentLocationStore(historyPath);
        await history.RecordAsync(root); await history.RecordAsync(Path.Combine(root, "second"));
        await peer.RemoveAsync(root); Check((await history.LoadAsync()).Length == 1, "Per-place history removal shared across stores");
        await history.RecordAsync(root, recordingAllowed: () => PreferencesStore.Load(prefPath).RecordRecentLocations); Check((await history.LoadAsync()).Length == 1, "Shared recording stop prevents stale window writes");
        await peer.RemoveAsync(); Check((await history.LoadAsync()).Length == 0, "Whole history deletion shared across stores");
        File.WriteAllText(historyPath, "broken"); await history.RemoveAsync(); Check((await peer.LoadAsync()).Length == 0, "Whole history deletion also resets unreadable history");
        await ViewerStateStore.SaveAsync(new(Width: 1234), new(root), root);
        var viewPath = Path.Combine(root, "last-view.json"); var original = File.ReadAllBytes(viewPath);
        await ViewerStateStore.SaveAsync(new(Width: 1357), new("should-not-be-saved"), root, saveView: false);
        Check(original.SequenceEqual(File.ReadAllBytes(viewPath)) && ViewerStateStore.Load(root).Layout.Width == 1357, "Stopping browsing-state save preserves window layout saving");
        await ViewerStateStore.SaveAsync(new(), new("stale-view"), root, savingAllowed: () => false);
        Check(original.SequenceEqual(File.ReadAllBytes(viewPath)), "Shared save guard prevents stale browsing-state writes");
        File.WriteAllText(viewPath + ".corrupt-test", "private location");
        await ViewerStateStore.ClearViewAsync(root);
        Check(ViewerStateStore.Load(root).View.Folder == "" && !Directory.GetFiles(root, "last-view.json.corrupt-*").Any(), "Clearing saved view removes corruption copies");
        File.WriteAllText(viewPath, "broken");
        Check(ViewerStateStore.Load(root).View.Folder == "", "Deleted viewing location cannot return from backup recovery");
        var positions = new FolderImagePositionStore(Path.Combine(root, "positions.json"));
        await positions.SaveAsync(root, Path.Combine(root, "image.png")); await positions.ClearAsync();
        await positions.SaveAsync(root, Path.Combine(root, "stale.png"), () => false);
        Check(await positions.LoadAsync(root) is null, "Cleared folder positions reject stale window writes");
        return count;
    }
}
