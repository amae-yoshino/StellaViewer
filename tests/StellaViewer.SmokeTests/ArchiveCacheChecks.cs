using System.IO.Compression;
using System.Security.Cryptography;
using ImageMagick;
using StellaViewer.Infrastructure;

internal static class ArchiveCacheChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
        async Task Reject<T>(Func<Task> action, string label) where T : Exception {
            try { await action(); } catch (T) { Check(true, label); return; } throw new Exception(label + " was accepted");
        }
        var root = Path.Combine(Path.GetFullPath(output), "archive-cache"); Directory.CreateDirectory(root);
        byte[] Image(MagickColor color) { using var image = new MagickImage(color, 512, 256); return image.ToByteArray(MagickFormat.Png); }
        var red = Image(MagickColors.Red); var green = Image(MagickColors.Lime); var blue = Image(MagickColors.Blue);
        var book = Path.Combine(root, "book.zip");
        using (var zip = new ZipArchive(File.Create(book), ZipArchiveMode.Create)) {
            foreach (var entry in new[] { ("1.png", red), ("2.png", green), ("3.png", blue) }) {
                using var stream = zip.CreateEntry(entry.Item1).Open(); stream.Write(entry.Item2);
            }
        }
        var hash = SHA256.HashData(File.ReadAllBytes(book));
        var disk = Path.Combine(root, "disk-" + Guid.NewGuid().ToString("N")); var cache = new DecodedImageCache(new MagickImageDecoder(), disk); cache.Configure(new(8, 8, 2));
        using var archive = await ArchiveSession.OpenAsync(book, default);
        var first = new ArchiveLocation(book, "1.png", false); var second = new ArchiveLocation(book, "2.png", false);
        var image1 = await cache.GetArchiveAsync(archive, first, default); var repeated = await cache.GetArchiveAsync(archive, first, default);
        Check(ReferenceEquals(image1, repeated) && cache.Decodes == 1 && cache.MemoryHits == 1, "Archive foreground display reuses decoded memory pixels");
        using (var alias = await ArchiveSession.OpenAsync(book.ToUpperInvariant(), default)) {
            await cache.GetArchiveAsync(alias, first with { ArchivePath = book.ToUpperInvariant() }, default);
            Check(cache.Decodes == 1 && cache.MemoryHits == 2, "Archive cache normalizes physical Windows path casing");
        }
        var image2 = await cache.GetArchiveAsync(archive, second, default);
        Check(image1.BgraPixels[2] > 240 && image2.BgraPixels[1] > 240 && cache.MemoryBytes == 2 * 512 * 256 * 4, "Archive entries remain distinct within common pixel budget");
        cache.Configure(new(0, 8, 0)); var diskImage = await cache.GetArchiveAsync(archive, first, default);
        Check(cache.MemoryCount == 0 && cache.DiskHits == 1 && diskImage.BgraPixels.SequenceEqual(image1.BgraPixels), "Archive disk cache preserves BGRA while memory cache is disabled");
        using (var reopened = await ArchiveSession.OpenAsync(book, default)) {
            await cache.GetArchiveAsync(reopened, first, default);
            Check(cache.Decodes == 2 && cache.DiskHits == 2, "Reopened archive reuses persistent image cache");
        }
        await cache.InvalidateAsync(archive.Images("").Select(entry => entry.Path)); await cache.GetArchiveAsync(archive, first, default);
        Check(cache.Decodes == 3, "F5-style invalidation forces archive decode without source timestamp changes");
        File.WriteAllBytes(Directory.GetFiles(disk, "*.svcache").Single(), [1, 2, 3]); await cache.GetArchiveAsync(archive, first, default);
        Check(cache.Decodes == 4, "Corrupt archive image cache falls back to entry decoding");
        var prefetch = new DecodedImageCache(new MagickImageDecoder()); prefetch.Configure(new(1, 0, 2));
        Check(await prefetch.PrefetchArchiveAsync(archive, first, default) && await prefetch.PrefetchArchiveAsync(archive, second, default)
            && !await prefetch.PrefetchArchiveAsync(archive, new(book, "3.png", false), default), "Archive prefetch probes pixels before exceeding available memory");
        var decodes = prefetch.Decodes; await prefetch.GetArchiveAsync(archive, second, default);
        Check(prefetch.Decodes == decodes && prefetch.MemoryBytes <= 1024 * 1024, "Archive page turn uses prefetched entry within capacity");
        prefetch.Configure(new(0, 0, 2));
        Check(!await prefetch.PrefetchArchiveAsync(archive, first, default), "Memory zero disables archive speculative decode");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Reject<OperationCanceledException>(() => cache.GetArchiveAsync(archive, first, canceled.Token), "Archive cached lookup honors cancellation");
        await Reject<NotSupportedException>(() => cache.GetArchiveAsync(archive, new(book), default), "Archive folder cannot be treated as cached image item");
        await Reject<InvalidOperationException>(() => cache.GetArchiveAsync(archive, first with { ArchivePath = book + ".other.zip" }, default), "Cache refuses an entry belonging to a different archive");
        Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(book))), "Archive cache and probes leave original bytes unchanged");
        File.SetLastWriteTimeUtc(book, File.GetLastWriteTimeUtc(book).AddSeconds(2));
        await Reject<IOException>(() => cache.GetArchiveAsync(archive, first, default), "Changed archive is rejected before returning cached pixels");
        using (var changed = await ArchiveSession.OpenAsync(book, default)) { await cache.GetArchiveAsync(changed, first, default); Check(cache.Decodes == 5, "New archive source revision cannot reuse old generation"); }
        archive.Dispose();
        await Reject<ObjectDisposedException>(() => cache.GetArchiveAsync(archive, first, default), "Disposed archive cannot retrieve cached pixels");
        var outer = Path.Combine(root, "outer.zip");
        using (var zip = new ZipArchive(File.Create(outer), ZipArchiveMode.Create)) zip.CreateEntryFromFile(book, "inner.zip");
        var inner = new ArchiveLocation(outer, "inner.zip", false); var nested = new ArchiveLocation(inner.Identity, "2.png", false);
        var nestedCache = new DecodedImageCache(new MagickImageDecoder(), Path.Combine(root, "nested-disk-" + Guid.NewGuid().ToString("N"))); nestedCache.Configure(new(0, 8, 0));
        for (var i = 0; i < 2; i++) {
            await using var store = new ArchiveTemporaryStore(Path.Combine(root, "temp-" + Guid.NewGuid().ToString("N")));
            using (var session = await ArchiveSession.OpenAsync(inner.Identity, default, store: store)) await nestedCache.GetArchiveAsync(session, nested, default);
            Check(store.UsedBytes == 0, "Nested image cache does not keep archive temporary input alive");
        }
        Check(nestedCache.Decodes == 1 && nestedCache.DiskHits == 1, "Nested archive image disk cache survives re-extraction paths");
        var formats = Path.Combine(root, "formats.zip");
        using (var zip = new ZipArchive(File.Create(formats), ZipArchiveMode.Create)) {
            zip.CreateEntryFromFile(Path.Combine(output, "psd", "rgb-16-rle.psd"), "1.psd");
            zip.CreateEntryFromFile(Path.Combine(output, "clip", "2-small.clip"), "2.clip");
            foreach (var entry in new[] { ("UP.png", red), ("up.png", blue), ("broken.png", new byte[] { 1, 2, 3 }) }) { using var stream = zip.CreateEntry(entry.Item1).Open(); stream.Write(entry.Item2); }
        }
        using var formatArchive = await ArchiveSession.OpenAsync(formats, default);
        var formatCache = new DecodedImageCache(new MagickImageDecoder(), Path.Combine(root, "format-disk-" + Guid.NewGuid().ToString("N"))); formatCache.Configure(new(8, 8, 2));
        Check(await formatCache.PrefetchArchiveAsync(formatArchive, new(formats, "1.psd", false), default), "PSD stream header enables bounded archive prefetch");
        Check(await formatCache.PrefetchArchiveAsync(formatArchive, new(formats, "2.clip", false), default), "CLIP stored PNG dimensions enable archive prefetch");
        formatCache.Configure(new(0, 8, 0)); var clip = await formatCache.GetArchiveAsync(formatArchive, new(formats, "2.clip", false), default);
        Check(clip.Width == 8 && clip.OriginalWidth == 1600 && formatCache.DiskHits == 1, "Archive CLIP disk hit retains original canvas dimensions for shrink notice");
        var upper = await formatCache.GetArchiveAsync(formatArchive, new(formats, "UP.png", false), default);
        var lower = await formatCache.GetArchiveAsync(formatArchive, new(formats, "up.png", false), default);
        Check(upper.BgraPixels[2] > 240 && lower.BgraPixels[0] > 240, "Case-sensitive archive entry names never share cache pixels");
        await Reject<Exception>(() => formatCache.GetArchiveAsync(formatArchive, new(formats, "broken.png", false), default), "Broken archive item is not cached as a successful image");
        Check((await formatCache.GetArchiveAsync(formatArchive, new(formats, "up.png", false), default)).BgraPixels[0] > 240, "Archive cache remains usable after a failed entry");
        return count;
    }
}
