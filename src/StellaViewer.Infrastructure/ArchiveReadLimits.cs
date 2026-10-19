using System.Diagnostics;

namespace StellaViewer.Infrastructure;

/// <summary>Independent of reusable image-cache and temporary-extraction quotas.</summary>
public static class ArchiveReadLimits
{
    public const int MaximumItems = 100000;
    public const int MaximumPathCharacters = 4096;
    public const int MaximumPathDepth = 128;
    public const long MaximumCatalogCharacters = 16L * 1024 * 1024;
    public const long MaximumSolidSkipBytes = 2L * 1024 * 1024 * 1024;
    public const int TimeoutSeconds = 60;

    internal static void CheckPath(string path)
    {
        if (path.Length > MaximumPathCharacters || path.Count(c => c is '/' or '\\') >= MaximumPathDepth)
            throw new NotSupportedException("書庫内のパスが長さ4,096文字または階層128段の上限を超えています。");
    }
    internal static void CheckTime(Stopwatch timer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (timer.Elapsed > TimeSpan.FromSeconds(TimeoutSeconds))
            throw new IOException("書庫の読み込みが60秒を超えたため中止しました。");
    }
    internal static Stream OpenSource(string path, CancellationToken token) => new TimedStream(
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), token);
    private sealed class TimedStream(Stream source, CancellationToken token) : Stream
    {
        private readonly Stopwatch timer = Stopwatch.StartNew();
        private void Check() => CheckTime(timer, token);
        public override int Read(byte[] buffer, int offset, int count) { Check(); var n = source.Read(buffer, offset, count); Check(); return n; }
        public override int Read(Span<byte> buffer) { Check(); var n = source.Read(buffer); Check(); return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            Check(); var n = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); Check(); return n;
        }
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => source.CanSeek;
        public override bool CanWrite => false;
        public override long Length { get { Check(); return source.Length; } }
        public override long Position { get => source.Position; set { Check(); source.Position = value; } }
        public override long Seek(long offset, SeekOrigin origin) { Check(); return source.Seek(offset, origin); }
        public override void Flush() => source.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }
}
