using System.IO.Compression;
using StellaViewer.Infrastructure;

internal static class PdfCacheChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS: " + name); }
        async Task Reject<T>(Func<Task> operation, string name) where T : Exception {
            try { await operation(); } catch (T) { Check(true, name); return; } throw new Exception(name + " was accepted");
        }
        var root = Path.Combine(Path.GetFullPath(output), "pdf-cache", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "pages.pdf"); File.Copy(Path.Combine(output, "pdf-checks", "pages.pdf"), path);
        var disk = Path.Combine(root, "disk"); var cache = new DecodedImageCache(new MagickImageDecoder(), disk); cache.Configure(new(8, 8, 2));
        using var pdf = await PdfSession.OpenAsync(path, default);
        var first = await cache.GetPdfAsync(pdf, 1, default); var again = await cache.GetPdfAsync(pdf, 1, default);
        Check(ReferenceEquals(first, again) && cache.Decodes == 1 && cache.MemoryHits == 1, "PDF repeat display uses shared decoded memory cache");
        using (var alias = await PdfSession.OpenAsync(path.ToUpperInvariant(), default)) {
            var aliasImage = await cache.GetPdfAsync(alias, 1, default);
            Check(ReferenceEquals(first, aliasImage) && cache.Decodes == 1, "Physical PDF cache ignores Windows path casing without folding archive entry names");
        }
        var second = await cache.GetPdfAsync(pdf, 2, default);
        Check(first.BgraPixels[2] > 240 && second.BgraPixels[1] > 240 && cache.Decodes == 2, "PDF cache keys distinguish pages and retain straight BGRA colors");
        Check(cache.MemoryBytes == first.BgraPixels.LongLength + second.BgraPixels.LongLength, "PDF pixels count toward existing common memory budget");
        cache.Configure(new(0, 8, 2));
        var diskImage = await cache.GetPdfAsync(pdf, 1, default);
        Check(cache.MemoryCount == 0 && cache.DiskHits == 1 && diskImage.BgraPixels.SequenceEqual(first.BgraPixels), "PDF disk hit preserves pixels with memory caching disabled");
        var reopenedCache = new DecodedImageCache(new MagickImageDecoder(), disk); reopenedCache.Configure(new(0, 8, 0));
        using (var reopened = await PdfSession.OpenAsync(path, default)) {
            await reopenedCache.GetPdfAsync(reopened, 1, default);
            Check(reopenedCache.Decodes == 0 && reopenedCache.DiskHits == 1, "Reopening a physical PDF reuses persistent page cache");
        }
        await reopenedCache.InvalidateAsync(pdf.Pages().Select(page => page.Path)); await reopenedCache.GetPdfAsync(pdf, 1, default);
        Check(reopenedCache.Decodes == 1 && reopenedCache.DiskHits == 1, "Explicit PDF invalidation bypasses disk pages even when original timestamps are unchanged");
        var damaged = Directory.GetFiles(disk, "*.svcache").Single(); File.WriteAllBytes(damaged, [1, 2, 3]);
        await reopenedCache.GetPdfAsync(pdf, 1, default);
        Check(reopenedCache.Decodes == 2, "Corrupt PDF page cache is discarded and rendered again");
        reopenedCache.Configure(new(0, 0, 0)); await reopenedCache.MaintainDiskAsync();
        await reopenedCache.GetPdfAsync(pdf, 1, default);
        Check(reopenedCache.Decodes == 3 && Directory.GetFiles(disk).Length == 0, "Zero disk capacity disables PDF persistence and removes old cache entries");
        var prefetch = new DecodedImageCache(new MagickImageDecoder()); prefetch.Configure(new(1, 0, 2));
        Check(await prefetch.PrefetchPdfAsync(pdf, 1, default) && await prefetch.PrefetchPdfAsync(pdf, 2, default)
            && !await prefetch.PrefetchPdfAsync(pdf, 3, default), "PDF prefetch checks full raster bytes before exceeding free memory budget");
        Check(prefetch.MemoryCount == 2 && prefetch.MemoryBytes <= 1024 * 1024, "PDF speculative memory stays within configured capacity");
        var decodes = prefetch.Decodes; await prefetch.GetPdfAsync(pdf, 2, default);
        Check(prefetch.Decodes == decodes && prefetch.MemoryHits == 1, "Foreground PDF navigation reuses prefetched page without rendering");
        prefetch.Configure(new(0, 0, 2));
        Check(!await prefetch.PrefetchPdfAsync(pdf, 1, default) && prefetch.MemoryCount == 0, "Memory zero stops PDF prefetch independently of requested distance");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Reject<OperationCanceledException>(() => cache.GetPdfAsync(pdf, 1, canceled.Token), "Cached PDF request honors cancellation before lookup");
        await Reject<ArgumentOutOfRangeException>(() => cache.GetPdfAsync(pdf, 0, default), "PDF cache never accepts an invalid page number");
        pdf.Dispose();
        await Reject<ObjectDisposedException>(() => cache.GetPdfAsync(pdf, 1, default), "Cached PDF pixels do not bypass session disposal");
        using (var changed = await PdfSession.OpenAsync(path, default)) {
            cache.Configure(new(8, 0, 0)); await cache.GetPdfAsync(changed, 1, default);
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
            await Reject<IOException>(() => cache.GetPdfAsync(changed, 1, default), "Changed original PDF is rejected even when old page is cached");
        }
        var beforeReopen = cache.Decodes;
        using (var changed = await PdfSession.OpenAsync(path, default)) await cache.GetPdfAsync(changed, 1, default);
        Check(cache.Decodes == beforeReopen + 1, "New PDF source revision cannot hit earlier page cache");
        var book = Path.Combine(root, "book.zip");
        using (var zip = ZipFile.Open(book, ZipArchiveMode.Create)) zip.CreateEntryFromFile(path, "folder/book.pdf");
        var archiveDisk = Path.Combine(root, "archive-disk"); var archivedCache = new DecodedImageCache(new MagickImageDecoder(), archiveDisk); archivedCache.Configure(new(0, 8, 0));
        var location = new ArchiveLocation(book, "folder/book.pdf", false);
        await using (var store = new ArchiveTemporaryStore(Path.Combine(root, "temp"))) {
            using var archive = await ArchiveSession.OpenAsync(book, default);
            using (var document = await PdfSession.OpenArchiveAsync(archive, location, store, default)) {
                var page = await archivedCache.GetPdfAsync(document, 2, default);
                Check(page.BgraPixels[1] > 240 && archivedCache.Decodes == 1, "Archive PDF page cache uses logical page identity");
            }
            Check(store.UsedBytes == 0, "PDF cache does not retain archive input temporary files");
            using (var document = await PdfSession.OpenArchiveAsync(archive, location, store, default)) {
                await archivedCache.GetPdfAsync(document, 2, default);
                Check(archivedCache.DiskHits == 1 && archivedCache.Decodes == 1, "Re-extracting same archive PDF reuses disk page despite different temporary timestamp");
                File.SetLastWriteTimeUtc(book, File.GetLastWriteTimeUtc(book).AddSeconds(2));
                await Reject<IOException>(() => archivedCache.GetPdfAsync(document, 2, default), "Archive revision is validated before cached PDF lookup");
            }
        }
        await using (var store = new ArchiveTemporaryStore(Path.Combine(root, "temp2"))) {
            using var archive = await ArchiveSession.OpenAsync(book, default);
            using var document = await PdfSession.OpenArchiveAsync(archive, location, store, default);
            await archivedCache.GetPdfAsync(document, 2, default);
            Check(archivedCache.Decodes == 2, "Changed outer archive produces new PDF page cache generation");
        }
        var outer = Path.Combine(root, "outer.zip");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create)) zip.CreateEntryFromFile(book, "inner.zip");
        var inner = new ArchiveLocation(outer, "inner.zip", false);
        async Task ReadNested(string temporary) {
            await using var store = new ArchiveTemporaryStore(Path.Combine(root, temporary));
            using var archive = await ArchiveSession.OpenAsync(inner.Identity, default, store: store);
            using var document = await PdfSession.OpenArchiveAsync(archive, new(inner.Identity, "folder/book.pdf", false), store, default);
            await archivedCache.GetPdfAsync(document, 2, default);
        }
        await ReadNested("nested1");
        Check(archivedCache.Decodes == 3, "Nested PDF identity is distinct from same-named document in outer physical archive");
        var diskHits = archivedCache.DiskHits; await ReadNested("nested2");
        Check(archivedCache.Decodes == 3 && archivedCache.DiskHits == diskHits + 1, "Nested PDF disk key remains stable across re-extraction of archive chain");
        return count;
    }
}
