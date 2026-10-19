using System.IO.Compression;
using System.Security.Cryptography;
using StellaViewer.Infrastructure;

internal static class NestedArchiveChecks
{
    public static async Task<int> RunAsync(string output)
    {
        var count = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
        async Task Reject<T>(Func<Task> action, string label) where T : Exception {
            try { await action(); } catch (T) { Check(true, label); return; }
            throw new Exception(label + " was accepted");
        }
        var root = Path.Combine(output, "nested-checks"); Directory.CreateDirectory(root);
        var png = await File.ReadAllBytesAsync(Path.Combine(output, "sample.png"));
        var pdf = await File.ReadAllBytesAsync(Path.Combine(output, "pdf-checks", "pages.pdf"));
        byte[] Zip(params (string Name, byte[] Data)[] files) {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
                foreach (var file in files) { using var entry = archive.CreateEntry(file.Name).Open(); entry.Write(file.Data); }
            return memory.ToArray();
        }
        var leaf = Zip(("sub/2.png", png), ("sub/10.png", png), ("sub/book.pdf", pdf));
        var middle = Zip(("children/leaf.cbz", leaf), ("1.png", png));
        var path = Path.Combine(root, "outer.zip");
        await File.WriteAllBytesAsync(path, Zip(("books/inner.zip", middle), ("root.png", png), ("bad.zip", new byte[] { 1, 2, 3 })));
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var innerItem = new ArchiveLocation(path, "books/inner.zip", false);
        var leafItem = new ArchiveLocation(innerItem.Identity, "children/leaf.cbz", false);
        var image = new ArchiveLocation(leafItem.Identity, "sub/10.png", false);
        Check(ArchiveLocation.Parse(image.Identity) == image && image.NestingDepth == 2
            && ArchiveLocation.PhysicalPath(image.Identity) == path && image.Description.Contains("inner.zip › children › leaf.cbz › sub › 10.png"),
            "nested archive identity preserves two-level hierarchy and outer physical source");
        var tempRoot = Path.Combine(root, "temp-" + Guid.NewGuid().ToString("N"));
        await using (var store = new ArchiveTemporaryStore(tempRoot)) {
            using var outer = await ArchiveSession.OpenAsync(path, default);
            using var inner = await ArchiveSession.OpenNestedAsync(outer, innerItem, store, default);
            Check(inner.SourcePath == innerItem.Identity && inner.Images("").Single().Name == "1.png" && inner.Locations("").Single().Name == "children",
                "explicit nested open shows direct images and child folders separately");
            using var child = await ArchiveSession.OpenNestedAsync(inner, leafItem, store, default);
            inner.Dispose(); outer.Dispose();
            var page = await child.DecodeAsync(image, default);
            Check(page.Width == 8 && page.BgraPixels[2] > 200 && store.UsedBytes == middle.Length + leaf.Length,
                "nested image reads retain ancestor temporary inputs after old sessions close");
            using (var parent = child.CreateParentView()!) {
                Check(parent.SourcePath == innerItem.Identity && parent.Images("").Single().Name == "1.png", "parent view reuses outer archive without extracting it again");
                var before = store.UsedBytes;
                using var restored = await ArchiveSession.OpenAsync(innerItem.Identity, default, false, store, child);
                Check(store.UsedBytes == before && restored.SourcePath == innerItem.Identity, "ancestor restoration clones existing inputs without duplicate disk allocation");
            }
            using (var document = await PdfSession.OpenArchiveAsync(child, new(leafItem.Identity, "sub/book.pdf", false), store, default)) {
                child.Dispose();
                var rendered = await document.DecodeAsync(2, default);
                Check(rendered.Width == 288 && rendered.BgraPixels[1] > 240, "PDF in nested archive retains entire source chain while rendering");
                var selection = ViewerStateStore.ResolveStartup(new(new ArchiveLocation(leafItem.Identity, "sub").Identity, image.Identity), null, true);
                Check(selection.Path == image.Identity, "startup resolves nested page using outer file existence");
                var pdfTarget = new PdfLocation(new ArchiveLocation(leafItem.Identity, "sub/book.pdf", false).Identity, 2);
                Check(ViewerStateStore.ResolveStartup(new(new PdfLocation(pdfTarget.DocumentPath).Identity, pdfTarget.Identity), null, true).Path == pdfTarget.Identity,
                    "startup resolves nested PDF page without temporary paths");
            }
            Check(store.UsedBytes == 0, "last nested document disposal releases all ancestor expanded files");
            using (var restored = await ArchiveSession.OpenAsync(leafItem.Identity, default, false, store)) {
                Check((await restored.DecodeAsync(image, default)).Width == 8 && store.UsedBytes == middle.Length + leaf.Length,
                    "full nested restore recreates chain from physical outer archive");
            }
            Check(store.UsedBytes == 0, "restored chain cleanup removes intermediate archive files");
            using var source = await ArchiveSession.OpenAsync(path, default);
            await Reject<Exception>(() => ArchiveSession.OpenNestedAsync(source, new(path, "bad.zip", false), store, default), "malformed nested archive fails cleanly");
            Check(store.UsedBytes == 0, "malformed nested archive rolls back temporary file");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Reject<OperationCanceledException>(() => ArchiveSession.OpenNestedAsync(source, innerItem, store, cancellation.Token), "nested archive cancellation releases pending input references");
        }
        await using (var limited = new ArchiveTemporaryStore(tempRoot, middle.Length - 1)) {
            using var source = await ArchiveSession.OpenAsync(path, default);
            await Reject<IOException>(() => ArchiveSession.OpenNestedAsync(source, innerItem, limited, default), "nested extraction obeys shared temporary budget");
            Check(limited.UsedBytes == 0, "over-budget nested archive leaves no expanded inputs");
        }
        var chain = Zip(("page.png", png));
        for (var i = 0; i < 9; i++) chain = Zip(("inner.zip", chain));
        var deepPath = Path.Combine(root, "deep.zip"); await File.WriteAllBytesAsync(deepPath, chain);
        await using (var store = new ArchiveTemporaryStore(tempRoot)) {
            ArchiveSession? current = await ArchiveSession.OpenAsync(deepPath, default); string currentSource = deepPath;
            try {
                for (var depth = 1; depth <= 8; depth++) {
                    var item = new ArchiveLocation(currentSource, "inner.zip", false);
                    var next = await ArchiveSession.OpenNestedAsync(current, item, store, default);
                    current.Dispose(); current = next; currentSource = item.Identity;
                }
                Check(new ArchiveLocation(currentSource).NestingDepth == 8 && current.Locations("").Single().Name == "inner.zip", "approved eight nested levels open successfully");
                var bytes = store.UsedBytes;
                await Reject<NotSupportedException>(() => ArchiveSession.OpenNestedAsync(current, new(currentSource, "inner.zip", false), store, default), "ninth nested level is rejected before expansion");
                Check(store.UsedBytes == bytes, "nesting limit rejection does not allocate another temporary file");
                var invalid = new ArchiveLocation(new ArchiveLocation(currentSource, "inner.zip", false).Identity).Identity;
                await Reject<NotSupportedException>(() => Task.Run(() => ArchiveLocation.Parse(invalid)), "persisted excessive-depth identity cannot bypass nesting limit");
            }
            finally { current?.Dispose(); }
            Check(store.UsedBytes == 0, "deep chain disposal clears all eight temporary archives");
        }
        var after = SHA256.HashData(await File.ReadAllBytesAsync(path));
        Check(hash.SequenceEqual(after), "nested browsing preserves original outer archive bytes");
        return count;
    }
}
