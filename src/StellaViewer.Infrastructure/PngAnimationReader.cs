using System.Buffers.Binary;
using StellaViewer.Core;
using LimitException = StellaViewer.Infrastructure.MagickImageDecoder.AnimationLimitException;

namespace StellaViewer.Infrastructure;

// APNG frames remain PNG images; Magick handles their pixel format, interlacing and ICC.
// This reader handles only the animation container, canvas blending and disposal.
internal static class PngAnimationReader
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private sealed record Control(int Width, int Height, int X, int Y, double Delay, byte Dispose, byte Blend);
    public static bool IsPng(Stream source)
    {
        Span<byte> signature = stackalloc byte[8]; var count = source.Read(signature); source.Position = 0;
        return count == 8 && signature.SequenceEqual(Signature);
    }
    internal static bool HasAnimationHeader(Stream source, CancellationToken token)
    {
        Span<byte> signature = stackalloc byte[8]; source.ReadExactly(signature);
        if (!signature.SequenceEqual(Signature)) return false;
        Span<byte> header = stackalloc byte[8]; var skip = new byte[65536];
        while (true) {
            token.ThrowIfCancellationRequested(); source.ReadExactly(header);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = System.Text.Encoding.ASCII.GetString(header[4..]);
            if (type == "acTL") return true;
            if (type is "IDAT" or "IEND") return false;
            long remaining = length + 4L;
            while (remaining > 0) {
                token.ThrowIfCancellationRequested(); var count = (int)Math.Min(skip.Length, remaining);
                source.ReadExactly(skip.AsSpan(0, count)); remaining -= count;
            }
        }
    }
    public static AnimationDecodeResult Read(Stream source, AnimationLimits limits, CancellationToken token)
    {
        source.Position = 8;
        byte[]? header = null; int width = 0, height = 0, declared = 0;
        uint sequence = 0; var hasImageData = false; var sawDefault = false; var sawEnd = false;
        var shared = new List<(string Type, byte[] Data)>(); long sharedBytes = 0;
        byte[]? canvas = null; Control? control = null;
        using var compressed = new MemoryStream();
        var frames = new List<AnimationFrame>();
        void FinishFrame()
        {
            if (control is null) return;
            if (!hasImageData || header is null || canvas is null) throw new InvalidDataException("APNGのフレームデータがありません。");
            using var png = new MemoryStream(); png.Write(Signature);
            var frameHeader = (byte[])header.Clone();
            BinaryPrimitives.WriteInt32BigEndian(frameHeader, control.Width); BinaryPrimitives.WriteInt32BigEndian(frameHeader.AsSpan(4), control.Height);
            WriteChunk(png, "IHDR", frameHeader);
            foreach (var item in shared) WriteChunk(png, item.Type, item.Data);
            WriteChunk(png, "IDAT", compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length))); WriteChunk(png, "IEND", []);
            var frame = MagickImageDecoder.DecodePngAnimationFrame(png.ToArray(), token, out var orientation);
            if (frame.Width != control.Width || frame.Height != control.Height) throw new InvalidDataException("APNGのフレーム寸法が一致しません。");
            var previous = control.Dispose == 2 ? (byte[])canvas.Clone() : null;
            for (var y = 0; y < control.Height; y++) {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < control.Width; x++) {
                    var src = (y * control.Width + x) * 4; var dst = ((y + control.Y) * width + x + control.X) * 4;
                    if (control.Blend == 0) frame.BgraPixels.AsSpan(src, 4).CopyTo(canvas.AsSpan(dst, 4));
                    else Blend(frame.BgraPixels, src, canvas, dst);
                }
            }
            frames.Add(new(Orient(canvas, width, height, orientation, token), control.Delay));
            if (control.Dispose == 2) canvas = previous!;
            else if (control.Dispose == 1) for (var y = 0; y < control.Height; y++)
                Array.Clear(canvas, ((y + control.Y) * width + control.X) * 4, control.Width * 4);
            control = null; compressed.SetLength(0); hasImageData = false;
        }
        Span<byte> chunkHeader = stackalloc byte[8]; Span<byte> crcBytes = stackalloc byte[4];
        while (source.Position < source.Length) {
            token.ThrowIfCancellationRequested();
            source.ReadExactly(chunkHeader);
            var length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader);
            var type = System.Text.Encoding.ASCII.GetString(chunkHeader[4..]);
            if (length > source.Length - source.Position - 4) throw new InvalidDataException("PNGチャンクが途中で切れています。");
            if (type == "IDAT" && declared == 0) return new(null);
            var recognized = type is "IHDR" or "acTL" or "fcTL" or "IDAT" or "fdAT" or "IEND"
                or "PLTE" or "tRNS" or "iCCP" or "gAMA" or "sRGB" or "cHRM" or "eXIf";
            if (header is null && type != "IHDR") throw new InvalidDataException("PNGヘッダーの順序が不正です。");
            if (!recognized) {
                if (type[0] is >= 'A' and <= 'Z') throw new NotSupportedException("未対応のPNG必須チャンクです。");
                source.Position += length + 4L; continue;
            }
            if (length > limits.PixelBytes || length > int.MaxValue) throw new LimitException("APNGの作業データがメモリ上限を超えています。");
            var data = new byte[(int)length];
            for (var offset = 0; offset < data.Length;) {
                token.ThrowIfCancellationRequested(); var size = Math.Min(65536, data.Length - offset); source.ReadExactly(data.AsSpan(offset, size)); offset += size;
            }
            source.ReadExactly(crcBytes);
            if (BinaryPrimitives.ReadUInt32BigEndian(crcBytes) != Crc(chunkHeader[4..], data)) throw new InvalidDataException("APNGのCRCが一致しません。");
            switch (type) {
                case "IHDR":
                    if (header is not null || data.Length != 13) throw new InvalidDataException("PNGヘッダーが不正です。");
                    header = data; width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data)); height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)));
                    if (width <= 0 || height <= 0) throw new InvalidDataException("PNGの寸法が不正です。");
                    break;
                case "acTL":
                    if (declared != 0 || header is null || sawDefault || data.Length != 8) throw new InvalidDataException("APNGの制御情報が不正です。");
                    var count = BinaryPrimitives.ReadUInt32BigEndian(data);
                    if (count == 0) throw new InvalidDataException("APNGのフレーム数が不正です。");
                    if (count > limits.FrameCount) throw new LimitException($"アニメーションがフレーム上限（{limits.FrameCount:N0}枚）を超えています。");
                    declared = (int)count;
                    if ((long)width * height > limits.PixelBytes / 4 / declared) throw new LimitException("アニメーションの展開画素がメモリ上限を超えています。");
                    canvas = new byte[checked(width * height * 4)]; break;
                case "fcTL":
                    if (declared == 0 || data.Length != 26 || BinaryPrimitives.ReadUInt32BigEndian(data) != sequence++) throw new InvalidDataException("APNGのフレーム制御が不正です。");
                    FinishFrame();
                    if (frames.Count >= declared) throw new InvalidDataException("APNGのフレーム数が一致しません。");
                    var w = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)); var h = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
                    var x = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12)); var y = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16));
                    if (w == 0 || h == 0 || (ulong)x + w > (uint)width || (ulong)y + h > (uint)height || data[24] > 2 || data[25] > 1)
                        throw new InvalidDataException("APNGのフレーム領域・合成方法が不正です。");
                    if (!sawDefault && (w != width || h != height || x != 0 || y != 0)) throw new InvalidDataException("APNGの先頭フレーム寸法が不正です。");
                    var numerator = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(20)); var denominator = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(22));
                    control = new((int)w, (int)h, (int)x, (int)y, numerator == 0 ? 100 : numerator * 1000.0 / (denominator == 0 ? 100 : denominator), data[24], data[25]);
                    break;
                case "IDAT":
                    sawDefault = true;
                    if (control is not null) AddData(data);
                    break;
                case "fdAT":
                    if (control is null || data.Length < 4 || BinaryPrimitives.ReadUInt32BigEndian(data) != sequence++) throw new InvalidDataException("APNGのフレーム順序が不正です。");
                    AddData(data.AsSpan(4)); break;
                case "IEND":
                    if (data.Length != 0) throw new InvalidDataException("PNGの終端が不正です。");
                    FinishFrame(); sawEnd = true; break;
                default:
                    if (sawDefault || control is not null && frames.Count > 0) break;
                    sharedBytes += data.Length;
                    if (sharedBytes > limits.PixelBytes) throw new LimitException("APNGのプロファイルがメモリ上限を超えています。");
                    shared.Add((type, data)); break;
            }
            if (sawEnd) break;
        }
        if (declared == 0) return new(null);
        if (!sawEnd || frames.Count != declared) throw new InvalidDataException("APNGのフレーム数または終端が一致しません。");
        return frames.Count < 2 ? new(null) : new(new(frames));
        void AddData(ReadOnlySpan<byte> data)
        {
            if (compressed.Length + data.Length > limits.PixelBytes) throw new LimitException("APNGの作業データがメモリ上限を超えています。");
            compressed.Write(data); hasImageData = true;
        }
    }
    private static void Blend(byte[] source, int src, byte[] target, int dst)
    {
        var sa = source[src + 3] / 255.0; var da = target[dst + 3] / 255.0; var alpha = sa + da * (1 - sa);
        for (var c = 0; c < 3; c++) target[dst + c] = alpha == 0 ? (byte)0 : (byte)Math.Clamp(Math.Round((source[src + c] * sa + target[dst + c] * da * (1 - sa)) / alpha), 0, 255);
        target[dst + 3] = (byte)Math.Round(alpha * 255);
    }
    private static DecodedImage Orient(byte[] source, int width, int height, int orientation, CancellationToken token)
    {
        var swap = orientation is >= 5 and <= 8; var outputWidth = swap ? height : width; var outputHeight = swap ? width : height;
        var result = new byte[source.Length];
        for (var y = 0; y < height; y++) {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++) {
                var (dx, dy) = orientation switch {
                    2 => (width - x - 1, y), 3 => (width - x - 1, height - y - 1), 4 => (x, height - y - 1),
                    5 => (y, x), 6 => (height - y - 1, x), 7 => (height - y - 1, width - x - 1), 8 => (y, width - x - 1), _ => (x, y)
                };
                source.AsSpan((y * width + x) * 4, 4).CopyTo(result.AsSpan((dy * outputWidth + dx) * 4, 4));
            }
        }
        return new(outputWidth, outputHeight, width, height, result);
    }
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value => {
        var crc = (uint)value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0); return crc;
    }).ToArray();
    private static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (var b in type) Update(b); foreach (var b in data) Update(b); return ~crc;
        void Update(byte b) => crc = (crc >> 8) ^ CrcTable[(byte)(crc ^ b)];
    }
    private static void WriteChunk(Stream target, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length); target.Write(number);
        var name = System.Text.Encoding.ASCII.GetBytes(type); target.Write(name); target.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(name, data)); target.Write(number);
    }
}
