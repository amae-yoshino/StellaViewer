using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Providers;
using SharpCompress.Providers.System;

namespace StellaViewer.Infrastructure;

public sealed partial class ArchiveSession
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding LegacyJapanese = CodePagesEncodingProvider.Instance.GetEncoding(932)!;

    private static IArchive OpenArchive(Stream source)
    {
        if(!DecodeWorker.IsWorker)throw new InvalidOperationException("書庫の解析・展開は読み込みプロセスでのみ実行できます。");
        return ArchiveFactory.OpenArchive(source, new ReaderOptions {
        ArchiveEncoding = new ArchiveEncoding { CustomDecoder = DecodeEntryName },
        Providers = CompressionProviderRegistry.Default.With(new SystemDeflateCompressionProvider())
        });
    }

    private static string DecodeEntryName(byte[] bytes, int start, int count, EncodingType type)
    {
        // Explicit UTF-8 names remain UTF-8. Untagged older Japanese ZIP/RAR names
        // fall back to CP932 only when the bytes are not valid UTF-8.
        if (type == EncodingType.UTF8) return Encoding.UTF8.GetString(bytes, start, count);
        try { return StrictUtf8.GetString(bytes, start, count); }
        catch (DecoderFallbackException) { return LegacyJapanese.GetString(bytes, start, count); }
    }

    private static Stream OpenItemStream(IArchive archive, ArchiveItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var entry = archive.Entries.ElementAt(item.Index);
        if (entry.Key is null || ArchiveLocation.NormalizeEntry(entry.Key) != item.Path || entry.IsEncrypted)
            throw new IOException("書庫が変更されました。再読み込みしてください。");
        // Solid RAR entries share decompression state. A fresh archive cannot
        // decode a later solid entry until its predecessors have been read.
        if (archive.Type == ArchiveType.Rar && entry.IsSolid) {
            var buffer = new byte[131072]; long skipped = 0;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            foreach (var previous in archive.Entries.Take(item.Index)) {
                token.ThrowIfCancellationRequested();
                if (previous.IsDirectory) continue;
                if (previous.Size < 0 || previous.Size > ArchiveReadLimits.MaximumSolidSkipBytes - skipped)
                    throw new NotSupportedException("ソリッド書庫の先行読み込みが上限2 GiBを超えています。");
                using var input = previous.OpenEntryStream();
                while (true) {
                    token.ThrowIfCancellationRequested();
                    ArchiveReadLimits.CheckTime(timer, token);
                    var count = input.Read(buffer); if (count == 0) break;
                    if (count > ArchiveReadLimits.MaximumSolidSkipBytes - skipped)
                        throw new NotSupportedException("ソリッド書庫の先行読み込みが上限2 GiBを超えています。");
                    skipped += count;
                }
            }
        }
        token.ThrowIfCancellationRequested();
        return entry.OpenEntryStream();
    }
}
