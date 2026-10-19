using SharpCompress.Archives;

namespace StellaViewer.Infrastructure;

public sealed partial class ArchiveSession
{
    private ArchiveItem CacheItem(ArchiveLocation location)
    {
        if (!ArchiveLocation.SameSource(SourcePath, location.ArchivePath)) throw new InvalidOperationException("別の書庫の項目です。");
        var item = FindFile(location.EntryPath);
        if (location.IsFolder || item is null || !FolderBrowser.IsImageFile(item.Path)) throw new NotSupportedException("この書庫内項目は画像として表示できません。");
        return item;
    }
    internal Task<string> CacheIdentityAsync(ArchiveLocation location, CancellationToken token, bool allowNetwork) => Task.Run(() => {
        NetworkAccess.Validate(ArchiveLocation.PhysicalPath(SourcePath), allowNetwork);
        using var retained = RetainInput(); var item = CacheItem(location);
        token.ThrowIfCancellationRequested(); ValidateSource();
        var original = new FileInfo(ArchiveLocation.PhysicalPath(SourcePath));
        var length = original.Length; var modified = original.LastWriteTimeUtc.Ticks;
        ValidateSource(); token.ThrowIfCancellationRequested();
        return $"Archive-static-guard-v3|Magick15-Q8-sRGB-v2|bzPSD-06b3f52|CLIP-SQLite3540000|{length}|{modified}|{sourceLength}|{item.Index}|{item.Length}";
    }, token);
    internal async Task<long> EstimatePixelBytesAsync(ArchiveLocation location, CancellationToken token, bool allowNetwork)
    {
        NetworkAccess.Validate(ArchiveLocation.PhysicalPath(SourcePath), allowNetwork);
        using var retained = RetainInput(); var item = CacheItem(location);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try {
            if(!DecodeWorker.IsWorker) {
                ValidateSource();
                var bytes=await DecodeWorker.ArchiveProbeAsync(physicalPath,SourcePath,item.Path,token).ConfigureAwait(false);
                ValidateSource();return bytes;
            }
            return await Task.Run(async () => {
                token.ThrowIfCancellationRequested(); ValidateSource();
                using var source = OpenSource(physicalPath, token); using var archive = OpenArchive(source);
                var entry = archive.Entries.ElementAt(item.Index);
                if (entry.Key is null || ArchiveLocation.NormalizeEntry(entry.Key) != item.Path || entry.IsEncrypted)
                    throw new IOException("書庫が変更されました。再読み込みしてください。");
                using var stream = OpenItemStream(archive, item, token);
                var estimate = await new MagickImageDecoder().EstimateStreamPixelBytesAsync(stream, token).ConfigureAwait(false);
                ValidateSource(); token.ThrowIfCancellationRequested(); return estimate;
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
