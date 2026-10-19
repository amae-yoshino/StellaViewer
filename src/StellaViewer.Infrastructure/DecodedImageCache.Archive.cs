using StellaViewer.Core;

namespace StellaViewer.Infrastructure;

public sealed partial class DecodedImageCache
{
    private static async Task<Key> IdentifyArchiveAsync(ArchiveSession session, ArchiveLocation location, CancellationToken token, bool allowNetwork)
    {
        var version = await session.CacheIdentityAsync(location, token, allowNetwork).ConfigureAwait(false);
        var path = NormalizePath(location.Identity);
        return new(path, PathHash(path) + "-" + Hash(version) + ".svcache");
    }
    public async Task<DecodedImage> GetArchiveAsync(ArchiveSession session, ArchiveLocation location, CancellationToken token, bool allowNetwork = false)
    {
        NetworkAccess.Validate(ArchiveLocation.PhysicalPath(session.SourcePath), allowNetwork);
        await work.WaitAsync(token).ConfigureAwait(false);
        try {
            var key = await IdentifyArchiveAsync(session, location, token, allowNetwork).ConfigureAwait(false);
            return await GetLockedAsync(key, () => session.DecodeAsync(location, token, allowNetwork: allowNetwork),
                () => IdentifyArchiveAsync(session, location, token, allowNetwork), token).ConfigureAwait(false);
        }
        finally { work.Release(); }
    }
    public async Task<bool> PrefetchArchiveAsync(ArchiveSession session, ArchiveLocation location, CancellationToken token, bool allowNetwork = false)
    {
        NetworkAccess.Validate(ArchiveLocation.PhysicalPath(session.SourcePath), allowNetwork);
        lock (sync) if (settings.MemoryBytes == 0) return false;
        await work.WaitAsync(token).ConfigureAwait(false);
        try {
            var key = await IdentifyArchiveAsync(session, location, token, allowNetwork).ConfigureAwait(false);
            lock (sync) if (memory.ContainsKey(key.Name)) return true;
            var estimate = await session.EstimatePixelBytesAsync(location, token, allowNetwork).ConfigureAwait(false);
            lock (sync) if (estimate <= 0 || estimate > settings.MemoryBytes - bytes) return false;
            await GetLockedAsync(key, () => session.DecodeAsync(location, token, allowNetwork: allowNetwork),
                () => IdentifyArchiveAsync(session, location, token, allowNetwork), token).ConfigureAwait(false);
            return true;
        }
        finally { work.Release(); }
    }
}
