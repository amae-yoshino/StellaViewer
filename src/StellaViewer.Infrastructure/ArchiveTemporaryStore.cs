namespace StellaViewer.Infrastructure;

/// <summary>One owner's bounded temporary files. Archive names never become disk paths.</summary>
public sealed class ArchiveTemporaryStore : IAsyncDisposable
{
    public const long DefaultLimit = 2L * 1024 * 1024 * 1024;
    private readonly object sync = new();
    private readonly SemaphoreSlim copies = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly FileStream owner;
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, long> files = new(StringComparer.Ordinal);
    private readonly HashSet<string> leases = new(StringComparer.Ordinal);
    private bool disposed;
    private Task? cleanup;
    public string DirectoryPath { get; }
    public long Limit { get; }
    public long UsedBytes { get { lock (sync) return files.Values.Sum(); } }

    public ArchiveTemporaryStore(string root, long limit = DefaultLimit)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        root = Path.GetFullPath(root); Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("一時フォルダにリンクは使用できません。");
        CleanupUnused(root);
        Limit = limit;
        DirectoryPath = Path.Combine(root, $"session-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        owner = new FileStream(Path.Combine(DirectoryPath, "owner.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }
    public async Task<TemporaryFileLease> CreateAsync(Func<Stream, long, CancellationToken, Task> write, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await copies.WaitAsync(linked.Token).ConfigureAwait(false);
        string? path = null;
        try {
            long available;
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                available = Limit - files.Values.Sum();
                path = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".pdf");
                files.Add(path, 0);
            }
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous)) {
                using var bounded = new BoundedWriter(destination, available, size => { lock (sync) files[path] = size; });
                await write(bounded, available, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                if (destination.Length > available) throw new IOException("一時展開の容量上限を超えました。");
                lock (sync) files[path] = destination.Length;
            }
            lock (sync) { ObjectDisposedException.ThrowIf(disposed, this); leases.Add(path); }
            return new TemporaryFileLease(this, path);
        }
        catch {
            if (path is not null) ReleaseFile(path);
            throw;
        }
        finally { copies.Release(); }
    }
    internal void ReleaseFile(string path)
    {
        lock (sync) {
            leases.Remove(path);
            // Keep a failed deletion charged; it is retried at shutdown or next startup.
            try { File.Delete(path); files.Remove(path); }
            catch (IOException) { ChargeRemainingFile(path); }
            catch (UnauthorizedAccessException) { ChargeRemainingFile(path); }
            if (disposed && leases.Count == 0) drained.TrySetResult();
        }
    }
    private void ChargeRemainingFile(string path)
    {
        try { files[path] = new FileInfo(path).Length; }
        catch (IOException) { files[path] = Limit; }
        catch (UnauthorizedAccessException) { files[path] = Limit; }
    }
    public ValueTask DisposeAsync()
    {
        lock (sync) {
            if (cleanup is not null) return new(cleanup);
            disposed = true; lifetime.Cancel();
            cleanup = CleanupAsync(); return new(cleanup);
        }
    }
    private async Task CleanupAsync()
    {
        await copies.WaitAsync().ConfigureAwait(false);
        copies.Release();
        lock (sync) if (leases.Count == 0) drained.TrySetResult();
        // Active decoders retain leases; never remove their input during native reads.
        await drained.Task.ConfigureAwait(false);
        owner.Dispose();
        TryRemoveOwnedDirectory(DirectoryPath);
    }
    public static void CleanupUnused(string root)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
        foreach (var directory in Directory.EnumerateDirectories(root, "session-*", SearchOption.TopDirectoryOnly)) {
            try {
                var name = Path.GetFileName(directory).Split('-');
                if (name.Length != 3 || name[0] != "session" || !int.TryParse(name[1], out _) || !Guid.TryParseExact(name[2], "N", out _)
                    || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                var marker = Path.Combine(directory, "owner.lock");
                if (!File.Exists(marker) || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0) continue;
                using (new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                TryRemoveOwnedDirectory(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    private static void TryRemoveOwnedDirectory(string path)
    {
        try {
            // This store creates only flat files. Never recurse through unexpected folders/links.
            if (Directory.EnumerateDirectories(path).Any()) return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
                if (Path.GetFileName(file) != "owner.lock") File.Delete(file);
            File.Delete(Path.Combine(path, "owner.lock"));
            Directory.Delete(path, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private sealed class BoundedWriter(Stream output, long limit, Action<long> progress) : Stream
    {
        private long written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => written;
        public override long Position { get => written; set => throw new NotSupportedException(); }
        private void Check(int count) { if (count > limit - written) throw new IOException("一時展開の容量上限を超えました。"); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); output.Write(buffer, offset, count); written += count; progress(written); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
            Check(buffer.Length); await output.WriteAsync(buffer, token).ConfigureAwait(false); written += buffer.Length; progress(written);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) => WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override void Flush() => output.Flush();
        public override Task FlushAsync(CancellationToken token) => output.FlushAsync(token);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

public sealed class TemporaryFileLease : IDisposable
{
    private readonly object sync = new();
    private readonly ArchiveTemporaryStore owner;
    private int references = 1;
    private bool released;
    public string Path { get; }
    internal TemporaryFileLease(ArchiveTemporaryStore owner, string path) { this.owner = owner; Path = path; }
    public IDisposable Retain()
    {
        lock (sync) { ObjectDisposedException.ThrowIf(released, this); references++; }
        return new Reference(this);
    }
    internal IDisposable RetainAlive()
    {
        lock (sync) { ObjectDisposedException.ThrowIf(references == 0, this); references++; }
        return new Reference(this);
    }
    public void Dispose() { lock (sync) { if (released) return; released = true; Release(); } }
    private void Release()
    {
        lock (sync) { if (references == 0) return; if (--references == 0) owner.ReleaseFile(Path); }
    }
    private sealed class Reference(TemporaryFileLease lease) : IDisposable
    {
        private TemporaryFileLease? value = lease;
        public void Dispose() => Interlocked.Exchange(ref value, null)?.Release();
    }
}
