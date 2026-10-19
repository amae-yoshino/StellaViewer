using StellaViewer.Core;
using StellaViewer.Infrastructure;

internal static class ImageCacheChecks
{
    public static async Task<int> RunAsync(string output)
    {
        var checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
        var root = Path.Combine(output, "cache-checks", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var paths = Enumerable.Range(0, 6).Select(i => Path.Combine(root, $"{i}.png")).ToArray();
        for (var i = 0; i < paths.Length; i++) await File.WriteAllTextAsync(paths[i], i.ToString());
        var decoder = new CountingDecoder(); var cache = new DecodedImageCache(decoder); cache.Configure(new(1, 0, 0));
        var original = await cache.GetAsync(paths[0], default); var second = await cache.GetAsync(paths[0], default);
        Check(ReferenceEquals(original, second) && decoder.Count == 1 && cache.MemoryHits == 1, "memory hit reuses immutable pixels without decoding");
        foreach (var path in paths.Skip(1)) await cache.GetAsync(path, default);
        Check(cache.MemoryBytes == 1024 * 1024 && cache.MemoryCount == 4, "memory LRU enforces byte budget");
        var before = decoder.Count; await cache.GetAsync(paths[0], default);
        Check(decoder.Count == before + 1 && original.BgraPixels.Length == 256 * 256 * 4, "evicted source decodes again while held display data stays valid");
        cache.Configure(new(0, 0, 2)); before = decoder.Count;
        await cache.GetAsync(paths[0], default); await cache.GetAsync(paths[0], default);
        Check(cache.MemoryBytes == 0 && decoder.Count == before + 2, "memory zero disables reuse without preventing display decode");
        Check(!await cache.PrefetchAsync(paths[1], default), "memory zero disables prefetch");
        cache.Configure(new(1, 0, 2));
        Check(await cache.PrefetchAsync(paths[1], default), "prefetch admits image within free memory budget");
        before = decoder.Count; await cache.GetAsync(paths[1], default);
        Check(decoder.Count == before, "foreground navigation reuses prefetched pixels");
        foreach (var path in paths.Skip(2).Take(3)) await cache.PrefetchAsync(path, default);
        before = decoder.Count;
        Check(!await cache.PrefetchAsync(paths[5], default) && decoder.Count == before && cache.MemoryBytes <= 1024 * 1024,
            "prefetch skips images that exceed remaining memory before decoding");
        var disk = Path.Combine(root, "disk"); var persisted = new DecodedImageCache(decoder, disk); persisted.Configure(new(0, 1, 0));
        await persisted.GetAsync(paths[0], default); before = decoder.Count;
        var restart = new DecodedImageCache(decoder, disk); restart.Configure(new(0, 1, 0));
        var restored = await restart.GetAsync(paths[0], default);
        Check(decoder.Count == before && restart.DiskHits == 1 && restored.BgraPixels.SequenceEqual(original.BgraPixels), "disk cache survives new cache instance with correct pixels");
        var corrupt = Directory.GetFiles(disk, "*.svcache").Single(); await File.WriteAllBytesAsync(corrupt, [1, 2, 3]);
        await restart.GetAsync(paths[0], default);
        Check(decoder.Count == before + 1 && new FileInfo(corrupt).Length == 256 * 256 * 4 + 52, "corrupt disk entry is regenerated from original");
        var damaged = await File.ReadAllBytesAsync(corrupt); damaged[^1] ^= 1; await File.WriteAllBytesAsync(corrupt, damaged);
        before = decoder.Count; await restart.GetAsync(paths[0], default);
        Check(decoder.Count == before + 1, "checksum detects pixel damage even when cache length is unchanged");
        foreach (var path in paths) await restart.GetAsync(path, default);
        Check(Directory.GetFiles(disk, "*.svcache").Sum(path => new FileInfo(path).Length) <= 1024 * 1024, "disk LRU enforces actual file byte budget including header");
        await restart.GetAsync(paths[0], default);
        var writeStamp = File.GetLastWriteTimeUtc(paths[0]);
        await File.WriteAllTextAsync(paths[0], "9"); File.SetLastWriteTimeUtc(paths[0], writeStamp);
        await restart.InvalidateAsync([paths[0]]); before = decoder.Count;
        var updated = await restart.GetAsync(paths[0], default);
        Check(decoder.Count == before + 1 && updated.BgraPixels[0] == (byte)'9', "F5 invalidation bypasses even identical length and timestamp");
        await File.WriteAllTextAsync(paths[0], "changed-length"); before = decoder.Count;
        await restart.GetAsync(paths[0], default);
        Check(decoder.Count == before + 1, "source metadata change makes old disk data unreachable");
        await Task.WhenAll(restart.GetAsync(paths[2], default), persisted.GetAsync(paths[2], default));
        Check(!Directory.GetFiles(disk, "*.tmp").Any() && Directory.GetFiles(disk, "*.svcache").Sum(path => new FileInfo(path).Length) <= 1024 * 1024,
            "two cache instances serialize atomic disk writes and trimming");
        restart.Configure(new(0, 0, 0)); await restart.MaintainDiskAsync();
        Check(!Directory.GetFiles(disk, "*.svcache").Any(), "disk zero removes reusable disk entries when maintained");
        var abandoned = Path.Combine(disk, "abandoned.svcache.old.tmp"); await File.WriteAllTextAsync(abandoned, "unfinished");
        await restart.MaintainDiskAsync();
        Check(!File.Exists(abandoned), "maintenance removes abandoned atomic cache writes under shared lock");
        var blockedDisk = Path.Combine(root, "not-a-directory"); await File.WriteAllTextAsync(blockedDisk, "file");
        var fallback = new DecodedImageCache(decoder, blockedDisk); fallback.Configure(new(0, 1, 0));
        Check((await fallback.GetAsync(paths[1], default)).Width == 256 && fallback.Warning is not null, "unavailable cache storage does not prevent image display");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await fallback.GetAsync(paths[1], cancellation.Token); throw new Exception("Cancelled cache request accepted"); }
        catch (OperationCanceledException) { Check(true, "cancelled foreground request is not loaded or stored"); }
        var shared = new ImageCacheSettings(0, 0, 0);
        var sharedCache = new DecodedImageCache(decoder, disk, () => shared); sharedCache.Configure(new(0, 1, 0));
        await sharedCache.GetAsync(paths[1], default);
        Check(!Directory.GetFiles(disk, "*.svcache").Any(), "latest shared disk disable prevents older instance writing cache");
        Check(new ImageCacheSettings(-1, -1, -1).Normalize() == new ImageCacheSettings(0, 0, 0), "invalid stored cache limits normalize to zero");
        var preferences = Path.Combine(root, "preferences.json");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(cache: new(64, 2048, 2), previousCache: new(), filePath: preferences),
            PreferencesStore.SaveChangeAsync(cache: new(512, 128, 0), previousCache: new(), filePath: preferences));
        Check(PreferencesStore.Load(preferences).Cache == new ImageCacheSettings(64, 128, 0), "cache fields merge independently across concurrent settings writes");
        Check(new ViewerPreferences().Cache == new ImageCacheSettings(512, 2048, 2), "approved cache defaults are 512 MiB, 2 GiB and two neighbors each side");
        return checks;
    }
    private sealed class CountingDecoder : IImageDecoder, IImageSizeProbe
    {
        public int Count;
        public async Task<DecodedImage> DecodeAsync(string path, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(path, token); Interlocked.Increment(ref Count);
            var pixels = new byte[256 * 256 * 4]; Array.Fill(pixels, (byte)text[0]);
            return new(256, 256, 256, 256, pixels);
        }
        public Task<long> EstimatePixelBytesAsync(string path, CancellationToken token) => Task.FromResult(256L * 256 * 4);
    }
}
