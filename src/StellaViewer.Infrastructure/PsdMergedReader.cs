using System.Buffers.Binary;
using System.Text;

namespace StellaViewer.Infrastructure;

/// <summary>Bounded, forward-only PSD v1 merged-image reader. Layer pixels are never loaded.</summary>
internal sealed class PsdMergedReader(Stream stream, CancellationToken token)
{
    internal const long MaximumPixelBytes = 256L * 1024 * 1024;
    private const int MaximumMetadataBytes = 16 * 1024 * 1024;
    private long position;
    private readonly byte[] scratch = new byte[16 * 1024];
    internal sealed record Header(int Width, int Height, short Channels, short Depth);
    internal sealed record Result(Header Header, byte[] Rgba, byte[]? Icc, byte[]? Exif);

    internal Header ReadHeader()
    {
        if (Text(4) != "8BPS") throw Bad("署名が不正です。");
        var version = U16();
        if (version != 1) throw new NotSupportedException("PSB／PSDバージョン2以降は未対応です。");
        var reserved = Bytes(6);
        if (reserved.Any(b => b != 0)) throw Bad("予約領域が不正です。");
        var channels = U16(); var height = U32(); var width = U32(); var depth = U16(); var mode = U16();
        if (channels is < 1 or > 24 || width is < 1 or > 30000 || height is < 1 or > 30000)
            throw Bad("画像サイズまたはチャンネル数が不正です。");
        if (mode != 3) throw new NotSupportedException($"PSDのRGB以外のカラーモード（{mode}）は未対応です。");
        if (channels < 3) throw Bad("RGBチャンネルが不足しています。");
        if (depth is not (8 or 16)) throw new NotSupportedException($"PSDの{depth}bit画像は未対応です（8bit／16bit RGBのみ対応）。");
        if ((long)width * height * 4 > MaximumPixelBytes)
            throw new NotSupportedException("PSDの表示用画素が暫定上限256 MiBを超えるため、読み込みを中止しました。");
        return new((int)width, (int)height, (short)channels, (short)depth);
    }

    internal Result Read()
    {
        var header = ReadHeader();
        Skip(U32()); // Color-mode data is unused for RGB.
        byte[]? icc = null, exif = null;
        var metadataBytes = 0L;
        var resourceEnd = End(U32());
        while (position < resourceEnd) {
            Require(resourceEnd, 7);
            if (Text(4) is not ("8BIM" or "MeSa")) throw Bad("画像リソースの署名が不正です。");
            var id = U16(); var nameLength = Byte();
            var nameBytes = nameLength + ((nameLength + 1) % 2);
            Require(resourceEnd, nameBytes + 4); Skip(nameBytes);
            var length = U32(); Require(resourceEnd, (long)length + (length & 1));
            if (id is 1039 or 1058 or 1059 or 1057) {
                metadataBytes += length;
                if (metadataBytes > MaximumMetadataBytes) throw Bad("ICC／EXIF等のメタデータが読み込み上限16 MiBを超えています。");
                var data = Bytes((int)length);
                if (id == 1039) icc = data;
                else if (id is 1058 or 1059) exif ??= data;
                else if (data.Length < 5) throw Bad("VersionInfoが不正です。");
                else if (data[4] == 0) throw new NotSupportedException("このPSDには互換表示用の合成画像が保存されていません。");
            }
            else Skip(length);
            Skip(length & 1);
        }
        var layersEnd = End(U32());
        var transparency = false;
        if (position < layersEnd) {
            Require(layersEnd, 4); var layerLength = U32(); Require(layersEnd, layerLength);
            if (layerLength > 0) {
                if (layerLength < 2) throw Bad("レイヤー情報の長さが不正です。");
                transparency = unchecked((short)U16()) < 0; Skip(layerLength - 2);
            }
            if (position < layersEnd) {
                Require(layersEnd, 4); var maskLength = U32(); Require(layersEnd, maskLength); Skip(maskLength);
            }
            while (position < layersEnd) {
                // Global additional layer information is padded to a multiple of four.
                Require(layersEnd, 12);
                if (Text(4) is not ("8BIM" or "8B64")) throw Bad("追加レイヤー情報の署名が不正です。");
                var key = Text(4); var length = U32(); var padding = (4 - length % 4) % 4;
                Require(layersEnd, (long)length + padding);
                if (key == "Mtrn" && header.Depth == 8 || key == "Mt16" && header.Depth == 16) transparency = true;
                if (key == "Lr16" && header.Depth == 16) {
                    if (length < 2) throw Bad("16bitレイヤー情報が不正です。");
                    transparency |= unchecked((short)U16()) < 0; Skip(length - 2);
                }
                else Skip(length);
                Skip(padding);
            }
        }
        if (transparency && header.Channels < 4) throw Bad("合成透明度のチャンネルがありません。");
        var compression = U16();
        if (compression > 1) throw new NotSupportedException($"PSDの圧縮方式（{compression}）は未対応です（Raw／RLEのみ対応）。");
        var bytesPerRow = checked(header.Width * (header.Depth / 8));
        var planeBytes = checked(bytesPerRow * header.Height);
        var planeCount = transparency ? 4 : 3;
        var planes = new byte[planeCount][];
        // Retain only RGB and confirmed merged transparency, never selection/spot channels.
        ushort[]? rowLengths = null;
        if (compression == 1) {
            rowLengths = new ushort[checked(header.Height * header.Channels)];
            for (var i = 0; i < rowLengths.Length; i++) rowLengths[i] = U16();
        }
        var discardedRow = new byte[bytesPerRow];
        for (var channel = 0; channel < header.Channels; channel++) {
            token.ThrowIfCancellationRequested();
            if (channel < planeCount) planes[channel] = new byte[planeBytes];
            for (var row = 0; row < header.Height; row++) {
                var target = channel < planeCount ? planes[channel].AsSpan(row * bytesPerRow, bytesPerRow) : discardedRow;
                if (rowLengths is null) ReadExactly(target);
                else ReadRle(target, rowLengths[channel * header.Height + row]);
            }
        }
        token.ThrowIfCancellationRequested();
        var psd = new bzPSD.PsdFile { Columns = header.Width, Rows = header.Height, Depth = header.Depth,
            Channels = (short)planeCount, ColorMode = bzPSD.ColorMode.RGB, ImageData = planes };
        using var bitmap = bzPSD.ImageDecoder.DecodeImage(psd);
        token.ThrowIfCancellationRequested();
        // Photoshop's merged RGB is white-matted. Undo that before ICC conversion/straight-alpha display.
        if (transparency) {
            for (var i = 0; i < bitmap.Pixels.Length; i += 4) {
                if ((i & 0xffff) == 0) token.ThrowIfCancellationRequested();
                var alpha = bitmap.Pixels[i + 3];
                if (alpha is 0 or 255) continue;
                for (var c = 0; c < 3; c++) bitmap.Pixels[i + c] = (byte)Math.Clamp(
                    (int)Math.Round((bitmap.Pixels[i + c] * 255.0 - (255 - alpha) * 255.0) / alpha), 0, 255);
            }
        }
        return new(header, bitmap.Pixels, icc, exif);
    }

    private void ReadRle(Span<byte> target, int length)
    {
        var end = End(length); var written = 0;
        while (position < end) {
            var code = Byte();
            if (code == 128) continue;
            var count = code < 128 ? code + 1 : 257 - code;
            if (count > target.Length - written) throw Bad("RLEの行データが画像幅を超えています。");
            Require(end, code < 128 ? count : 1);
            if (code < 128) ReadExactly(target.Slice(written, count));
            else target.Slice(written, count).Fill(Byte());
            written += count;
        }
        if (written != target.Length) throw Bad("RLEの行データが不足しています。");
    }
    private void Require(long end, long count) { if (count < 0 || position > end - count) throw Bad("セクションの長さが不正です。"); }
    private long End(long length) => checked(position + length);
    private byte Byte() { ReadExactly(scratch.AsSpan(0, 1)); return scratch[0]; }
    private ushort U16() { ReadExactly(scratch.AsSpan(0, 2)); return BinaryPrimitives.ReadUInt16BigEndian(scratch); }
    private uint U32() { ReadExactly(scratch.AsSpan(0, 4)); return BinaryPrimitives.ReadUInt32BigEndian(scratch); }
    private string Text(int length) => Encoding.ASCII.GetString(Bytes(length));
    private byte[] Bytes(int length) { var data = new byte[length]; ReadExactly(data); return data; }
    private void Skip(long length) { while (length > 0) { var count = (int)Math.Min(length, scratch.Length); ReadExactly(scratch.AsSpan(0, count)); length -= count; } }
    private void ReadExactly(Span<byte> target)
    {
        while (!target.IsEmpty) {
            token.ThrowIfCancellationRequested(); var count = stream.Read(target);
            if (count == 0) throw Bad("ファイルが途中で切れています。");
            position += count; target = target[count..];
        }
    }
    private static InvalidDataException Bad(string reason) => new("PSD: " + reason);
}
