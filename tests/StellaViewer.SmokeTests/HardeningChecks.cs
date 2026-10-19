using System.Diagnostics;
using System.IO.Compression;
using StellaViewer.Infrastructure;

internal static class HardeningChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var root = Path.Combine(Path.GetFullPath(output), "hardening-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
        var folder = Path.Combine(root, "登録先"); Directory.CreateDirectory(folder);
        var favoritesPath = Path.Combine(root, "favorites.json"); var favorites = new FavoritesStore(favoritesPath);
        var linksPath = Path.Combine(root, "links.json"); var links = new DirectoryShortcutStore(linksPath);
        var historyPath = Path.Combine(root, "history.json"); var history = new RecentLocationStore(historyPath);
        foreach (var (path, save, load) in new (string, Func<Task>, Func<Task<int>>)[] {
            (favoritesPath, async () => { await favorites.SetAsync(folder, FavoriteKind.Folder, true); }, async () => (await favorites.LoadAsync()).Length),
            (linksPath, () => links.AddAsync(folder), async () => (await links.LoadAsync()).Length),
            (historyPath, () => history.RecordAsync(folder), async () => (await history.LoadAsync()).Length) }) {
            await save();
            Check(File.Exists(path + ".bak"), "Registration initial save creates backup: " + Path.GetFileName(path));
            File.WriteAllText(path, "broken json");
            Check(await load() == 1 && Directory.GetFiles(root, Path.GetFileName(path) + ".corrupt-*").Length == 1,
                "Registration recovers backup and quarantines original: " + Path.GetFileName(path));
            Check(RecoverableSettings.TakeNotice(root)?.Contains("バックアップから復旧") == true, "Registration recovery provides notice");
            using (var big = File.Open(path, FileMode.Create)) big.SetLength(4 * 1024 * 1024 + 1);
            Check(await load() == 1, "Registration rejects oversized JSON before parsing");
            using (var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                var denied = false; try { await load(); } catch (IOException) { denied = true; }
                Check(denied, "Registration lock error does not overwrite data");
            }
            File.WriteAllText(path, "null"); File.WriteAllText(path + ".bak", "broken");
            Check(await load() == 0, "Registration defaults safely when both copies are invalid");
            await save(); Check(await load() == 1, "Registration remains editable after recovery");
        }
        // Semantic corruption is also recovered, including a broken manual-order snapshot.
        await favorites.SetAsync(Path.Combine(root, "second"), FavoriteKind.Folder, true);
        File.WriteAllText(favoritesPath, "{\"Version\":1,\"Entries\":[{\"Path\":\"relative\",\"Kind\":2}],\"ManualOrder\":true}");
        Check((await favorites.LoadAsync()).Single().Path == folder, "Invalid saved absolute path recovers prior valid registration");
        await favorites.SetAsync(Path.Combine(root, "second"), FavoriteKind.Folder, true);
        await favorites.MoveAsync(Path.Combine(root, "second"), -1);
        var ordered = await favorites.LoadAsync();
        await favorites.SetAsync(Path.Combine(root, "third"), FavoriteKind.Folder, true);
        File.WriteAllText(favoritesPath, "broken");
        Check((await favorites.LoadAsync()).SequenceEqual(ordered), "Registration backup restores manually arranged order");
        await history.RemoveAsync();
        File.WriteAllText(historyPath, "broken");
        Check((await history.LoadAsync()).Length == 0, "Cleared history is not resurrected from recovery backup");
        ArchiveReadLimits.CheckPath(new string('a', ArchiveReadLimits.MaximumPathCharacters));
        Check(true, "Archive path accepts exact character boundary");
        var book = Path.Combine(root, "long-name.zip");
        using (var z = new ZipArchive(File.Create(book), ZipArchiveMode.Create)) z.CreateEntry(new string('x', 4097) + ".png");
        var rejected = false;
        try { using var ignored = await ArchiveSession.OpenAsync(book, default); } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Archive catalog rejects excessive path length");
        book = Path.Combine(root, "deep.zip");
        using (var z = new ZipArchive(File.Create(book), ZipArchiveMode.Create)) z.CreateEntry(string.Concat(Enumerable.Repeat("a/", 128)) + "x.png");
        rejected = false;
        try { using var ignored = await ArchiveSession.OpenAsync(book, default); } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Archive catalog rejects excessive synthetic folder depth");
        book = Path.Combine(root, "many.zip");
        using (var z = new ZipArchive(File.Create(book), ZipArchiveMode.Create))
            for (var i = 0; i <= ArchiveReadLimits.MaximumItems; i++) z.CreateEntry($"{i}.png");
        rejected = false;
        try { using var ignored = await ArchiveSession.OpenAsync(book, default); } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Archive catalog rejects more than 100000 entries");
        book = Path.Combine(root, "path-total.zip");
        using (var z = new ZipArchive(File.Create(book), ZipArchiveMode.Create))
            for (var i = 0; i < 5000; i++) z.CreateEntry($"{i}-" + new string('x', 3600) + ".png");
        rejected = false;
        try { using var ignored = await ArchiveSession.OpenAsync(book, default); }
        catch (NotSupportedException error) { rejected = error.Message.Contains("合計"); }
        Check(rejected, "Archive catalog rejects excessive total path characters");
        var decoder = new MagickImageDecoder(); var imagePath = Path.Combine(output, "sample.png");
        var decoded = await decoder.DecodeAsync(imagePath, default);
        var pid = DecodeWorker.WorkerProcessId;
        Check(pid.HasValue && pid != Environment.ProcessId && decoded.Width == 8, "Native image decoding uses a separate process");
        await decoder.DecodeAsync(imagePath, default);
        Check(DecodeWorker.WorkerProcessId == pid, "Thumbnail and image requests reuse worker process");
        using (var cancellation = new CancellationTokenSource()) {
            using var blocked = new WaitingStream();
            var request = decoder.DecodeStreamAsync(blocked, cancellation.Token, 0);
            await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            pid = DecodeWorker.WorkerProcessId;
            cancellation.Cancel();
            var canceled = false; try { await request; } catch (OperationCanceledException) { canceled = true; }
            Check(canceled && DecodeWorker.WorkerProcessId is null, "Cancellation terminates worker and pipe operations");
            Check(!Alive(pid), "Canceled worker process has exited");
        }
        Check((await decoder.DecodeAsync(imagePath, default)).Width == 8, "Next image restarts worker after cancellation");
        var oldTimeout = DecodeWorker.RequestTimeout;
        try {
            DecodeWorker.RequestTimeout = TimeSpan.FromMilliseconds(300);
            using var blocked = new WaitingStream(); var timer = Stopwatch.StartNew();
            var timedOut = false;
            try { await decoder.DecodeStreamAsync(blocked, default, 0); } catch (IOException error) { timedOut = error.Message.Contains("中止"); }
            Check(timedOut && timer.Elapsed < TimeSpan.FromSeconds(5) && DecodeWorker.WorkerProcessId is null, "Timeout terminates worker and returns actionable error");
        }
        finally { DecodeWorker.RequestTimeout = oldTimeout; }
        Check((await decoder.DecodeAsync(imagePath, default)).Width == 8, "Next image remains usable after timeout");
        using (var blocked = new WaitingStream()) {
            var request = decoder.DecodeStreamAsync(blocked, default, 0);
            await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var worker = Process.GetProcessById(DecodeWorker.WorkerProcessId!.Value); worker.Kill(entireProcessTree: true);
            var crashed = false; try { await request; } catch (IOException) { crashed = true; }
            Check(crashed && DecodeWorker.WorkerProcessId is null, "Unexpected worker exit does not hang viewer request");
        }
        Check((await decoder.DecodeAsync(imagePath, default)).Width == 8, "Next image remains usable after worker crash");
        return checks;
    }
    private static bool Alive(int? id)
    {
        if (id is null) return false;
        try { using var p = Process.GetProcessById(id.Value); return !p.HasExited && !p.WaitForExit(2000); }
        catch (ArgumentException) { return false; }
    }
    private sealed class WaitingStream : Stream
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
            Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
