namespace StellaViewer.Infrastructure;

public sealed record ImageCacheUsage(long MemoryBytes, int MemoryCount, long DiskBytes, int DiskCount);
public sealed partial class DecodedImageCache
{
    public Task<ImageCacheUsage> GetUsageAsync() => Task.Run(() => {
        long used; int count; lock (sync) { used = bytes; count = memory.Count; }
        long diskBytes = 0; int diskCount = 0;
        if (diskDirectory is not null && !DiskLocked(() => {
            if (Directory.Exists(diskDirectory)) foreach (var file in new DirectoryInfo(diskDirectory).EnumerateFiles("*.svcache")) { diskBytes += file.Length; diskCount++; }
            return true;
        })) throw new IOException(Warning ?? "キャッシュ使用量を確認できません。");
        return new ImageCacheUsage(used, count, diskBytes, diskCount);
    });
    public async Task ClearAsync(bool clearMemory, bool clearDisk)
    {
        lock (sync) { epoch++; if (clearMemory) TrimMemory(0); }
        await work.WaitAsync().ConfigureAwait(false);
        try {
            if (clearMemory) lock (sync) TrimMemory(0);
            if (clearDisk && diskDirectory is not null) {
                var success = await Task.Run(() => DiskLocked(() => {
                    if (Directory.Exists(diskDirectory)) {
                        foreach (var file in Directory.EnumerateFiles(diskDirectory, "*.svcache")) File.Delete(file);
                        foreach (var file in Directory.EnumerateFiles(diskDirectory, "*.svcache.*.tmp")) File.Delete(file);
                    }
                    return true;
                })).ConfigureAwait(false);
                if (!success) throw new IOException(Warning ?? "ディスクキャッシュを削除できません。");
            }
        }
        finally { work.Release(); }
    }
}
