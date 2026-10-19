namespace StellaViewer.Infrastructure;

/// <summary>Replays signature bytes without requiring seek, and leaves the caller's stream open.</summary>
internal sealed class PrefixReadStream(ReadOnlyMemory<byte> prefix, Stream source) : Stream
{
    private int offset;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int start, int count) => Read(buffer.AsSpan(start, count));
    public override int Read(Span<byte> buffer)
    {
        if (offset >= prefix.Length) return source.Read(buffer);
        var count = Math.Min(buffer.Length, prefix.Length - offset);
        prefix.Span.Slice(offset, count).CopyTo(buffer); offset += count; return count;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
