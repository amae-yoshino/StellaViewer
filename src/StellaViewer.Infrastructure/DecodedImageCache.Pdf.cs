using StellaViewer.Core;

namespace StellaViewer.Infrastructure;

public sealed partial class DecodedImageCache
{
    private static async Task<Key> IdentifyPdfAsync(PdfSession session, int page, CancellationToken token, bool allowNetwork)
    {
        var version = await session.CacheIdentityAsync(page, token, allowNetwork).ConfigureAwait(false);
        var path = NormalizePath(new PdfLocation(session.SourcePath, page).Identity);
        return new(path, PathHash(path) + "-" + Hash(version) + ".svcache");
    }
    public async Task<DecodedImage> GetPdfAsync(PdfSession session, int page, CancellationToken token, bool allowNetwork = false)
    {
        NetworkAccess.Validate(BrowsePath.PhysicalPath(session.SourcePath), allowNetwork);
        await work.WaitAsync(token).ConfigureAwait(false);
        try {
            var key = await IdentifyPdfAsync(session, page, token, allowNetwork).ConfigureAwait(false);
            return await GetLockedAsync(key, () => session.DecodeAsync(page, token, allowNetwork: allowNetwork),
                () => IdentifyPdfAsync(session, page, token, allowNetwork), token).ConfigureAwait(false);
        }
        finally { work.Release(); }
    }
    public async Task<bool> PrefetchPdfAsync(PdfSession session, int page, CancellationToken token, bool allowNetwork = false)
    {
        NetworkAccess.Validate(BrowsePath.PhysicalPath(session.SourcePath), allowNetwork);
        lock (sync) if (settings.MemoryBytes == 0) return false;
        await work.WaitAsync(token).ConfigureAwait(false);
        try {
            var key = await IdentifyPdfAsync(session, page, token, allowNetwork).ConfigureAwait(false);
            lock (sync) if (memory.ContainsKey(key.Name)) return true;
            var estimate = await session.EstimatePixelBytesAsync(page, token, allowNetwork).ConfigureAwait(false);
            lock (sync) if (estimate <= 0 || estimate > settings.MemoryBytes - bytes) return false;
            await GetLockedAsync(key, () => session.DecodeAsync(page, token, allowNetwork: allowNetwork),
                () => IdentifyPdfAsync(session, page, token, allowNetwork), token).ConfigureAwait(false);
            return true;
        }
        finally { work.Release(); }
    }
}
