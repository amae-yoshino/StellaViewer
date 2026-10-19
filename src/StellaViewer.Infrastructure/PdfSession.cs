using System.Runtime.InteropServices;
using PDFtoImage;
using PDFtoImage.Exceptions;
using SkiaSharp;
using StellaViewer.Core;

namespace StellaViewer.Infrastructure;

/// <summary>PDFium access is serialized across all documents and thumbnail requests.</summary>
public sealed partial class PdfSession : IDisposable
{
    public const int RenderDpi = 144;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public string SourcePath { get; private set; }
    public int PageCount { get; }
    public long Length { get; }
    public DateTime Modified { get; private set; }
    private readonly string physicalPath;
    private readonly object lifetime = new();
    private TemporaryFileLease? temporary;
    private SourceLeaseGroup archiveInput = new([]);
    private Action? validateArchive;
    private bool disposed;
    private readonly DateTime modifiedUtc;
    private PdfSession(string path, int count, long length, DateTime modified, DateTime utc)
    { SourcePath = physicalPath = path; PageCount = count; Length = length; Modified = modified; modifiedUtc = utc; }

    public static async Task<PdfSession> OpenArchiveAsync(ArchiveSession archive, ArchiveLocation location,
        ArchiveTemporaryStore store, CancellationToken token, bool allowNetwork = false)
    {
        NetworkAccess.Validate(ArchiveLocation.PhysicalPath(location.ArchivePath), allowNetwork);
        if (location.IsFolder || !Path.GetExtension(location.EntryPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("PDFではない書庫内項目です。");
        var item = archive.FindFile(location.EntryPath) ?? throw new FileNotFoundException("書庫内PDFが見つかりません。");
        using var retainedArchive = archive.RetainInput();
        var file = await store.CreateAsync((output, limit, cancellation) => archive.CopyEntryAsync(location, output, limit, cancellation, allowNetwork), token).ConfigureAwait(false);
        PdfSession? opened = null;
        try {
            var session = await OpenAsync(file.Path, token).ConfigureAwait(false);
            opened = session;
            session.SourcePath = location.Identity; session.Modified = item.Modified ?? default;
            session.validateArchive = archive.ValidateUnchanged; session.temporary = file;
            session.archiveInput.Dispose(); session.archiveInput = retainedArchive.Fork();
            session.ValidateSource(); return session;
        }
        catch { opened?.Dispose(); file.Dispose(); throw; }
    }
    public void Dispose() { lock (lifetime) { if (disposed) return; disposed = true; temporary?.Dispose(); archiveInput.Dispose(); } }

    public static async Task<PdfSession> OpenAsync(string path, CancellationToken token, bool allowNetwork = false)
    {
        NetworkAccess.Validate(path, allowNetwork); path = Path.GetFullPath(path);
        if (!DecodeWorker.IsWorker) {
            var info = new FileInfo(path); var bytes = info.Length; var utc = info.LastWriteTimeUtc; var modified = info.LastWriteTime;
            var count = checked((int)await DecodeWorker.PdfNumberAsync("pdf-count", path, 0, token).ConfigureAwait(false));
            if (count < 1 || count > 100000) throw new NotSupportedException("PDFのページ数が上限100,000ページを超えているか、不正です。");
            var opened = new PdfSession(path, count, bytes, modified, utc); opened.ValidateSource(); return opened;
        }
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try {
            return await Task.Run(() => {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("PDF閲覧はWindowsでのみ対応しています。");
                token.ThrowIfCancellationRequested();
                var source = new FileInfo(path); var length = source.Length; var utc = source.LastWriteTimeUtc; var modified = source.LastWriteTime;
                using var stream = OpenSource(path);
                var count = Conversion.GetPageCount(stream, leaveOpen: true);
                if (count < 1) throw new InvalidDataException("PDFに表示できるページがありません。");
                token.ThrowIfCancellationRequested();
                var session = new PdfSession(path, count, length, modified, utc);
                session.ValidateSource(); return session;
            }, token).ConfigureAwait(false);
        }
        catch (PdfPasswordProtectedException error) { throw new NotSupportedException("パスワード付きPDFには対応していません。", error); }
        catch (PdfUnsupportedSecuritySchemeException error) { throw new NotSupportedException("このPDFの保護方式には対応していません。", error); }
        catch (PdfException error) { throw new InvalidDataException("PDFを読み込めません。破損または非対応の文書です。", error); }
        finally { Gate.Release(); }
    }
    public FileEntry[] Pages() => Enumerable.Range(1, PageCount)
        .Select(page => new FileEntry(new PdfLocation(SourcePath, page).Identity, $"ページ {page}", Length, Modified)).ToArray();
    public async Task<DecodedImage> DecodeAsync(int page, CancellationToken token, uint thumbnailSize = 0, bool allowNetwork = false)
    {
        NetworkAccess.Validate(BrowsePath.PhysicalPath(SourcePath), allowNetwork);
        if (page < 1 || page > PageCount) throw new ArgumentOutOfRangeException(nameof(page));
        IDisposable? reference;
        SourceLeaseGroup ancestors;
        lock (lifetime) {
            ObjectDisposedException.ThrowIf(disposed, this); ancestors = archiveInput.Fork();
            try { reference = temporary?.Retain(); } catch { ancestors.Dispose(); throw; }
        }
        using var retained = reference;
        using var retainedAncestors = ancestors;
        if (!DecodeWorker.IsWorker) {
            ValidateSource();
            var decoded = await DecodeWorker.PdfImageAsync(physicalPath, page, thumbnailSize, token).ConfigureAwait(false);
            ValidateSource(); return decoded;
        }
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try {
            return await Task.Run(() => {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("PDF閲覧はWindowsでのみ対応しています。");
                token.ThrowIfCancellationRequested(); ValidateSource();
                using var stream = OpenSource(physicalPath);
                var size = Conversion.GetPageSize(stream, page - 1, leaveOpen: true);
                if (!float.IsFinite(size.Width) || !float.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
                    throw new InvalidDataException("PDFページの寸法が不正です。");
                var fullWidth = checked((int)Math.Ceiling(size.Width * RenderDpi / 72d));
                var fullHeight = checked((int)Math.Ceiling(size.Height * RenderDpi / 72d));
                ImageReadGuard.CheckSize(fullWidth, fullHeight);
                stream.Position = 0;
                token.ThrowIfCancellationRequested();
                var thumb = thumbnailSize > 0 && (fullWidth > thumbnailSize || fullHeight > thumbnailSize);
                var options = new RenderOptions(Dpi: RenderDpi,
                    Width: thumb && size.Width >= size.Height ? checked((int)thumbnailSize) : null,
                    Height: thumb && size.Height > size.Width ? checked((int)thumbnailSize) : null,
                    WithAspectRatio: thumb);
                using var bitmap = Conversion.ToImage(stream, page - 1, leaveOpen: true, options: options);
                token.ThrowIfCancellationRequested();
                using var colorSpace = SKColorSpace.CreateSrgb();
                var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, colorSpace);
                using var normalized = new SKBitmap(info);
                using var image = SKImage.FromBitmap(bitmap);
                if (!image.ReadPixels(info, normalized.GetPixels(), normalized.RowBytes, 0, 0))
                    throw new InvalidDataException("PDFページの画素を取得できませんでした。");
                var stride = checked(bitmap.Width * 4); var pixels = new byte[checked(stride * bitmap.Height)];
                for (var row = 0; row < bitmap.Height; row++)
                    Marshal.Copy(IntPtr.Add(normalized.GetPixels(), checked(row * normalized.RowBytes)), pixels, row * stride, stride);
                ValidateSource(); token.ThrowIfCancellationRequested();
                return new DecodedImage(bitmap.Width, bitmap.Height,
                    thumbnailSize == 0 ? bitmap.Width : fullWidth, thumbnailSize == 0 ? bitmap.Height : fullHeight, pixels);
            }, token).ConfigureAwait(false);
        }
        catch (PdfException error) { throw new InvalidDataException("PDFページを描画できませんでした。再読み込みしてください。", error); }
        finally { Gate.Release(); }
    }
    private static FileStream OpenSource(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    private void ValidateSource()
    {
        validateArchive?.Invoke();
        var source = new FileInfo(physicalPath);
        if (!source.Exists || source.Length != Length || source.LastWriteTimeUtc != modifiedUtc)
            throw new IOException("PDFが変更されました。再読み込みしてください。");
    }
}
