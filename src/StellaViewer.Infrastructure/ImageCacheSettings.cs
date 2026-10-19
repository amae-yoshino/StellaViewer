namespace StellaViewer.Infrastructure;

public sealed record ImageCacheSettings(int MemoryMiB = 512, int DiskMiB = 2048, int PrefetchPages = 2)
{
    public long MemoryBytes => Math.Max(0, MemoryMiB) * 1024L * 1024;
    public long DiskBytes => Math.Max(0, DiskMiB) * 1024L * 1024;
    public ImageCacheSettings Normalize() => this with {
        MemoryMiB = Math.Max(0, MemoryMiB), DiskMiB = Math.Max(0, DiskMiB), PrefetchPages = Math.Max(0, PrefetchPages)
    };
    public ImageCacheSettings MergeChanges(ImageCacheSettings before, ImageCacheSettings next) => this with {
        MemoryMiB = next.MemoryMiB != before.MemoryMiB ? next.MemoryMiB : MemoryMiB,
        DiskMiB = next.DiskMiB != before.DiskMiB ? next.DiskMiB : DiskMiB,
        PrefetchPages = next.PrefetchPages != before.PrefetchPages ? next.PrefetchPages : PrefetchPages
    };
}
