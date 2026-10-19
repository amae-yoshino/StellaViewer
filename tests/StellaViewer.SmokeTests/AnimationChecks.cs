using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using ImageMagick;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

internal static class AnimationChecks
{
    public static async Task<int> RunAsync(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output); var decoder = new MagickImageDecoder(); var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); checks++; }
        var full = new[] { new Frame(4,4,0,0,255,0,0,255,0,0,1), new Frame(4,4,0,0,0,255,0,255,0,0,2), new Frame(4,4,0,0,0,0,255,255,0,0,3) };
        foreach (var extension in new[] { "gif", "webp" }) {
            using var collection = new MagickImageCollection();
            for (var index = 0; index < 3; index++) {
                var color = new[] { MagickColors.Red, MagickColors.Lime, MagickColors.Blue }[index];
                collection.Add(new MagickImage(color, 4, 4) { AnimationDelay = (uint)((index + 1) * 10), AnimationTicksPerSecond = 100, AnimationIterations = 1 });
            }
            collection.Write(Path.Combine(output, "animated." + extension));
        }
        WriteApng(Path.Combine(output, "animated.png"), full);
        foreach (var extension in new[] { "gif", "webp", "png" }) {
            var path = Path.Combine(output, "animated." + extension); var before = SHA256.HashData(File.ReadAllBytes(path));
            var result = await decoder.DecodeAnimationAsync(path, CancellationToken.None); var animation = result.Animation!;
            Check(animation is not null && animation.Frames.Count == 3 && result.Warning is null, extension + " animation reads all frames");
            Check(animation!.Frames.All(f => f.Image.Width == 4 && f.Image.Height == 4), extension + " animation uses full canvas");
            Check(animation.Frames[0].Image.BgraPixels[2] > 240 && animation.Frames[1].Image.BgraPixels[1] > 240 && animation.Frames[2].Image.BgraPixels[0] > 240, extension + " animation preserves frame colors");
            Check(animation.Frames.Select(f => f.DelayMilliseconds).SequenceEqual(new double[] { 100, 200, 300 }), extension + " frame durations");
            Check(animation.FrameAt(0) == 0 && animation.FrameAt(100) == 1 && animation.FrameAt(300) == 2 && animation.FrameAt(600) == 0 && animation.FrameAt(1300) == 1, extension + " playback loops despite finite source loop count");
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), extension + " animation leaves original bytes unchanged");
            Check((await decoder.DecodeAnimationAsync(path, CancellationToken.None, new(FrameCount: 2))).Warning is not null, extension + " frame limit falls back before full decode");
            Check((await decoder.DecodeAnimationAsync(path, CancellationToken.None, new(PixelBytes: 128))).Warning is not null, extension + " expanded-pixel limit falls back");
        }
        var complex = new[] { full[0], new Frame(2,2,1,1,0,255,0,128,2,1,2), new Frame(1,1,0,0,0,0,255,255,1,0,3), new Frame(1,1,3,3,0,0,0,0,0,0,1) };
        var complexPath = Path.Combine(output, "disposal.png"); WriteApng(complexPath, complex);
        var composited = (await decoder.DecodeAnimationAsync(complexPath, CancellationToken.None)).Animation!;
        var center = (1 * 4 + 1) * 4;
        Check(composited.Frames[1].Image.BgraPixels[center + 1] is >= 127 and <= 129 && composited.Frames[1].Image.BgraPixels[center + 2] is >= 126 and <= 128, "APNG over blends straight alpha with previous canvas");
        Check(composited.Frames[2].Image.BgraPixels[center + 2] == 255 && composited.Frames[2].Image.BgraPixels[0] == 255, "APNG previous disposal restores earlier canvas");
        Check(composited.Frames[3].Image.BgraPixels[3] == 0 && composited.Frames[3].Image.BgraPixels[(3 * 4 + 3) * 4 + 3] == 0, "APNG background disposal and source blend clear alpha");
        var excludedPath = Path.Combine(output, "default-excluded.png"); WriteApng(excludedPath, full, true);
        Check((await decoder.DecodeAnimationAsync(excludedPath, CancellationToken.None)).Animation!.Frames.Count == 3, "APNG excludes non-animation default image");
        var staticPath = Path.Combine(output, "static.png"); using (var image = new MagickImage(MagickColors.Red, 4, 4)) image.Write(staticPath);
        Check((await decoder.DecodeAnimationAsync(staticPath, CancellationToken.None)).Animation is null, "Static PNG is not animated");
        var badPath = Path.Combine(output, "bad.png"); var bad = File.ReadAllBytes(complexPath); bad[^1] ^= 1; File.WriteAllBytes(badPath, bad);
        var rejected = false; try { await decoder.DecodeAnimationAsync(badPath, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "APNG rejects damaged CRC");
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); rejected = false;
        try { await decoder.DecodeAnimationAsync(complexPath, canceled.Token); } catch (OperationCanceledException) { rejected = true; }
        Check(rejected, "Animation supports pre-cancellation");
        Check((await decoder.DecodeAnimationAsync(complexPath, CancellationToken.None)).Animation is not null, "Animation decoder remains usable after failure and cancellation");
        // Two delta frames rely on the GIF previous-disposal rule.
        var deltaPath = Path.Combine(output, "disposal.gif");
        using (var images = new MagickImageCollection()) {
            images.Add(new MagickImage(MagickColors.Red, 4, 4) { AnimationDelay = 10, GifDisposeMethod = GifDisposeMethod.None });
            images.Add(new MagickImage(MagickColors.Lime, 2, 2) { Page = new MagickGeometry(1,1,4,4), AnimationDelay = 10, GifDisposeMethod = GifDisposeMethod.Previous });
            images.Add(new MagickImage(MagickColors.Blue, 1, 1) { Page = new MagickGeometry(0,0,4,4), AnimationDelay = 10 }); images.Write(deltaPath);
        }
        var delta = (await decoder.DecodeAnimationAsync(deltaPath, CancellationToken.None)).Animation!;
        Check(delta.Frames[1].Image.BgraPixels[center + 1] > 240 && delta.Frames[2].Image.BgraPixels[center + 2] > 240, "GIF delta frames honor previous disposal and offsets");
        var zeroPng = Path.Combine(output, "zero-duration.png"); WriteApng(zeroPng, full.Select(f => f with { Delay = 0 }).ToArray());
        Check((await decoder.DecodeAnimationAsync(zeroPng, CancellationToken.None)).Animation!.Frames.All(f => f.DelayMilliseconds == 100), "APNG zero frame duration uses approved 100 ms fallback");
        var zeroGif = Path.Combine(output, "zero-duration.gif");
        using (var images = new MagickImageCollection()) {
            images.Add(new MagickImage(MagickColors.Red, 4, 4) { AnimationDelay = 0 }); images.Add(new MagickImage(MagickColors.Blue, 4, 4) { AnimationDelay = 0 }); images.Write(zeroGif);
        }
        Check((await decoder.DecodeAnimationAsync(zeroGif, CancellationToken.None)).Animation!.Frames.All(f => f.DelayMilliseconds == 100), "GIF zero frame duration uses approved 100 ms fallback");
        var palettePath = Path.Combine(output, "palette.png");
        WriteApng(palettePath, full.Select((f,i) => f with { R = (byte)i }).ToArray(), palette: true);
        var palette = (await decoder.DecodeAnimationAsync(palettePath, CancellationToken.None)).Animation!;
        Check(palette.Frames[0].Image.BgraPixels[2] == 255 && palette.Frames[1].Image.BgraPixels[1] == 255
            && palette.Frames[1].Image.BgraPixels[3] == 128 && palette.Frames[2].Image.BgraPixels[3] == 0, "Palette APNG shares palette and transparent entries across frames");
        byte[] metadataPng;
        using (var image = new MagickImage(MagickColors.Red, 4, 4)) {
            var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, (ushort)6); image.SetProfile(exif);
            image.Orientation = Orientation.RightTop; image.SetProfile(ColorProfiles.SRGB);
            image.Settings.SetDefines(new ImageMagick.Formats.PngWriteDefines { IncludeChunks = ImageMagick.Formats.PngChunkFlags.All, PreserveiCCP = true });
            metadataPng = image.ToByteArray(MagickFormat.Png);
        }
        var metadata = new List<(string, byte[])>();
        for (var offset = 8; offset < metadataPng.Length;) {
            var length = BinaryPrimitives.ReadInt32BigEndian(metadataPng.AsSpan(offset)); var type = System.Text.Encoding.ASCII.GetString(metadataPng, offset + 4, 4);
            if (type is "iCCP" or "eXIf") metadata.Add((type, metadataPng.AsSpan(offset + 8, length).ToArray())); offset += length + 12;
        }
        Check(metadata.Any(c => c.Item1 == "iCCP") && metadata.Any(c => c.Item1 == "eXIf"), "APNG metadata fixture includes real ICC and EXIF chunks");
        var orientedPath = Path.Combine(output, "oriented.png"); WriteApng(orientedPath, [full[0], new Frame(1,1,0,0,0,255,0,255,0,0,1)], metadata: metadata);
        var oriented = (await decoder.DecodeAnimationAsync(orientedPath, CancellationToken.None)).Animation!;
        Check(oriented.Frames[1].Image.BgraPixels[3 * 4 + 1] == 255 && oriented.Frames[1].Image.BgraPixels[1] == 0,
            "APNG applies EXIF after compositing canvas and reads ICC through static decoder");
        var spread = Path.Combine(output, "spread"); Directory.CreateDirectory(spread);
        File.Copy(Path.Combine(output, "animated.gif"), Path.Combine(spread, "1.gif"), true);
        File.Copy(Path.Combine(output, "animated.webp"), Path.Combine(spread, "2.webp"), true);
        File.Copy(staticPath, Path.Combine(spread, "3.png"), true);
        var zipPath = Path.Combine(output, "animations.zip");
        using (var file = File.Create(zipPath)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create)) {
            zip.CreateEntryFromFile(Path.Combine(output, "animated.gif"), "1.gif"); zip.CreateEntryFromFile(Path.Combine(output, "animated.png"), "2.png"); zip.CreateEntryFromFile(staticPath, "3.png");
        }
        using (var archive = await ArchiveSession.OpenAsync(zipPath, CancellationToken.None)) {
            Check(await archive.HasPngAnimationAsync(new(zipPath, "2.png", false), CancellationToken.None), "Archive detects APNG without extracting ordinary PNGs");
            Check(!await archive.HasPngAnimationAsync(new(zipPath, "3.png", false), CancellationToken.None), "Archive static PNG needs no animation extraction");
        }
        using (var file = File.Create(Path.Combine(output, "outer.zip"))) using (var zip = new ZipArchive(file, ZipArchiveMode.Create)) zip.CreateEntryFromFile(zipPath, "inner.zip");
        Console.WriteLine($"Animation: {checks} checks passed."); return checks;
    }
    internal sealed record Frame(int Width, int Height, int X, int Y, byte R, byte G, byte B, byte A, byte Dispose, byte Blend, ushort Delay);
    internal static void WriteApng(string path, Frame[] frames, bool excluded = false, bool palette = false, List<(string Type, byte[] Data)>? metadata = null)
    {
        using var file = File.Create(path); file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] data) {
            var size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, data.Length); file.Write(size);
            var type = System.Text.Encoding.ASCII.GetBytes(name); file.Write(type); file.Write(data); uint crc = uint.MaxValue;
            foreach (var b in type.Concat(data)) { crc ^= b; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0); }
            BinaryPrimitives.WriteUInt32BigEndian(size, ~crc); file.Write(size);
        }
        byte[] Pixels(Frame frame) {
            using var data = new MemoryStream();
            using (var zip = new ZLibStream(data, CompressionLevel.Fastest, true)) for (var y = 0; y < frame.Height; y++) {
                zip.WriteByte(0); for (var x = 0; x < frame.Width; x++) { if (palette) zip.WriteByte(frame.R); else zip.Write(new byte[] { frame.R, frame.G, frame.B, frame.A }); }
            }
            return data.ToArray();
        }
        var header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, 4); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 4); header[8] = 8; header[9] = palette ? (byte)3 : (byte)6; Chunk("IHDR", header);
        if (palette) { Chunk("PLTE", [255,0,0,0,255,0,0,0,255]); Chunk("tRNS", [255,128,0]); }
        if (metadata is not null) foreach (var item in metadata) Chunk(item.Type, item.Data);
        var animation = new byte[8]; BinaryPrimitives.WriteUInt32BigEndian(animation, (uint)frames.Length); BinaryPrimitives.WriteUInt32BigEndian(animation.AsSpan(4), 1); Chunk("acTL", animation);
        if (excluded) Chunk("IDAT", Pixels(new(4,4,0,0,0,0,0,255,0,0,1)));
        uint sequence = 0;
        for (var index = 0; index < frames.Length; index++) {
            var frame = frames[index]; var control = new byte[26]; BinaryPrimitives.WriteUInt32BigEndian(control, sequence++);
            BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(4), frame.Width); BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(8), frame.Height);
            BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(12), frame.X); BinaryPrimitives.WriteInt32BigEndian(control.AsSpan(16), frame.Y);
            BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(20), frame.Delay); BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(22), 10); control[24] = frame.Dispose; control[25] = frame.Blend; Chunk("fcTL", control);
            var compressed = Pixels(frame);
            if (index == 0 && !excluded) Chunk("IDAT", compressed);
            else { var frameData = new byte[compressed.Length + 4]; BinaryPrimitives.WriteUInt32BigEndian(frameData, sequence++); compressed.CopyTo(frameData, 4); Chunk("fdAT", frameData); }
        }
        Chunk("IEND", []);
    }
}
