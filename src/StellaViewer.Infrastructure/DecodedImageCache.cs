using System.Security.Cryptography;
using System.Text;
using StellaViewer.Core;

namespace StellaViewer.Infrastructure;

// Cache ownership is separate from the bitmap held by the viewer. All returned pixels are immutable.
public sealed partial class DecodedImageCache(IImageDecoder decoder, string? diskDirectory = null, Func<ImageCacheSettings>? sharedDiskSettings = null)
{
    private const int HeaderLength = 52;
    private const int Magic = 0x53564332;
    private sealed record Key(string Path, string Name);
    private sealed record Entry(Key Key, DecodedImage Image);
    private readonly object sync = new();
    private readonly SemaphoreSlim work = new(1, 1);
    private readonly Dictionary<string, LinkedListNode<Entry>> memory = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> lru = new();
    private ImageCacheSettings settings = new();
    private long bytes, epoch, memoryHits, diskHits, decodes;
    private volatile bool suppressDiskReads;
    public long MemoryBytes { get { lock (sync) return bytes; } }
    public int MemoryCount { get { lock (sync) return memory.Count; } }
    public long MemoryHits => Interlocked.Read(ref memoryHits);
    public long DiskHits => Interlocked.Read(ref diskHits);
    public long Decodes => Interlocked.Read(ref decodes);
    public string? Warning { get; private set; }
    public void Configure(ImageCacheSettings value)
    {
        lock (sync) { settings = value.Normalize(); epoch++; TrimMemory(settings.MemoryBytes); }
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string NormalizePath(string path) => PdfLocation.Parse(path) is { } pdf
        ? new PdfLocation(CanonicalSource(pdf.DocumentPath), pdf.Page).Identity
        : ArchiveLocation.IsIdentity(path) ? CanonicalSource(path) : Path.GetFullPath(path);
    private static string CanonicalSource(string path) => ArchiveLocation.Parse(path) is { } item
        ? (item with { ArchivePath = CanonicalSource(item.ArchivePath) }).Identity
        : Path.GetFullPath(path).ToUpperInvariant();
    private static string PathHash(string path) => Hash(BrowsePath.IsVirtual(path) ? NormalizePath(path) : Path.GetFullPath(path).ToUpperInvariant());
    private static Key Identify(string path)
    {
        path = Path.GetFullPath(path); var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("画像が見つかりません。", path);
        // Bump the version when decoder/color-conversion behavior changes.
        var version = "guard-v1|" + (Path.GetExtension(path).ToLowerInvariant() switch {
            ".psd" => "bzPSD-06b3f52-merged-v1", ".clip" => "CLIP-stored-PNG-SQLite3540000-Magick15-v1", _ => "Magick15-Q8-sRGB-v2"
        });
        return new(path, PathHash(path) + "-" + Hash($"{version}|{file.Length}|{file.LastWriteTimeUtc.Ticks}") + ".svcache");
    }
    public async Task<DecodedImage> GetAsync(string path, CancellationToken token, bool allowNetwork = false)
    {
        NetworkAccess.Validate(path, allowNetwork);
        await work.WaitAsync(token).ConfigureAwait(false);
        try { return await GetLockedAsync(path, token).ConfigureAwait(false); }
        finally { work.Release(); }
    }
    private async Task<DecodedImage> GetLockedAsync(string path, CancellationToken token)
    {
        var key = await Task.Run(() => Identify(path), token).ConfigureAwait(false);
        return await GetLockedAsync(key, () => decoder.DecodeAsync(key.Path, token), () => Task.Run(() => Identify(path), token), token).ConfigureAwait(false);
    }
    private async Task<DecodedImage> GetLockedAsync(Key key, Func<Task<DecodedImage>> decode, Func<Task<Key>> identify, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ImageCacheSettings limits; long generation;
        lock (sync) {
            limits = settings; generation = epoch;
            if (limits.MemoryBytes > 0 && memory.TryGetValue(key.Name, out var found)) {
                lru.Remove(found); lru.AddFirst(found); Interlocked.Increment(ref memoryHits); return found.Value.Image;
            }
        }
        DecodedImage? image = null;
        if (diskDirectory is not null && limits.DiskBytes > 0)
            image = await Task.Run(() => DiskLocked(() => ReadDisk(key, limits, token)), token).ConfigureAwait(false);
        if (image is not null) Interlocked.Increment(ref diskHits);
        else {
            image = await decode().ConfigureAwait(false); Interlocked.Increment(ref decodes);
            token.ThrowIfCancellationRequested();
            // Do not cache bytes read while the source was being replaced or modified.
            if (await identify().ConfigureAwait(false) != key) return image;
            if (diskDirectory is not null && limits.DiskBytes > 0)
                await Task.Run(() => DiskLocked(() => { WriteDisk(key, image, limits, generation, token); return true; }), token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        lock (sync) {
            if (generation == epoch && image.BgraPixels.LongLength <= settings.MemoryBytes && settings.MemoryBytes > 0) {
                TrimMemory(settings.MemoryBytes - image.BgraPixels.LongLength);
                var node = lru.AddFirst(new Entry(key, image)); memory.Add(key.Name, node); bytes += image.BgraPixels.LongLength;
            }
        }
        return image;
    }
    public async Task<bool> PrefetchAsync(string path, CancellationToken token, bool allowNetwork = false)
    {
        NetworkAccess.Validate(path, allowNetwork);
        if (decoder is not IImageSizeProbe probe) return false;
        lock (sync) if (settings.MemoryBytes == 0) return false;
        await work.WaitAsync(token).ConfigureAwait(false);
        try {
            var key = await Task.Run(() => Identify(path), token).ConfigureAwait(false);
            lock (sync) if (memory.ContainsKey(key.Name)) return true;
            var estimate = await probe.EstimatePixelBytesAsync(path, token).ConfigureAwait(false);
            lock (sync) if (estimate <= 0 || estimate > settings.MemoryBytes - bytes) return false;
            await GetLockedAsync(path, token).ConfigureAwait(false); return true;
        }
        finally { work.Release(); }
    }
    private void TrimMemory(long limit)
    {
        while (bytes > limit && lru.Last is { } last) {
            bytes -= last.Value.Image.BgraPixels.LongLength; memory.Remove(last.Value.Key.Name); lru.RemoveLast();
        }
    }
    public async Task InvalidateAsync(IEnumerable<string> paths)
    {
        var values = paths.Select(NormalizePath).ToHashSet(StringComparer.Ordinal);
        lock (sync) {
            epoch++;
            var prefixes = values.Select(PathHash).ToHashSet(StringComparer.Ordinal);
            foreach (var node in memory.Values.Where(n => prefixes.Contains(n.Value.Key.Name.Split('-')[0])).ToArray()) {
                bytes -= node.Value.Image.BgraPixels.LongLength; memory.Remove(node.Value.Key.Name); lru.Remove(node);
            }
        }
        if (diskDirectory is null) return;
        var invalidated = await Task.Run(() => DiskLocked(() => {
            if (!Directory.Exists(diskDirectory)) return true;
            var prefixes = values.Select(PathHash).ToHashSet(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(diskDirectory, "*.svcache"))
                if (prefixes.Contains(Path.GetFileName(file).Split('-')[0])) File.Delete(file);
            return true;
        })).ConfigureAwait(false);
        if (!invalidated) suppressDiskReads = true;
    }
    public async Task MaintainDiskAsync()
    {
        if (diskDirectory is null) return;
        ImageCacheSettings limits; lock (sync) limits = settings;
        await Task.Run(() => DiskLocked(() => { TrimDisk(Math.Min(limits.DiskBytes, sharedDiskSettings?.Invoke().Normalize().DiskBytes ?? limits.DiskBytes)); return true; })).ConfigureAwait(false);
    }
    private T? DiskLocked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, @"Local\StellaViewer.ImageDiskCache"); var held = false;
        try {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { held = true; }
            if (!held) { Warning = "キャッシュの使用待ちがタイムアウトしました。"; return default; }
            return action();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException) {
            Warning = "キャッシュを利用できません：" + error.Message; return default;
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }
    private DecodedImage? ReadDisk(Key key, ImageCacheSettings limits, CancellationToken token)
    {
        var capacity = Math.Min(limits.DiskBytes, sharedDiskSettings?.Invoke().Normalize().DiskBytes ?? limits.DiskBytes);
        if (capacity == 0 || suppressDiskReads) return null;
        var path = Path.Combine(diskDirectory!, key.Name);
        if (!File.Exists(path)) return null;
        try {
            using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != Magic) throw new InvalidDataException();
            int w = reader.ReadInt32(), h = reader.ReadInt32(), ow = reader.ReadInt32(), oh = reader.ReadInt32();
            var length = checked((long)w * h * 4);
            if (w <= 0 || h <= 0 || ow <= 0 || oh <= 0 || length > ImageReadGuard.MaximumPixelBytes || length + HeaderLength != stream.Length || stream.Length > capacity)
                throw new InvalidDataException();
            var checksum = reader.ReadBytes(32);
            var pixels = new byte[(int)length];
            for (var offset = 0; offset < pixels.Length;) {
                token.ThrowIfCancellationRequested(); var count = stream.Read(pixels, offset, Math.Min(65536, pixels.Length - offset));
                if (count == 0) throw new EndOfStreamException(); offset += count;
            }
            if (!SHA256.HashData(pixels).SequenceEqual(checksum)) throw new InvalidDataException();
            stream.Dispose(); File.SetLastWriteTimeUtc(path, DateTime.UtcNow); return new(w, h, ow, oh, pixels);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or OverflowException) { File.Delete(path); return null; }
    }
    private void WriteDisk(Key key, DecodedImage image, ImageCacheSettings limits, long generation, CancellationToken token)
    {
        var capacity = Math.Min(limits.DiskBytes, sharedDiskSettings?.Invoke().Normalize().DiskBytes ?? limits.DiskBytes);
        if (image.BgraPixels.LongLength + HeaderLength > capacity || generation != Interlocked.Read(ref epoch)) return;
        Directory.CreateDirectory(diskDirectory!);
        // All writers hold the named mutex: temporary files here are abandoned writes.
        foreach (var old in Directory.EnumerateFiles(diskDirectory!, PathHash(key.Path) + "-*.svcache")) File.Delete(old);
        TrimDisk(capacity - image.BgraPixels.LongLength - HeaderLength);
        var target = Path.Combine(diskDirectory!, key.Name); var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(Magic); writer.Write(image.Width); writer.Write(image.Height); writer.Write(image.OriginalWidth); writer.Write(image.OriginalHeight);
                writer.Write(SHA256.HashData(image.BgraPixels));
                for (var offset = 0; offset < image.BgraPixels.Length; offset += 65536) {
                    token.ThrowIfCancellationRequested(); writer.Write(image.BgraPixels, offset, Math.Min(65536, image.BgraPixels.Length - offset));
                }
            }
            token.ThrowIfCancellationRequested();
            if (generation != Interlocked.Read(ref epoch)) return;
            File.Move(temporary, target, true); TrimDisk(capacity);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private void TrimDisk(long limit)
    {
        if (!Directory.Exists(diskDirectory)) return;
        foreach (var abandoned in Directory.EnumerateFiles(diskDirectory, "*.svcache.*.tmp")) File.Delete(abandoned);
        var files = new DirectoryInfo(diskDirectory).GetFiles("*.svcache").OrderBy(f => f.LastWriteTimeUtc).ToArray();
        var total = files.Sum(f => f.Length);
        foreach (var file in files) { if (total <= limit) break; total -= file.Length; file.Delete(); }
    }
}
