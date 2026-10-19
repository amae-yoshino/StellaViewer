using System.IO.Compression;
using System.Security.Cryptography;
using StellaViewer.Infrastructure;

internal static class ArchivePdfChecks
{
    public static async Task<int> RunAsync(string output)
    {
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS: " + name); }
        async Task Reject<T>(Func<Task> action, string label) where T : Exception {
            try { await action(); } catch (T) { Check(true, label); return; }
            throw new Exception(label + " was accepted");
        }
        var root = Path.Combine(output, "archive-pdf-checks"); Directory.CreateDirectory(root);
        var archivePath = Path.Combine(root, "book.zip");
        using (var zip = new ZipArchive(File.Create(archivePath), ZipArchiveMode.Create)) {
            foreach (var (name, source) in new[] { ("日本語/book.pdf", "pages.pdf"), ("protected.pdf", "protected.pdf"), ("broken.pdf", "broken.pdf") }) {
                using var input = File.OpenRead(Path.Combine(output, "pdf-checks", source));
                using var entry = zip.CreateEntry(name).Open(); input.CopyTo(entry);
            }
            using var png = File.OpenRead(Path.Combine(output, "sample.png"));
            using var image = zip.CreateEntry("日本語/1.png").Open(); png.CopyTo(image);
        }
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(archivePath));
        var session = await ArchiveSession.OpenAsync(archivePath, default);
        var location = new ArchiveLocation(archivePath, "日本語/book.pdf", false);
        var pdfLocation = new PdfLocation(location.Identity, 2);
        Check(PdfLocation.Parse(pdfLocation.Identity) == pdfLocation && BrowsePath.PhysicalPath(pdfLocation.Identity) == archivePath
            && BrowsePath.DisplayPath(pdfLocation.Identity).Contains("日本語 › book.pdf › ページ 2"), "archive PDF identity resolves external archive and readable hierarchy");
        var tempRoot = Path.Combine(root, "temp-" + Guid.NewGuid().ToString("N"));
        await using (var store = new ArchiveTemporaryStore(tempRoot)) {
            Check(store.Limit == 2L * 1024 * 1024 * 1024, "archive temporary budget defaults to approved two GiB");
            using (var pdf = await PdfSession.OpenArchiveAsync(session, location, store, default)) {
                Check(pdf.PageCount == 3 && pdf.SourcePath == location.Identity && pdf.Pages().All(e => PdfLocation.Parse(e.Path)!.DocumentPath == location.Identity),
                    "archive PDF pages use archive item identity instead of temporary file path");
                Check(pdf.Length == session.FindFile(location.EntryPath)!.Length && store.UsedBytes == pdf.Length, "temporary bytes include actual expanded PDF file size");
                var page = await pdf.DecodeAsync(2, default);
                Check(page.Width == 288 && page.Height == 432 && page.BgraPixels[1] > 240, "archive PDF renders requested page at 144 dpi");
                var startup = ViewerStateStore.ResolveStartup(new(new PdfLocation(location.Identity).Identity, pdfLocation.Identity), null, true);
                Check(startup.Path == pdfLocation.Identity, "archive PDF startup checks outer archive rather than virtual document path");
                var names = Directory.GetFiles(store.DirectoryPath).Select(Path.GetFileName).Where(n => n != "owner.lock").ToArray();
                Check(names.Length == 1 && Guid.TryParseExact(Path.GetFileNameWithoutExtension(names[0]), "N", out _), "archive entry names never become temporary extraction paths");
            }
            Check(store.UsedBytes == 0 && Directory.GetFiles(store.DirectoryPath, "*.pdf").Length == 0, "closing archive PDF promptly deletes unused expanded file");
            await Reject<NotSupportedException>(() => PdfSession.OpenArchiveAsync(session, new(archivePath, "protected.pdf", false), store, default), "archive password-protected PDF remains unsupported");
            await Reject<InvalidDataException>(() => PdfSession.OpenArchiveAsync(session, new(archivePath, "broken.pdf", false), store, default), "archive malformed PDF reports failure");
            Check(store.UsedBytes == 0, "failed archive PDF initialization rolls back temporary bytes");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Reject<OperationCanceledException>(() => PdfSession.OpenArchiveAsync(session, location, store, canceled.Token), "archive PDF extraction honors pre-cancellation");
            Check(store.UsedBytes == 0, "canceled archive PDF extraction leaves no retained file");
            await using var other = new ArchiveTemporaryStore(tempRoot);
            Check(Directory.Exists(store.DirectoryPath), "startup cleanup does not delete another active owner's directory");
        }
        Check(Directory.GetDirectories(tempRoot).Length == 0, "normal shutdown removes owner directories and markers");
        await using (var small = new ArchiveTemporaryStore(tempRoot, 10)) {
            using var lease = await small.CreateAsync(async (stream, _, token) => await stream.WriteAsync(new byte[6], token), default);
            await Reject<IOException>(() => small.CreateAsync(async (stream, _, token) => await stream.WriteAsync(new byte[5], token), default), "temporary budget counts all retained files together");
            Check(small.UsedBytes == 6 && Directory.GetFiles(small.DirectoryPath, "*.pdf").Length == 1, "over-budget write is stopped and partial file is removed");
            await Reject<IOException>(() => small.CreateAsync(async (stream, _, token) => {
                await stream.WriteAsync(new byte[3], token); await stream.WriteAsync(new byte[2], token);
            }, default), "temporary budget checks actual bytes during each chunk");
            Check(small.UsedBytes == 6, "partial expansion failure releases its accounting");
            using var cancellation = new CancellationTokenSource();
            await Reject<OperationCanceledException>(() => small.CreateAsync(async (stream, _, token) => {
                await stream.WriteAsync(new byte[3], token); cancellation.Cancel();
            }, cancellation.Token), "cancellation after partial extraction rolls back output");
            Check(small.UsedBytes == 6, "partial cancellation leaves only original retained file charged");
        }
        var waiting = new ArchiveTemporaryStore(tempRoot, 10);
        var held = await waiting.CreateAsync(async (stream, _, token) => await stream.WriteAsync(new byte[6], token), default);
        using var reference = held.Retain(); held.Dispose(); held.Dispose();
        Check(File.Exists(held.Path) && waiting.UsedBytes == 6, "retained native input survives repeated owner disposal");
        var cleanup = waiting.DisposeAsync().AsTask();
        Check(!cleanup.IsCompleted && File.Exists(held.Path), "shutdown waits for active native input lease");
        reference.Dispose(); await cleanup;
        Check(!Directory.Exists(waiting.DirectoryPath), "last native input release allows shutdown cleanup");
        var lockedStore = new ArchiveTemporaryStore(tempRoot, 10);
        var lockedLease = await lockedStore.CreateAsync(async (stream, _, token) => await stream.WriteAsync(new byte[6], token), default);
        using (var lockedFile = new FileStream(lockedLease.Path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            lockedLease.Dispose();
            Check(lockedStore.UsedBytes == 6, "failed deletion remains counted against temporary budget");
            await lockedStore.DisposeAsync();
            Check(File.Exists(Path.Combine(lockedStore.DirectoryPath, "owner.lock")), "failed shutdown deletion keeps marker for next cleanup without hanging");
        }
        ArchiveTemporaryStore.CleanupUnused(tempRoot);
        Check(!Directory.Exists(lockedStore.DirectoryPath), "stale cleanup retries files that were locked at shutdown");
        var stale = Path.Combine(tempRoot, $"session-123-{Guid.NewGuid():N}"); Directory.CreateDirectory(stale);
        await File.WriteAllTextAsync(Path.Combine(stale, "owner.lock"), "unused"); await File.WriteAllTextAsync(Path.Combine(stale, "partial.pdf"), "partial");
        var unrelated = Path.Combine(tempRoot, "session-user-data"); Directory.CreateDirectory(unrelated);
        await File.WriteAllTextAsync(Path.Combine(unrelated, "keep.txt"), "keep");
        ArchiveTemporaryStore.CleanupUnused(tempRoot);
        Check(!Directory.Exists(stale) && File.Exists(Path.Combine(unrelated, "keep.txt")), "stale cleanup removes only recognized inactive owner directories");
        var after = SHA256.HashData(await File.ReadAllBytesAsync(archivePath));
        Check(hash.SequenceEqual(after), "archive PDF browsing leaves original archive unchanged");
        var mutablePath = Path.Combine(root, "mutable.zip"); File.Copy(archivePath, mutablePath, true);
        var changing = await ArchiveSession.OpenAsync(mutablePath, default);
        await using (var store = new ArchiveTemporaryStore(tempRoot)) {
            using var pdf = await PdfSession.OpenArchiveAsync(changing, new(mutablePath, location.EntryPath, false), store, default);
            await File.AppendAllTextAsync(mutablePath, "changed");
            await Reject<IOException>(() => pdf.DecodeAsync(1, default), "expanded PDF does not hide a changed outer archive");
        }
        return count;
    }
}
