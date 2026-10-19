using System.IO;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private readonly DecodedImageCache imageCache;
    private ImageCacheSettings cacheSettings = new();
    private CancellationTokenSource? prefetch;
    private Task prefetchWork = Task.CompletedTask;
    private void CancelPrefetch() { prefetch?.Cancel(); prefetch?.Dispose(); prefetch = null; }
    private async Task ApplyCacheSettingsAsync(ImageCacheSettings? value)
    {
        CancelPrefetch(); cacheSettings = (value ?? new()).Normalize(); imageCache.Configure(cacheSettings);
        await imageCache.MaintainDiskAsync();
        if (imageCache.Warning is string warning) model.Status = warning;
        StartPrefetch();
    }
    private void StartPrefetch()
    {
        CancelPrefetch();
        if (closed || loading || decoded is null || cacheSettings.PrefetchPages == 0 || cacheSettings.MemoryMiB == 0
            || model.Entries.Count < 2 || !CanReadCurrentFolder() || display.IsCancellationRequested || browse.IsCancellationRequested) return;
        var selected = ImageList.SelectedIndex; if (selected < 0) return;
        var paths = new List<string>(); var seen = ProtectedDisplayPaths();
        var count = model.Entries.Count;
        var distance = Math.Min(cacheSettings.PrefetchPages, count - 1);
        for (var step = 1; step <= distance; step++) {
            foreach (var index in new[] { (int)(((long)selected + step) % count), (int)(((long)selected - step + count) % count) })
                if (seen.Add(model.Entries[index].Path)) paths.Add(model.Entries[index].Path);
        }
        prefetch = CancellationTokenSource.CreateLinkedTokenSource(display.Token, browse.Token);
        prefetchWork = PrefetchAsync(paths, prefetch.Token, model.AllowNetwork, pdfSession, archiveSession);
    }
    private async Task PrefetchAsync(IEnumerable<string> paths, CancellationToken token, bool allowed, PdfSession? pdf, ArchiveSession? archive)
    {
        try {
            // One request at a time; navigation cancels it before a foreground decode queues.
            foreach (var path in paths) {
                token.ThrowIfCancellationRequested();
                try {
                    if (PdfLocation.Parse(path) is { Page: int page } location) {
                        if (pdf is not null && ArchiveLocation.SameSource(pdf.SourcePath, location.DocumentPath))
                            await imageCache.PrefetchPdfAsync(pdf, page, token, allowed);
                    }
                    else if (ArchiveLocation.Parse(path) is { } item) {
                        if (archive is not null && ArchiveLocation.SameSource(archive.SourcePath, item.ArchivePath))
                            await imageCache.PrefetchArchiveAsync(archive, item, token, allowed);
                    }
                    else await imageCache.PrefetchAsync(path, token, allowed);
                }
                catch (Exception error) when (error is not OperationCanceledException) { /* Speculation must not replace the current image with an error. */ }
            }
        }
        catch (OperationCanceledException) { }
    }
}
