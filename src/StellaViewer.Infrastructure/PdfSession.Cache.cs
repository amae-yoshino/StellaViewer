using PDFtoImage;
using SkiaSharp;

namespace StellaViewer.Infrastructure;

public sealed partial class PdfSession
{
    private (IDisposable? File, SourceLeaseGroup Ancestors) RetainForCache()
    {
        lock (lifetime) {
            ObjectDisposedException.ThrowIf(disposed, this);
            var ancestors = archiveInput.Fork();
            try { return (temporary?.Retain(), ancestors); }
            catch { ancestors.Dispose(); throw; }
        }
    }
    internal Task<string> CacheIdentityAsync(int page, CancellationToken token, bool allowNetwork) => Task.Run(() => {
        NetworkAccess.Validate(BrowsePath.PhysicalPath(SourcePath), allowNetwork);
        if (page < 1 || page > PageCount) throw new ArgumentOutOfRangeException(nameof(page));
        var references = RetainForCache(); using var file = references.File; using var ancestors = references.Ancestors;
        token.ThrowIfCancellationRequested(); ValidateSource();
        var original = new FileInfo(BrowsePath.PhysicalPath(SourcePath));
        var length = original.Length; var modified = original.LastWriteTimeUtc.Ticks;
        ValidateSource(); token.ThrowIfCancellationRequested();
        // Use the outer original's revision, not a temporary extraction path or extraction timestamp.
        return $"guard-v1|PDF-sRGB-BGRA-v1|{typeof(Conversion).Assembly.GetName().Version}|{typeof(SKBitmap).Assembly.GetName().Version}|{RenderDpi}|{Length}|{length}|{modified}";
    }, token);
    internal async Task<long> EstimatePixelBytesAsync(int page, CancellationToken token, bool allowNetwork)
    {
        NetworkAccess.Validate(BrowsePath.PhysicalPath(SourcePath), allowNetwork);
        if (page < 1 || page > PageCount) throw new ArgumentOutOfRangeException(nameof(page));
        var references = RetainForCache(); using var file = references.File; using var ancestors = references.Ancestors;
        if (!DecodeWorker.IsWorker) {
            ValidateSource();
            var bytes = await DecodeWorker.PdfNumberAsync("pdf-probe", physicalPath, page, token).ConfigureAwait(false);
            ValidateSource(); return bytes;
        }
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try {
            return await Task.Run(() => {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("PDF閲覧はWindowsでのみ対応しています。");
                token.ThrowIfCancellationRequested(); ValidateSource();
                using var input = OpenSource(physicalPath);
                var size = Conversion.GetPageSize(input, page - 1, leaveOpen: true);
                if (!float.IsFinite(size.Width) || !float.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0) throw new InvalidDataException("PDFページの寸法が不正です。");
                var width = checked((int)Math.Ceiling(size.Width * RenderDpi / 72d));
                var height = checked((int)Math.Ceiling(size.Height * RenderDpi / 72d));
                ValidateSource(); token.ThrowIfCancellationRequested();
                return ImageReadGuard.CheckSize(width, height);
            }, token).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
}
