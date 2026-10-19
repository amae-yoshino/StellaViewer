using StellaViewer.Core;

namespace StellaViewer.Infrastructure;

public static class LocationPreview
{
    public static async Task<DecodedImage> DecodeAsync(string path, CancellationToken token, bool allowNetwork = false,uint thumbnailSize=96)
    {
        NetworkAccess.Validate(path, allowNetwork);
        var decoder = new MagickImageDecoder();
        if (await Task.Run(() => Directory.Exists(path), token))
        {
            // Select a direct child without retaining a large directory listing.
            var first = await Task.Run(() =>
            {
                FileEntry? first = null;
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    token.ThrowIfCancellationRequested();
                    if (!MagickImageDecoder.Supports(file)) continue;
                    var candidate = new FileEntry(file, Path.GetFileName(file), 0, default);
                    if (first is null || FolderBrowser.NaturalComparer.Compare(candidate, first) < 0) first = candidate;
                }
                return first?.Path ?? throw new NotSupportedException("直下にプレビューできる画像がありません。");
            }, token);
            return await decoder.DecodeAsync(first, token, thumbnailSize);
        }
        if (MagickImageDecoder.Supports(path)) return await decoder.DecodeAsync(path, token, thumbnailSize);
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) {
            using var pdf = await PdfSession.OpenAsync(path, token, allowNetwork);
            return await pdf.DecodeAsync(1, token, thumbnailSize, allowNetwork);
        }
        using var archive = await ArchiveSession.OpenAsync(path, token, allowNetwork);
        // The original preview also considered images in child folders.
        var candidate = archive.FirstPreviewImage();
        if (candidate is null) throw new NotSupportedException("プレビューできる画像がありません。");
        return await archive.DecodeAsync(new ArchiveLocation(archive.SourcePath, candidate.Path, false), token, thumbnailSize, allowNetwork);
    }
}
