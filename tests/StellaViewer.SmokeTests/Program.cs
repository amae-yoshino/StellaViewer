using System.Security.Cryptography;
using ImageMagick;
using StellaViewer.Infrastructure;
if(args.Length==1 && args[0]==DecodeWorker.Argument){Environment.ExitCode=await DecodeWorker.RunAsync();return;}
if(args.Length==2 && args[0]=="--archive-isolation-tests"){Console.WriteLine($"Archive isolation: {await ArchiveIsolationChecks.RunAsync(args[1])} checks passed.");return;}
if(args.Length>0&&args[0]=="--registration-order-tests"){Console.WriteLine($"Registration order checks: {await RegistrationOrderChecks.RunAsync(args[1])}");return;}
if(args.Length>0&&args[0]=="--recycle-tests"){Console.WriteLine($"Recycle checks: {await RecycleChecks.RunAsync(args[1])}");return;}
if(args.Length>0&&args[0]=="--requested-features-tests"){Console.WriteLine($"Requested feature checks: {await RequestedFeaturesChecks.RunAsync(args[1])}");return;}

if (args.Length > 0 && args[0] == "--animation-tests") { await AnimationChecks.RunAsync(args[1]); return; }
if (args.Length > 0 && args[0] == "--psd-tests") { await PsdChecks.RunAsync(args[1]); return; }
if (args.Length > 0 && args[0] == "--preferences-io-tests") { await PreferencesIoChecks.RunAsync(args[1]); return; }
if (args.Length > 0 && args[0] == "--clip-tests") { await ClipChecks.RunAsync(args[1]); return; }
if (args.Length > 0 && args[0] == "--favorites-tests") { await FavoritesChecks.RunAsync(args[1]); return; }

if (args.Length > 0 && args[0] == "--performance") {
    await PerformanceProbe.RunAsync(args.Length > 1 ? args[1] : "artifacts/performance"); return;
}

var output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/smoke");
Directory.CreateDirectory(output);
var decoder = new MagickImageDecoder();
var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine("PASS: " + label);
    checks++;
}

// Synthetic fixtures are written only by this verification executable, never by the viewer.
foreach (var (extension, format) in new[] {
    ("png", MagickFormat.Png), ("jpg", MagickFormat.Jpeg), ("bmp", MagickFormat.Bmp),
    ("gif", MagickFormat.Gif), ("webp", MagickFormat.WebP) })
{
    var path = Path.Combine(output, "sample." + extension);
    using (var fixture = new MagickImage(MagickColors.Red, 8, 6)) fixture.Write(path, format);
    var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
    var decoded = await decoder.DecodeAsync(path, CancellationToken.None);
    Check(decoded.Width == 8 && decoded.Height == 6 && decoded.BgraPixels.Length == 8 * 6 * 4,
        extension + " dimensions and BGRA");
    Check(decoded.BgraPixels[2] > 200 && decoded.BgraPixels[0] < 30 && decoded.BgraPixels[3] == 255,
        extension + " channel order");
    var after = SHA256.HashData(await File.ReadAllBytesAsync(path));
    Check(before.SequenceEqual(after), extension + " unchanged source");
}

var transparentPath = Path.Combine(output, "alpha.png");
using (var fixture = new MagickImage(new MagickColor("#FF000080"), 2, 2)) fixture.Write(transparentPath);
var transparent = await decoder.DecodeAsync(transparentPath, CancellationToken.None);
Check(transparent.BgraPixels[3] is >= 127 and <= 129 && transparent.BgraPixels[2] == 255,
    "straight alpha preserved");

var orientedPath = Path.Combine(output, "oriented.jpg");
using (var fixture = new MagickImage(MagickColors.Blue, 8, 6))
{
    var exif = new ExifProfile();
    exif.SetValue(ExifTag.Orientation, (ushort)6);
    fixture.SetProfile(exif);
    fixture.Orientation = Orientation.RightTop;
    fixture.Write(orientedPath);
}
using (var fixtureReadback = new MagickImage(orientedPath))
    Check(fixtureReadback.Orientation == Orientation.RightTop, "EXIF fixture contains rotation tag");
var oriented = await decoder.DecodeAsync(orientedPath, CancellationToken.None);
Check(oriented.Width == 6 && oriented.Height == 8 && oriented.OriginalWidth == 8 && oriented.OriginalHeight == 6,
    "EXIF orientation and original dimensions");

var profilePath = Path.Combine(output, "profile.png");
using (var fixture = new MagickImage(MagickColors.Red, 3, 2))
{
    fixture.SetProfile(ColorProfiles.SRGB);
    fixture.Write(profilePath);
}
var profiled = await decoder.DecodeAsync(profilePath, CancellationToken.None);
Check(profiled.BgraPixels[2] > 250, "embedded sRGB profile");

var adobePath = Path.Combine(output, "adobe-rgb.png");
using (var fixture = new MagickImage(new MagickColor("#808080"), 2, 2))
{
    fixture.SetProfile(ColorProfiles.AdobeRGB1998);
    fixture.Write(adobePath);
}
var adobe = await decoder.DecodeAsync(adobePath, CancellationToken.None);
// Adobe RGB's gamma maps neutral code 128 to about 129 in sRGB (allow rounding).
Check(adobe.BgraPixels[2] is >= 128 and <= 131
    && Math.Abs(adobe.BgraPixels[2] - adobe.BgraPixels[1]) <= 1,
    "Adobe RGB neutral converted to sRGB");

var badPath = Path.Combine(output, "broken.png");
await File.WriteAllTextAsync(badPath, "not an image");
try { await decoder.DecodeAsync(badPath, CancellationToken.None); throw new Exception("Corrupt file accepted"); }
catch (Exception error) when (error is MagickException or NotSupportedException or InvalidDataException) { Check(true, "corrupt image reports failure"); }

using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { await decoder.DecodeAsync(profilePath, cancelled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { Check(true, "cancelled request"); }
var afterCancellation = await decoder.DecodeAsync(profilePath, CancellationToken.None);
Check(afterCancellation.Width == 3, "decoder usable after failure and cancellation");
try { await decoder.DecodeAsync(Path.Combine(output, "sample.pdf"), CancellationToken.None); throw new Exception("PDF accepted"); }
catch (NotSupportedException) { Check(true, "PDF excluded from image decoder"); }
Console.WriteLine($"{checks} checks passed. Fixtures: {output}");

Check(StellaViewer.Core.Zoom.Initial(200) == 100, "small images start at 100 percent");
Check(StellaViewer.Core.Zoom.Initial(25) == 25, "large images fit viewport");
Check(StellaViewer.Core.Zoom.Clamp(-100, 2.5) == 2.5, "fit below ten is zoom minimum");
Check(StellaViewer.Core.Zoom.Clamp(9000, 25) == 1000, "zoom maximum is 1000 percent");
Check(StellaViewer.Core.Zoom.Fit(4000, 2000, 1000, 1000) == 25, "aspect preserving fit");
var folderFixture = Path.Combine(output, "folder-enumeration");
Directory.CreateDirectory(folderFixture);
for (var i = 0; i < 300; i++) await File.WriteAllBytesAsync(Path.Combine(folderFixture, $"{i}.png"), []);
await File.WriteAllTextAsync(Path.Combine(folderFixture, "not-image.txt"), "ignore");
var entries = new List<FileEntry>(); var batches = 0;
await foreach (var batch in FolderBrowser.EnumerateAsync(folderFixture, CancellationToken.None)) {
    Check(batch.Length <= 128, "enumeration batch bounded"); entries.AddRange(batch); batches++;
}
Check(entries.Count == 300 && batches == 3, "enumeration includes images only");
var ordered = entries.OrderBy(e => e, FolderBrowser.NaturalComparer).ToArray();
Check(ordered[2].Name == "2.png" && ordered[10].Name == "10.png", "natural numeric sorting");
using var stopEnumeration = new CancellationTokenSource();
try {
    await foreach (var batch in FolderBrowser.EnumerateAsync(folderFixture, stopEnumeration.Token)) {
        stopEnumeration.Cancel(); stopEnumeration.Token.ThrowIfCancellationRequested();
    }
} catch (OperationCanceledException) { Check(true, "enumeration cancellation releases producer"); }
try { FolderBrowser.ValidateLocal(@"\\server\share\image.png"); throw new Exception("UNC accepted"); }
catch (NotSupportedException) { Check(true, "network disabled by default"); }
var thumbnail = await decoder.DecodeAsync(Path.Combine(output, "sample.png"), CancellationToken.None, 4);
Check(thumbnail.Width <= 4 && thumbnail.Height <= 4 && thumbnail.OriginalWidth == 8, "bounded thumbnail retains original dimensions");
Console.WriteLine($"Total: {checks} checks passed.");
var preferencesPath = Path.Combine(output, "test-preferences.json");
await File.WriteAllTextAsync(preferencesPath, "{}");
await Task.WhenAll(PreferencesStore.SaveChangeAsync(dark: true, filePath: preferencesPath),
    PreferencesStore.SaveChangeAsync(controlWheel: false, filePath: preferencesPath));
var savedPreferences = PreferencesStore.Load(preferencesPath);
Check(savedPreferences.Dark && !savedPreferences.ControlWheel, "concurrent preferences changes preserve unrelated values");
Console.WriteLine($"Final: {checks} checks passed.");
Check(StellaViewer.Core.FileSizeText.Format(0) == "0 B", "zero file size");
Check(StellaViewer.Core.FileSizeText.Format(999) == "999 B", "small file size in bytes");
Check(StellaViewer.Core.FileSizeText.Format(1000) == "1 KB", "KB boundary");
Check(StellaViewer.Core.FileSizeText.Format(1_000_000) == "1 MB", "MB boundary");
Check(StellaViewer.Core.FileSizeText.Format(1_000_000_000) == "1 GB", "GB boundary");
Check(StellaViewer.Core.FileSizeText.Format(999_999) == "1 MB", "rounded size promotes unit");
Console.WriteLine($"Final with size formatting: {checks} checks passed.");
await PreferencesStore.SaveChangeAsync(listMode: 2, filePath: preferencesPath);
var viewPreferences = PreferencesStore.Load(preferencesPath);
Check(viewPreferences.ListMode == 2 && viewPreferences.Dark && !viewPreferences.ControlWheel, "list mode persists without overwriting other preferences");
Console.WriteLine($"Final with list modes: {checks} checks passed.");
var locationsFixture = Path.Combine(output, "locations");
Directory.CreateDirectory(Path.Combine(locationsFixture, "child"));
foreach (var name in new[] { "book.zip", "book.PDF", "book.7z", "book.rar", "book.cbz", "book.cbr", "ignored.tar", "image.png" })
    await File.WriteAllBytesAsync(Path.Combine(locationsFixture, name), []);
var locationEntries = new List<LocationEntry>();
await foreach (var batch in FolderBrowser.EnumerateLocationsAsync(locationsFixture, CancellationToken.None)) locationEntries.AddRange(batch);
Check(locationEntries.Count == 7 && locationEntries.Count(e => e.IsDirectory) == 1, "locations include folders and supported containers only");
Check(locationEntries.Any(e => e.Kind == "PDF"), "PDF belongs to container list");
Check(!locationEntries.Any(e => e.Name == "image.png" || e.Name == "ignored.tar"), "images and TAR excluded from container list");
Console.WriteLine($"Final with navigation: {checks} checks passed.");
var previewFolder = Path.Combine(output, "previews");
Directory.CreateDirectory(previewFolder);
File.Copy(Path.Combine(output, "sample.png"), Path.Combine(previewFolder, "2.png"), true);
File.Copy(Path.Combine(output, "sample.jpg"), Path.Combine(previewFolder, "10.jpg"), true);
var folderPreview = await LocationPreview.DecodeAsync(previewFolder, CancellationToken.None);
Check(folderPreview.OriginalWidth == 8, "folder preview decodes direct image");
var archivePath = Path.Combine(output, "preview.cbz");
using (var archiveFile = File.Create(archivePath))
using (var zip = new System.IO.Compression.ZipArchive(archiveFile, System.IO.Compression.ZipArchiveMode.Create))
{
    var item = zip.CreateEntry("chapter/2.png");
    using var itemStream = item.Open();
    using var source = File.OpenRead(Path.Combine(output, "sample.png"));
    source.CopyTo(itemStream);
}
var archivePreview = await LocationPreview.DecodeAsync(archivePath, CancellationToken.None);
Check(archivePreview.BgraPixels.SequenceEqual(folderPreview.BgraPixels), "archive nested image preview without extraction");
using var cancelledPreview = new CancellationTokenSource();
cancelledPreview.Cancel();
try { await LocationPreview.DecodeAsync(archivePath, cancelledPreview.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { Check(true, "archive preview cancellation"); }
Console.WriteLine($"Final with previews: {checks} checks passed.");
var hiddenRoot = Path.Combine(output, "hidden-fixture");
var hiddenDirectory = Path.Combine(hiddenRoot, "private");
Directory.CreateDirectory(hiddenDirectory);
File.SetAttributes(hiddenDirectory, File.GetAttributes(hiddenDirectory) | FileAttributes.Hidden);
Check(new ViewerPreferences().HideHiddenFolders, "hidden folders default to hidden");
Check(!FolderBrowser.GetSubfolders(hiddenRoot, true).Contains(hiddenDirectory), "tree enumeration hides hidden folders");
Check(FolderBrowser.GetSubfolders(hiddenRoot, false).Contains(hiddenDirectory), "tree enumeration can show hidden folders");
var visibleLocations = new List<LocationEntry>();
await foreach (var batch in FolderBrowser.EnumerateLocationsAsync(hiddenRoot, CancellationToken.None)) visibleLocations.AddRange(batch);
Check(!visibleLocations.Any(e => e.Path == hiddenDirectory), "locations hide hidden directories by default");
await PreferencesStore.SaveChangeAsync(hideHiddenFolders: false, filePath: preferencesPath);
await PreferencesStore.SaveChangeAsync(dark: false, filePath: preferencesPath);
var withHidden = PreferencesStore.Load(preferencesPath);
Check(!withHidden.HideHiddenFolders && withHidden.ListMode == 2, "hidden-folder preference persists across unrelated updates");
File.Copy(Path.Combine(output, "sample.png"), Path.Combine(hiddenDirectory, "sample.png"), true);
Console.WriteLine($"Final with hidden folders: {checks} checks passed.");
await PreferencesStore.SaveChangeAsync(viewerToolbarPlacement: 2, filePath: preferencesPath);
await PreferencesStore.SaveChangeAsync(dark: true, filePath: preferencesPath);
var toolbarPreferences = PreferencesStore.Load(preferencesPath);
Check(toolbarPreferences.ViewerToolbarPlacement == 2 && !toolbarPreferences.HideHiddenFolders, "toolbar placement persists without overwriting hidden-folder setting");
Check(new ViewerPreferences().ViewerToolbarPlacement == 0, "viewer toolbar defaults to top");
Console.WriteLine($"Final with toolbar placement: {checks} checks passed.");
await PreferencesStore.SaveChangeAsync(sortMode: 2, restoreLastView: false, filePath: preferencesPath);
await PreferencesStore.SaveChangeAsync(listMode: 0, filePath: preferencesPath);
var restartPreferences = PreferencesStore.Load(preferencesPath);
Check(restartPreferences.SortMode == 2 && !restartPreferences.RestoreLastView && restartPreferences.ListMode == 0,
    "sort and restore preferences survive unrelated changes");
var stateRoot = Path.Combine(output, "state-tests");
Directory.CreateDirectory(stateRoot);
var layoutA = new WindowLayout(-200, 50, 1300, 850, true, 430, 240, .6, true);
var viewA = new LastView(output, Path.Combine(output, "sample.png"));
await ViewerStateStore.SaveAsync(layoutA, viewA, stateRoot);
var recovered = ViewerStateStore.Load(stateRoot);
Check(recovered.Layout == layoutA && recovered.View == viewA, "window layout and last file round trip separately");
Check(!File.ReadAllText(Path.Combine(stateRoot, "last-view.json")).Contains("Width"), "view restoration data is separate from layout");
var correctedLayout = new WindowLayout(double.NaN, double.PositiveInfinity, -1, 1, LeftPaneWidth: -100, TreeFraction: 9).Normalize();
Check(correctedLayout.Left is null && correctedLayout.Top is null && correctedLayout.Width == 800 && correctedLayout.Height == 500
    && correctedLayout.LeftPaneWidth == 280 && correctedLayout.TreeFraction == .85, "invalid window placement is bounded");
await Task.WhenAll(
    ViewerStateStore.SaveAsync(layoutA, new LastView("A"), stateRoot),
    ViewerStateStore.SaveAsync(layoutA with { Width = 1400 }, new LastView("B"), stateRoot));
var concurrentState = ViewerStateStore.Load(stateRoot);
Check((concurrentState.View.Folder == "A" && concurrentState.Layout.Width == 1300) ||
    (concurrentState.View.Folder == "B" && concurrentState.Layout.Width == 1400), "concurrent instances save a matching layout and view pair");
var explicitImage = Path.Combine(output, "sample.jpg");
Check(ViewerStateStore.ResolveStartup(viewA, explicitImage, true).Path == explicitImage, "explicit startup image takes precedence");
Check(ViewerStateStore.ResolveStartup(viewA, explicitImage, false).Path == explicitImage, "explicit startup image works with restore disabled");
Check(ViewerStateStore.ResolveStartup(viewA, null, false).Path is null, "restoration can be disabled");
var fallback = ViewerStateStore.ResolveStartup(new LastView(output, Path.Combine(output, "missing-image.png")), null, true);
Check(fallback.Path == output && fallback.Notification is not null, "missing last image falls back to parent with notification");
var absentFolder = ViewerStateStore.ResolveStartup(new LastView(Path.Combine(output, "absent-directory")), null, true);
Check(absentFolder.Path is null && absentFolder.Notification is not null, "missing parent starts empty with notification");
Check(ViewerStateStore.ResolveStartup(new LastView(@"\servershare"), null, true).Path is null, "network restoration respects current access restriction");
Console.WriteLine($"Final with startup state: {checks} checks passed.");
await PreferencesStore.SaveChangeAsync(locationListMode: 2, filePath: preferencesPath);
await PreferencesStore.SaveChangeAsync(listMode: 1, filePath: preferencesPath);
var independentLists = PreferencesStore.Load(preferencesPath);
Check(independentLists.LocationListMode == 2 && independentLists.ListMode == 1 && independentLists.SortMode == 2,
    "folder and archive list mode persists independently from image list and sort");
Check(new ViewerPreferences().LocationListMode == 1, "location list retains detail mode as default");
var metadataEntries = new List<LocationEntry>();
await foreach (var batch in FolderBrowser.EnumerateLocationsAsync(locationsFixture, CancellationToken.None)) metadataEntries.AddRange(batch);
Check(metadataEntries.Where(e => !e.IsDirectory).All(e => e.Length == 0 && e.Modified is not null),
    "archive and PDF details include physical size and date");
Check(metadataEntries.Where(e => e.IsDirectory).All(e => e.Length is null && e.Modified is not null),
    "folder details avoid recursive size calculation");
var manyLocations = Path.Combine(output, "locations-many");
for (var index = 0; index < 300; index++) Directory.CreateDirectory(Path.Combine(manyLocations, $"folder{index}"));
Console.WriteLine($"Final with location modes: {checks} checks passed.");
var sortFixture = new[] {
    new LocationEntry("/book10.cbz", "book10.cbz", false, 20, new DateTime(2026, 1, 2)),
    new LocationEntry("/book2.cbz", "book2.cbz", false, 20, new DateTime(2026, 1, 1)),
    new LocationEntry("/folder1", "folder1", true, null, new DateTime(2026, 1, 3)),
    new LocationEntry("/book1.pdf", "book1.pdf", false, 10, new DateTime(2026, 1, 2))
};
Check(FolderBrowser.SortLocations(sortFixture, 0).Select(e => e.Name).SequenceEqual(new[] { "folder1", "book1.pdf", "book2.cbz", "book10.cbz" }), "locations use natural name order");
Check(FolderBrowser.SortLocations(sortFixture, 1).Select(e => e.Name).SequenceEqual(new[] { "folder1", "book2.cbz", "book1.pdf", "book10.cbz" }), "locations sort oldest first with natural ties");
Check(FolderBrowser.SortLocations(sortFixture, 2).Select(e => e.Name).SequenceEqual(new[] { "folder1", "book1.pdf", "book2.cbz", "book10.cbz" }), "locations sort folders first then smallest files");
await PreferencesStore.SaveChangeAsync(locationSortMode: 1, filePath: preferencesPath);
await PreferencesStore.SaveChangeAsync(listMode: 0, filePath: preferencesPath);
Check(PreferencesStore.Load(preferencesPath) is { LocationSortMode: 1, SortMode: 2, LocationListMode: 2, ListMode: 0 }, "location sorting persists independently across other preference writes");
Console.WriteLine($"Final with location sorting: {checks} checks passed.");
var appearanceDefaults = new ViewerAppearance();
Check(appearanceDefaults.BackgroundMode == 0 && !appearanceDefaults.GridVisible, "appearance defaults preserve theme background without grid");
Check(ViewerAppearance.IsColor("#12aBcF") && !ViewerAppearance.IsColor("red") && !ViewerAppearance.IsColor("#12ZZ00"), "appearance accepts RGB hex colors only");
Check(new ViewerAppearance(99, "bad", true, 0, "bad", 101).Normalize() is { BackgroundMode: 0, BackgroundColor: "#202020", GridSpacing: 1, GridColor: "#FFFFFF", GridOpacity: 100 }, "invalid saved appearance values are bounded");
await Task.WhenAll(
    PreferencesStore.SaveChangeAsync(appearance: appearanceDefaults with { BackgroundMode = 2 }, previousAppearance: appearanceDefaults, filePath: preferencesPath),
    PreferencesStore.SaveChangeAsync(appearance: appearanceDefaults with { GridVisible = true, GridSpacing = 20 }, previousAppearance: appearanceDefaults, filePath: preferencesPath));
await PreferencesStore.SaveChangeAsync(dark: true, filePath: preferencesPath);
Check(PreferencesStore.Load(preferencesPath).Appearance is { BackgroundMode: 2, GridVisible: true, GridSpacing: 20 }, "concurrent appearance changes merge per field and survive unrelated settings");
using (var gridFixture = new MagickImage(new MagickColor("#808080"), 400, 300)) gridFixture.Write(Path.Combine(output, "grid.png"));
Console.WriteLine($"Final with appearance: {checks} checks passed.");
Check(StellaViewer.Core.SpreadPagination.GetPages(0, 0, 0) is null, "empty list has no spread");
Check(StellaViewer.Core.SpreadPagination.GetPages(1, 0, 0) is { First: 0, Second: null }, "one page spread has a blank partner");
Check(StellaViewer.Core.SpreadPagination.GetPages(4, 1, 0) is { First: 1, Second: 2 }
    && StellaViewer.Core.SpreadPagination.GetPages(4, 1, 1) is { First: 3, Second: 0 }, "even spread cycle can start at a selected middle page");
Check(StellaViewer.Core.SpreadPagination.GetPages(5, 1, 0) is { First: 1, Second: 2 }
    && StellaViewer.Core.SpreadPagination.GetPages(5, 1, 1) is { First: 3, Second: 4 }
    && StellaViewer.Core.SpreadPagination.GetPages(5, 1, 2) is { First: 0, Second: null }, "odd spread cycle keeps last page and blank without duplication");
Check(StellaViewer.Core.SpreadPagination.MoveGroup(5, 0, -1) == 2
    && StellaViewer.Core.SpreadPagination.MoveGroup(5, 2, 1) == 0, "spread navigation wraps backward and forward");
for (var count = 1; count <= 50; count++)
for (var anchor = 0; anchor < count; anchor++) {
    var visited = Enumerable.Range(0, StellaViewer.Core.SpreadPagination.GroupCount(count))
        .Select(i => StellaViewer.Core.SpreadPagination.GetPages(count, anchor, i)!.Value)
        .SelectMany(p => p.Second is int second ? new[] { p.First, second } : new[] { p.First }).ToArray();
    if (visited.Length != count || visited.Distinct().Count() != count || visited.Any(i => i < 0 || i >= count))
        throw new InvalidOperationException($"Spread cycle duplicates or omits pages: count={count}, anchor={anchor}");
}
Check(true, "spread cycle visits each page exactly once for all anchors in 1 to 50 pages");
Console.WriteLine($"Final with spread pagination: {checks} checks passed.");
foreach (var count in new[] { 1, 4, 5 }) {
    var directory = Path.Combine(output, $"spread-{count}"); Directory.CreateDirectory(directory);
    for (var page = 1; page <= count; page++) {
        using var fixture = new MagickImage(page % 2 == 0 ? MagickColors.Blue : MagickColors.Red,
            (uint)(100 + page * 20), (uint)(120 + page * 10));
        fixture.Write(Path.Combine(directory, $"page{page}.png"));
    }
}
var failedSpread = Path.Combine(output, "spread-failure"); Directory.CreateDirectory(failedSpread);
await File.WriteAllTextAsync(Path.Combine(failedSpread, "page1.png"), "broken image");
File.Copy(Path.Combine(output, "spread-4", "page2.png"), Path.Combine(failedSpread, "page2.png"), true);
await File.WriteAllTextAsync(Path.Combine(failedSpread, "page3.png"), "broken image");
await File.WriteAllTextAsync(Path.Combine(failedSpread, "page4.png"), "broken image");
await Task.WhenAll(PreferencesStore.SaveChangeAsync(spreadMode: true, filePath: preferencesPath),
    PreferencesStore.SaveChangeAsync(rightBinding: false, filePath: preferencesPath));
Check(PreferencesStore.Load(preferencesPath) is { SpreadMode: true, RightBinding: false, LocationSortMode: 1 }, "spread and binding changes preserve independent settings");
Check(!new ViewerPreferences().SpreadMode && new ViewerPreferences().RightBinding, "single page and right binding are initial defaults");
Console.WriteLine($"Final with spread preferences: {checks} checks passed.");
var watchFixture = Path.Combine(output, "watch-infrastructure", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(watchFixture);
var observedChanges = new System.Collections.Concurrent.ConcurrentQueue<FolderChangeBatch>();
using (var monitor = new FolderChangeMonitor(watchFixture, observedChanges.Enqueue)) {
    async Task WaitForChange(string path) {
        for (var attempt = 0; attempt < 120; attempt++) {
            if (observedChanges.Any(b => b.RescanRequired || b.Paths.Contains(path, StringComparer.OrdinalIgnoreCase))) return;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Filesystem notification timeout: " + path);
    }
    var created = Path.Combine(watchFixture, "created.png");
    File.Copy(Path.Combine(output, "sample.png"), created);
    await WaitForChange(created);
    var createdChanges = await FolderChanges.ReadAsync(watchFixture, [created], true, CancellationToken.None);
    Check(createdChanges.Single().Image is { Name: "created.png", Length: > 0 } && !createdChanges.Single().Missing,
        "native folder notification resolves new image metadata");
    while (observedChanges.TryDequeue(out _)) { }
    var renamed = Path.Combine(watchFixture, "renamed.png"); File.Move(created, renamed);
    await WaitForChange(created); await WaitForChange(renamed);
    var renamedChanges = await FolderChanges.ReadAsync(watchFixture, [created, renamed], true, CancellationToken.None);
    Check(renamedChanges.Single(c => c.Path == created).Missing && renamedChanges.Single(c => c.Path == renamed).Image is not null,
        "rename reports removed and added paths");
    File.Delete(renamed);
    Check((await FolderChanges.ReadAsync(watchFixture, [renamed], true, CancellationToken.None)).Single().Missing,
        "removed source is distinguished from unreadable metadata");
    var hidden = Path.Combine(watchFixture, "hidden"); Directory.CreateDirectory(hidden);
    File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
    Check((await FolderChanges.ReadAsync(watchFixture, [hidden], true, CancellationToken.None)).Single().Location is null
        && (await FolderChanges.ReadAsync(watchFixture, [hidden], false, CancellationToken.None)).Single().Location is { IsDirectory: true },
        "watch metadata respects hidden-folder setting");
    var outside = Path.Combine(output, "sample.png");
    Check((await FolderChanges.ReadAsync(watchFixture, [outside], true, CancellationToken.None)).Length == 0,
        "watch batches cannot introduce paths outside current folder");
    var rescan = await FolderChanges.RescanAsync(watchFixture, [renamed], true, CancellationToken.None);
    Check(rescan.Any(c => c.Path == renamed && c.Missing) && rescan.All(c => c.Image is null),
        "rescan reconciles known missing paths without reading image contents");
}
Console.WriteLine($"Final with folder monitoring: {checks} checks passed.");
Check(new LoupeSettings() is { Magnification: 2, Size: 240 }, "loupe defaults to twice current display scale and 240 DIP square");
Check(new LoupeSettings(double.NaN, 0).Normalize() is { Magnification: 2, Size: 80 }
    && new LoupeSettings(100, 2000).Normalize() is { Magnification: 16, Size: 800 }, "invalid saved loupe settings are normalized");
await Task.WhenAll(PreferencesStore.SaveChangeAsync(loupeMagnification: 3, filePath: preferencesPath),
    PreferencesStore.SaveChangeAsync(loupeSize: 320, filePath: preferencesPath));
await PreferencesStore.SaveChangeAsync(dark: false, filePath: preferencesPath);
Check(PreferencesStore.Load(preferencesPath) is { Loupe.Magnification: 3, Loupe.Size: 320, SpreadMode: true, RightBinding: false },
    "loupe size and magnification persist independently across concurrent and unrelated saves");
var legacyPreferences = Path.Combine(output, "legacy-loupe-preferences.json");
await File.WriteAllTextAsync(legacyPreferences, "{}");
Check(PreferencesStore.Load(legacyPreferences).Loupe == new LoupeSettings(), "existing preferences without loupe fields load defaults");
Console.WriteLine($"Final with loupe: {checks} checks passed.");
var keyboardDefaults = new StellaViewer.Core.KeyboardSettings();
var nextCommand = StellaViewer.Core.ViewerCommand.Next; var previousCommand = StellaViewer.Core.ViewerCommand.Previous;
Check(keyboardDefaults.For(nextCommand).Key == "RIGHT" && keyboardDefaults.For(previousCommand).Key == "LEFT"
    && keyboardDefaults.For(StellaViewer.Core.ViewerCommand.Reload).Key == "F5", "keyboard defaults retain existing navigation and reload");
var modifiedKeyboard = keyboardDefaults.Assign(nextCommand, new("j", 2));
Check(modifiedKeyboard.For(nextCommand) == new StellaViewer.Core.ShortcutGesture("J", 2)
    && keyboardDefaults.For(nextCommand).Key == "RIGHT", "keyboard assignment normalizes and leaves previous snapshot unchanged");
Check(keyboardDefaults.Conflict(nextCommand, new("left")) == previousCommand, "shortcut conflict identifies existing command");
var replacedKeyboard = keyboardDefaults.Assign(nextCommand, new("LEFT"), true);
Check(replacedKeyboard.For(nextCommand).Key == "LEFT" && replacedKeyboard.For(previousCommand).IsEmpty, "confirmed replacement unassigns previous owner");
Check(replacedKeyboard.Assign(nextCommand, new()).For(nextCommand).IsEmpty, "shortcut can be unassigned");
Check(new StellaViewer.Core.ShortcutGesture("Escape").Normalize().IsEmpty && new StellaViewer.Core.ShortcutGesture("Tab").Normalize().IsEmpty
    && new StellaViewer.Core.ShortcutGesture("F4", 1).Normalize().IsEmpty && new StellaViewer.Core.ShortcutGesture("J", 8).Normalize().IsEmpty,
    "reserved system and focus keys cannot be assigned");
var mergedKeyboard = modifiedKeyboard.MergeChanges(keyboardDefaults, keyboardDefaults.Assign(previousCommand, new("K", 2)));
Check(mergedKeyboard.For(nextCommand).Key == "J" && mergedKeyboard.For(previousCommand).Key == "K", "independent shortcut changes merge");
var rejectedConflict = false;
try { _ = modifiedKeyboard.MergeChanges(keyboardDefaults, keyboardDefaults.Assign(previousCommand, new("J", 2))); }
catch (InvalidOperationException) { rejectedConflict = true; }
Check(rejectedConflict, "concurrent shortcut collision is rejected without silent replacement");
await Task.WhenAll(
    PreferencesStore.SaveChangeAsync(keyboard: modifiedKeyboard, previousKeyboard: keyboardDefaults, filePath: preferencesPath),
    PreferencesStore.SaveChangeAsync(keyboard: keyboardDefaults.Assign(previousCommand, new("K", 2)), previousKeyboard: keyboardDefaults, filePath: preferencesPath));
await PreferencesStore.SaveChangeAsync(loupeSize: 400, filePath: preferencesPath);
var storedKeyboard = PreferencesStore.Load(preferencesPath);
Check(storedKeyboard.Keyboard.For(nextCommand).Key == "J" && storedKeyboard.Keyboard.For(previousCommand).Key == "K"
    && storedKeyboard.Loupe.Size == 400 && storedKeyboard.SpreadMode, "shared shortcut storage preserves independent keyboard and viewer changes");
Check(PreferencesStore.Load(legacyPreferences).Keyboard.For(nextCommand).Key == "RIGHT", "legacy settings retain keyboard defaults");
var multiKeys = keyboardDefaults.Add(nextCommand, new("J", 2)).Add(nextCommand, new("N"));
Check(multiKeys.AllFor(nextCommand).Count == 3 && keyboardDefaults.AllFor(nextCommand).Count == 1, "multiple shortcuts preserve defaults and original snapshot");
Check(multiKeys.Add(nextCommand, new("j", 2)).AllFor(nextCommand).Count == 3, "same-command duplicate is not added twice");
Check(multiKeys.Remove(nextCommand, new("J", 2)).AllFor(nextCommand).Select(g => g.Key).SequenceEqual(new[] { "RIGHT", "N" }), "remove only one shortcut");
var movedKey = multiKeys.Add(previousCommand, new("K")).Add(nextCommand, new("LEFT"), true);
Check(movedKey.AllFor(previousCommand).Single().Key == "K" && movedKey.AllFor(nextCommand).Count == 4, "conflict replacement preserves all other assignments");
var sameCommandMerge = multiKeys.MergeChanges(keyboardDefaults, keyboardDefaults.Add(nextCommand, new("L")));
Check(sameCommandMerge.AllFor(nextCommand).Count == 4, "concurrent additions to same command merge");
var removeAndAdd = multiKeys.MergeChanges(keyboardDefaults, keyboardDefaults.Assign(nextCommand, new()));
Check(removeAndAdd.AllFor(nextCommand).Select(g => g.Key).SequenceEqual(new[] { "J", "N" }), "concurrent removal preserves keys added by other window");
var oldKeyboardJson = """{"Bindings":{"Next":{"Key":"j","Modifiers":2},"Previous":{"Key":"","Modifiers":0}}}""";
var migratedKeys = System.Text.Json.JsonSerializer.Deserialize<StellaViewer.Core.KeyboardSettings>(oldKeyboardJson)!.Normalize();
Check(migratedKeys.AllFor(nextCommand).Single() == new StellaViewer.Core.ShortcutGesture("J", 2)
    && migratedKeys.AllFor(previousCommand).Count == 0 && migratedKeys.For(StellaViewer.Core.ViewerCommand.Reload).Key == "F5", "legacy single-key JSON migrates without losing empty or default bindings");
var multiJson = System.Text.Json.JsonSerializer.Serialize(multiKeys);
var roundTripKeys = System.Text.Json.JsonSerializer.Deserialize<StellaViewer.Core.KeyboardSettings>(multiJson)!.Normalize();
Check(roundTripKeys.AllFor(nextCommand).SequenceEqual(multiKeys.AllFor(nextCommand)) && multiJson.Contains("[{"), "multiple shortcuts JSON round trip");
var multiPreferencesPath = Path.Combine(Path.GetDirectoryName(preferencesPath)!, "multi-shortcuts.json");
await PreferencesStore.SaveChangeAsync(keyboard: multiKeys, previousKeyboard: keyboardDefaults, filePath: multiPreferencesPath);
Check(PreferencesStore.Load(multiPreferencesPath).Keyboard.AllFor(nextCommand).Count == 3, "preferences persist multiple shortcuts");

Console.WriteLine($"Final with keyboard: {checks} checks passed.");
Check(new ViewerAppearance().Normalize() is { CheckerColor1: "#F0F0F0", CheckerColor2: "#CDCDCD", CheckerSize: 12 }, "checker defaults preserve existing appearance");
Check(new ViewerAppearance(CheckerColor1: "bad", CheckerColor2: null!, CheckerSize: 0).Normalize()
    is { CheckerColor1: "#F0F0F0", CheckerColor2: "#CDCDCD", CheckerSize: 1 }
    && new ViewerAppearance(CheckerSize: 999).Normalize().CheckerSize == 256, "invalid stored checker values are normalized");
await Task.WhenAll(
    PreferencesStore.SaveChangeAsync(appearance: appearanceDefaults with { CheckerColor1 = "#123456", CheckerColor2 = "#ABCDEF" }, previousAppearance: appearanceDefaults, filePath: preferencesPath),
    PreferencesStore.SaveChangeAsync(appearance: appearanceDefaults with { CheckerSize = 28 }, previousAppearance: appearanceDefaults, filePath: preferencesPath));
Check(PreferencesStore.Load(preferencesPath).Appearance is { CheckerColor1: "#123456", CheckerColor2: "#ABCDEF", CheckerSize: 28, GridSpacing: 20 },
    "checker colors and size merge independently and preserve grid settings");
Check(PreferencesStore.Load(legacyPreferences).Appearance.CheckerSize == 12, "legacy preferences retain checker defaults");
Console.WriteLine($"Final with checker: {checks} checks passed.");
Check(!PreferencesStore.Load(legacyPreferences).SoftwareRendering, "legacy preferences default to automatic rendering");
await Task.WhenAll(
    PreferencesStore.SaveChangeAsync(softwareRendering: true, filePath: preferencesPath),
    PreferencesStore.SaveChangeAsync(loupeSize: 360, filePath: preferencesPath));
Check(PreferencesStore.Load(preferencesPath) is { SoftwareRendering: true, Loupe.Size: 360, Appearance.CheckerSize: 28 },
    "rendering preference merges without overwriting other settings");
await PreferencesStore.SaveChangeAsync(softwareRendering: false, filePath: preferencesPath);
Check(!PreferencesStore.Load(preferencesPath).SoftwareRendering, "automatic rendering can be restored and saved");
Console.WriteLine($"Final with rendering preference: {checks} checks passed.");
Check(NetworkAccess.IsNetworkPath(@"\\stella-test.invalid\share\image.png")
    && NetworkAccess.IsNetworkPath(@"\\?\UNC\stella-test.invalid\share\image.png")
    && !NetworkAccess.IsNetworkPath(Path.GetFullPath(preferencesPath)), "network policy distinguishes UNC, extended UNC and local paths without contacting server");
NetworkAccess.Validate(@"\\stella-test.invalid\share\image.png", true);
Check(true, "explicit network permission accepts UNC at policy layer without file access");
try { NetworkAccess.Validate(@"\\?\UNC\stella-test.invalid\share\image.png"); throw new Exception("Extended UNC accepted"); }
catch (NotSupportedException) { Check(true, "extended UNC cannot bypass disabled network access"); }
Check(!PreferencesStore.Load(legacyPreferences).AllowNetwork, "legacy settings disable network access");
await Task.WhenAll(PreferencesStore.SaveChangeAsync(allowNetwork: true, filePath: preferencesPath),
    PreferencesStore.SaveChangeAsync(loupeSize: 380, filePath: preferencesPath));
Check(PreferencesStore.Load(preferencesPath) is { AllowNetwork: true, Loupe.Size: 380, SoftwareRendering: false },
    "network permission persists without overwriting concurrent settings");
var allowedImages = new List<FileEntry>();
await foreach (var batch in FolderBrowser.EnumerateAsync(output, CancellationToken.None, true)) allowedImages.AddRange(batch);
Check(allowedImages.Count > 0, "network-enabled enumeration retains ordinary local browsing");
var blockedUnc = @"\\stella-test.invalid\share";
try { await LocationPreview.DecodeAsync(blockedUnc, CancellationToken.None); throw new Exception("UNC preview accepted"); }
catch (NotSupportedException) { Check(true, "disabled network previews reject path before file access"); }
try { await FolderChanges.ReadAsync(blockedUnc, [], true, CancellationToken.None); throw new Exception("UNC changes accepted"); }
catch (NotSupportedException) { Check(true, "disabled network change processing rejects path before file access"); }
Console.WriteLine($"Final with network access: {checks} checks passed.");
checks += await ImageCacheChecks.RunAsync(output);
Console.WriteLine($"Final with image cache: {checks} checks passed.");
checks += await ArchiveChecks.RunAsync(output);
Console.WriteLine($"Final with archive browsing: {checks} checks passed.");
checks += await PdfChecks.RunAsync(output);
Console.WriteLine($"Final with PDF browsing: {checks} checks passed.");
checks += await ArchivePdfChecks.RunAsync(output);
Console.WriteLine($"Final with archive PDF browsing: {checks} checks passed.");
checks += await NestedArchiveChecks.RunAsync(output);
Console.WriteLine($"Final with nested archive browsing: {checks} checks passed.");

checks += await AnimationChecks.RunAsync(Path.Combine(output, "animation"));
Console.WriteLine($"Final with animation decoding: {checks} checks passed.");
checks += await PsdChecks.RunAsync(Path.Combine(output, "psd"));
Console.WriteLine($"Final with PSD decoding: {checks} checks passed.");
checks += await PreferencesIoChecks.RunAsync(output);
Console.WriteLine($"Final with concurrent preferences IO: {checks} checks passed.");
checks += await ClipChecks.RunAsync(Path.Combine(output,"clip"));
Console.WriteLine($"Final with CLIP stored previews: {checks} checks passed.");
checks += await FavoritesChecks.RunAsync(output);
Console.WriteLine($"Final with favorites: {checks} checks passed.");
checks += await FolderOpeningChecks.RunAsync(output);
Console.WriteLine($"Final with folder opening: {checks} checks passed.");
checks += await DirectoryShortcutChecks.RunAsync(output);
checks += await RegistrationOrderChecks.RunAsync(output);
Console.WriteLine($"Final with directory shortcuts: {checks} checks passed.");
checks += await RecentLocationChecks.RunAsync(output);
Console.WriteLine($"Final with recent locations: {checks} checks passed.");
checks += await ThumbnailSizingChecks.RunAsync(output);
Console.WriteLine($"Final with thumbnail sizing: {checks} checks passed.");
checks += await SendCopyChecks.RunAsync(output);
Console.WriteLine($"Final with send copies: {checks} checks passed.");
checks += await TrayPreferenceChecks.RunAsync(output);
Console.WriteLine($"Final with tray preferences: {checks} checks passed.");
checks += await MetadataChecks.RunAsync(output);
Console.WriteLine($"Final with image metadata: {checks} checks passed.");
checks += await MouseChecks.RunAsync(output);
Console.WriteLine($"Final with mouse settings: {checks} checks passed.");
checks += await PdfCacheChecks.RunAsync(output);
Console.WriteLine($"Final with PDF cache: {checks} checks passed.");
checks += await ArchiveCacheChecks.RunAsync(output);
Console.WriteLine($"Final with archive cache: {checks} checks passed.");
checks += await SecurityRecoveryChecks.RunAsync(output);
checks += await HardeningChecks.RunAsync(output);
checks += await ArchiveIsolationChecks.RunAsync(output);
Console.WriteLine($"Final with security and recovery: {checks} checks passed.");
checks += await DataToolsChecks.RunAsync(output);
Console.WriteLine($"Final with data tools: {checks} checks passed.");
checks += await UiSizeChecks.RunAsync(output);
Console.WriteLine($"Final with UI sizing: {checks} checks passed.");
checks += await ListSettingsChecks.RunAsync(output);
Console.WriteLine($"Final with list settings: {checks} checks passed.");
checks += await SlideshowChecks.RunAsync(output);
Console.WriteLine($"Final with slideshow: {checks} checks passed.");
checks += await RequestedFeaturesChecks.RunAsync(Path.Combine(output, "requested-features"));
Console.WriteLine($"Final with sorting, archive compatibility and fit settings: {checks} checks passed.");
