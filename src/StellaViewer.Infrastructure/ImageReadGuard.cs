using System.Buffers.Binary;
using ImageMagick;

namespace StellaViewer.Infrastructure;

/// <summary>Inspect bounded headers before entering a native decoder, including non-seekable archive streams.</summary>
internal static class ImageReadGuard
{
    internal const long MaximumPixelBytes = 256L * 1024 * 1024;
    internal static long CheckSize(long width, long height)
    {
        if (width <= 0 || height <= 0 || width > MaximumPixelBytes / 4 / height)
            throw new NotSupportedException("画像の表示用画素が上限（256 MiB）を超えているか、寸法が不正です。");
        return width * height * 4;
    }
    internal sealed record Header(MagickFormat Format, long Width, long Height, byte[] Prefix);
    internal static Header Read(Stream source, CancellationToken token)
    {
        using var captured = new MemoryStream();
        byte[] Read(int count) {
            token.ThrowIfCancellationRequested();
            if (count < 0 || captured.Length + count > 16 * 1024 * 1024) throw new NotSupportedException("画像ヘッダーが読み取り上限（16 MiB）を超えています。");
            var bytes = new byte[count]; source.ReadExactly(bytes); captured.Write(bytes); return bytes;
        }
        var first = Read(2); MagickFormat format; long width, height;
        if (first[0] == 137 && first[1] == 80) {
            var h = Read(22);
            if (!h.AsSpan(0, 6).SequenceEqual(new byte[] { 78, 71, 13, 10, 26, 10 }) || BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(6)) != 13 || !h.AsSpan(10, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException("PNGヘッダーが不正です。");
            width = BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(14)); height = BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(18)); format = MagickFormat.Png;
        }
        else if (first.AsSpan().SequenceEqual("GI"u8)) {
            var h = Read(8); if (!h.AsSpan(0, 4).SequenceEqual("F87a"u8) && !h.AsSpan(0, 4).SequenceEqual("F89a"u8)) throw new InvalidDataException("GIFヘッダーが不正です。");
            width = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(4)); height = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(6)); format = MagickFormat.Gif;
        }
        else if (first.AsSpan().SequenceEqual("BM"u8)) {
            var h = Read(16); var dib = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12));
            if (dib == 12) { var size = Read(4); width = BinaryPrimitives.ReadUInt16LittleEndian(size); height = BinaryPrimitives.ReadUInt16LittleEndian(size.AsSpan(2)); }
            else if (dib is 40 or 52 or 56 or 64 or 108 or 124) { var size = Read(8); width = BinaryPrimitives.ReadInt32LittleEndian(size); height = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(size.AsSpan(4))); }
            else throw new NotSupportedException("このBMPヘッダーには対応していません。");
            format = MagickFormat.Bmp;
        }
        else if (first[0] == 255 && first[1] == 216) {
            while (true) {
                if (Read(1)[0] != 255) throw new InvalidDataException("JPEGマーカーが不正です。");
                byte marker; do { marker = Read(1)[0]; } while (marker == 255);
                if (marker is 0 or 217 or 218) throw new InvalidDataException("JPEGの画像寸法が見つかりません。");
                if (marker == 1 || marker is >= 208 and <= 215) continue;
                var size = BinaryPrimitives.ReadUInt16BigEndian(Read(2)); if (size < 2) throw new InvalidDataException("JPEG区画長が不正です。");
                var segment = Read(size - 2);
                if (marker is >= 192 and <= 207 && marker is not (196 or 200 or 204)) {
                    if (segment.Length < 6) throw new InvalidDataException("JPEG寸法が不正です。");
                    height = BinaryPrimitives.ReadUInt16BigEndian(segment.AsSpan(1)); width = BinaryPrimitives.ReadUInt16BigEndian(segment.AsSpan(3)); break;
                }
            }
            format = MagickFormat.Jpeg;
        }
        else if (first.AsSpan().SequenceEqual("RI"u8)) {
            var h = Read(10);
            if (!h.AsSpan(0, 2).SequenceEqual("FF"u8) || !h.AsSpan(6, 4).SequenceEqual("WEBP"u8)) throw new NotSupportedException("対応していない画像形式です。");
            var chunk = Read(8); var length = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
            if (chunk.AsSpan(0, 4).SequenceEqual("VP8X"u8) && length == 10) {
                var data = Read(10); width = 1L + data[4] + (data[5] << 8) + (data[6] << 16); height = 1L + data[7] + (data[8] << 8) + (data[9] << 16);
            }
            else if (chunk.AsSpan(0, 4).SequenceEqual("VP8L"u8) && length >= 5) {
                var data = Read(5); if (data[0] != 0x2f) throw new InvalidDataException("WebPヘッダーが不正です。");
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(1)); width = (bits & 0x3fff) + 1; height = ((bits >> 14) & 0x3fff) + 1;
            }
            else if (chunk.AsSpan(0, 4).SequenceEqual("VP8 "u8) && length >= 10) {
                var data = Read(10); if (!data.AsSpan(3, 3).SequenceEqual(new byte[] { 0x9d, 1, 0x2a })) throw new InvalidDataException("WebPヘッダーが不正です。");
                width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6)) & 0x3fff; height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)) & 0x3fff;
            }
            else throw new NotSupportedException("このWebPヘッダーには対応していません。");
            format = MagickFormat.WebP;
        }
        else throw new NotSupportedException("対応していない画像形式です。拡張子ではなくファイルの内容を確認してください。");
        CheckSize(width, height); return new(format, width, height, captured.ToArray());
    }
}
