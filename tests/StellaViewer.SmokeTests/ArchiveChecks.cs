using System.IO.Compression;
using System.Security.Cryptography;
using System.Formats.Tar;
using StellaViewer.Infrastructure;

internal static class ArchiveChecks
{
    public static async Task<int> RunAsync(string output)
    {
        var checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
        async Task Reject<T>(Func<Task> action, string label) where T : Exception {
            try { await action(); } catch (T) { Check(true, label); return; }
            throw new Exception(label + " did not reject");
        }
        var root = Path.Combine(output, "archive-checks"); Directory.CreateDirectory(root);
        var source = await File.ReadAllBytesAsync(Path.Combine(output, "sample.png"));
        var archivePath = Path.Combine(root, "book.cbz");
        using (var zip = new ZipArchive(File.Create(archivePath), ZipArchiveMode.Create)) {
            foreach (var key in new[] { "10.png", "2.png", "日本語/10.png", "日本語/2.png", "日本語/deep/1.png" }) {
                using var entry = zip.CreateEntry(key).Open(); entry.Write(source);
            }
            foreach (var key in new[] { "nested.zip", "document.pdf", "notes.txt", "broken.png", "empty/" }) {
                using var entry = zip.CreateEntry(key).Open(); if (!key.EndsWith('/')) entry.Write([1, 2, 3]);
            }
        }
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(archivePath));
        var session = await ArchiveSession.OpenAsync(archivePath, default);
        var rootLocation = new ArchiveLocation(archivePath);
        Check(ArchiveLocation.Parse(rootLocation.Identity) == rootLocation && ArchiveLocation.Parse(archivePath) is null,
            "archive identities roundtrip separately from physical paths");
        var page = new ArchiveLocation(archivePath, "日本語/2.png", false);
        Check(ArchiveLocation.Parse(page.Identity) == page && page.Folder.EntryPath == "日本語"
            && ArchiveLocation.PhysicalPath(page.Identity) == archivePath && !page.Description.Contains("stella-archive:"),
            "Unicode archive identity preserves folder and human-readable description");
        Check(session.Images("").Select(e => e.Name).Order().SequenceEqual(new[] { "10.png", "2.png", "broken.png" }.Order()),
            "archive root images exclude subfolder images");
        Check(session.Images("日本語").OrderBy(e => e, FolderBrowser.NaturalComparer).Select(e => e.Name).SequenceEqual(new[] { "2.png", "10.png" }),
            "archive subfolder images use natural order without flattening descendants");
        Check(session.Locations("").Select(e => e.Name).Order().SequenceEqual(new[] { "日本語", "nested.zip", "document.pdf", "empty" }.Order())
            && session.Locations("").Single(e => e.Name == "document.pdf").Kind == "PDF",
            "archive upper list includes implicit folders, nested archive, PDF and explicit empty folder");
        Check(session.ContainsFolder("empty") && session.Images("empty").Length == 0 && session.Locations("日本語").Single().Name == "deep",
            "empty and deeper archive folders retain separate hierarchy");
        var image = await session.DecodeAsync(page, default);
        Check(image.Width == 8 && image.Height == 6 && image.BgraPixels[2] > 200, "archive image stream decodes BGRA without extracting files");
        var preview = await session.DecodeAsync(page.Folder, default, 96);
        Check(preview.Width == 8 && preview.Height == 6, "archive folder preview decodes direct natural-first image");
        var parallel = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => session.DecodeAsync(page, default)));
        Check(parallel.All(i => i.Width == 8), "concurrent archive thumbnail and image requests remain valid");
        var hashAfter = SHA256.HashData(await File.ReadAllBytesAsync(archivePath));
        Check(hash.SequenceEqual(hashAfter)
            && Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length == 0, "archive browsing leaves source unchanged without temporary images");
        Check(ViewerStateStore.ResolveStartup(new(rootLocation.Identity, page.Identity), null, true).Path == page.Identity,
            "startup resolver retains last archive page");
        Check(ViewerStateStore.ResolveStartup(new(rootLocation.Identity, page.Identity), Path.Combine(output, "sample.png"), true).Path == Path.Combine(output, "sample.png"),
            "explicit startup image overrides restored archive page");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Reject<OperationCanceledException>(() => ArchiveSession.OpenAsync(archivePath, canceled.Token), "archive enumeration honors pre-cancellation");
        await Reject<OperationCanceledException>(() => session.DecodeAsync(page, canceled.Token), "archive decode honors pre-cancellation");
        await Reject<NotSupportedException>(() => ArchiveSession.OpenAsync(@"\\stella-test.invalid\share\book.zip", default),
            "archive network policy rejects UNC before file access");
        await Reject<NotSupportedException>(() => session.DecodeAsync(new(archivePath, "document.pdf", false), default),
            "PDF cannot be silently decoded as an archive image");
        await Reject<Exception>(() => session.DecodeAsync(new(archivePath, "broken.png", false), default), "broken archive image fails without blocking next page");
        Check((await session.DecodeAsync(page, default)).Width == 8, "archive decoder recovers after broken page");
        foreach (var key in new[] { "../escape.png", "/root.png", "C:/root.png", "folder/../escape.png" }) {
            var unsafePath = Path.Combine(root, "unsafe.zip");
            using (var zip = new ZipArchive(File.Create(unsafePath), ZipArchiveMode.Create)) { using var entry = zip.CreateEntry(key).Open(); entry.Write(source); }
            await Reject<InvalidDataException>(() => ArchiveSession.OpenAsync(unsafePath, default), "archive rejects unsafe entry " + key);
        }
        var duplicatePath = Path.Combine(root, "duplicate.zip");
        using (var zip = new ZipArchive(File.Create(duplicatePath), ZipArchiveMode.Create)) {
            foreach (var key in new[] { "same.png", "same.png" }) { using var entry = zip.CreateEntry(key).Open(); entry.Write(source); }
        }
        await Reject<InvalidDataException>(() => ArchiveSession.OpenAsync(duplicatePath, default), "duplicate archive file keys are rejected rather than selected ambiguously");
        var tarPath = Path.Combine(root, "disguised.zip");
        using (var tar = new TarWriter(File.Create(tarPath))) tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "page.png") { DataStream = new MemoryStream(source) });
        await Reject<NotSupportedException>(() => ArchiveSession.OpenAsync(tarPath, default), "TAR renamed to ZIP remains unsupported");
        var mutable = Path.Combine(root, "mutable.zip"); File.Copy(archivePath, mutable, true);
        var changing = await ArchiveSession.OpenAsync(mutable, default);
        await File.AppendAllTextAsync(mutable, "changed");
        await Reject<IOException>(() => changing.DecodeAsync(new(mutable, "2.png", false), default), "changed archive snapshot requests reload before decoding");
        var missing = new ArchiveLocation(Path.Combine(root, "missing.zip"));
        var fallback = ViewerStateStore.ResolveStartup(new(missing.Identity), null, true);
        Check(fallback.Path == root && fallback.Notification is not null, "missing restored archive falls back to existing physical parent");
        await Reject<InvalidDataException>(() => Task.Run(() => ArchiveLocation.Parse("stella-archive:invalid")), "corrupt persisted archive identity is rejected");
        return checks;
    }
}
