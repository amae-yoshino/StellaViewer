using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;
using ImageMagick.Drawing;
using StellaViewer.Infrastructure;

internal static class PsdChecks
{
    public static async Task<int> RunAsync(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var count = 0; var decoder = new MagickImageDecoder();
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
        async Task Reject(byte[] data, string fragment, string label) {
            using var input = new MemoryStream(data);
            try { await decoder.DecodeStreamAsync(input, default, 0); }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException) { Check(e.Message.Contains(fragment), label + ": " + e.Message); return; }
            throw new Exception(label + " was accepted");
        }
        foreach (var depth in new[] { 8, 16 }) foreach (var rle in new[] { false, true }) {
            var file = Path.Combine(output, $"rgb-{depth}-{(rle ? "rle" : "raw")}.psd");
            var bytes = Build(depth, rle); File.WriteAllBytes(file, bytes);
            var hash = SHA256.HashData(bytes); var image = await decoder.DecodeAsync(file, default);
            Check(image.Width == 4 && image.Height == 2 && image.BgraPixels.Length == 32, $"PSD {depth}bit {rle} dimensions");
            Check(image.BgraPixels[2] == 255 && image.BgraPixels[1] == 32 && image.BgraPixels[0] == 0 && image.BgraPixels[3] == 255
                && image.BgraPixels[16 + 2] == 128 && image.BgraPixels[16] == 255, $"PSD {depth}bit {rle} row offsets and RGB channels");
            Check(await decoder.EstimatePixelBytesAsync(file, default) == 32, "PSD size probe matches display buffer");
            var thumbnail = await decoder.DecodeAsync(file, default, 2);
            Check(thumbnail.Width == 2 && thumbnail.Height == 1 && thumbnail.OriginalWidth == 4 && thumbnail.OriginalHeight == 2, "PSD thumbnail retains original dimensions");
            Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))), "PSD original bytes unchanged");
        }
        var alpha = Build(alpha: true);
        File.WriteAllBytes(Path.Combine(output, "alpha.psd"), alpha);
        using (var input = new ShortReadStream(alpha)) {
            var image = await decoder.DecodeStreamAsync(input, default, 0);
            Check(image.BgraPixels[3] == 128 && image.BgraPixels[2] == 255 && image.BgraPixels[0] <= 1, "PSD merged transparency removes white matte on nonseekable short reads");
            Check(input.CanRead, "PSD decode leaves caller stream open");
        }
        using (var input = new MemoryStream(Build(selection: true))) {
            var image = await decoder.DecodeStreamAsync(input, default, 0);
            Check(image.BgraPixels[3] == 255 && image.BgraPixels[0] == 0, "PSD selection alpha is not merged transparency");
        }
        using (var input = new MemoryStream(Build(depth:16, alpha:true, additional16:true))) {
            var image = await decoder.DecodeStreamAsync(input, default, 0);
            Check(image.BgraPixels[3] == 128 && image.BgraPixels[0] <= 1, "PSD 16bit additional layer transparency marker");
        }
        using var metadata = new MemoryStream();
        using (var writer = new BinaryWriter(metadata, Encoding.ASCII, true)) {
            Resource(writer, 1039, ColorProfiles.AdobeRGB1998.ToByteArray());
            var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, (ushort)6);
            Resource(writer, 1058, exif.ToByteArray()!);
            Resource(writer, 1060, [1,2,3]); // Unknown, odd-length resource.
        }
        using (var input = new MemoryStream(Build(resources: metadata.ToArray(), neutral:true))) {
            var image = await decoder.DecodeStreamAsync(input, default, 0);
            Check(image.Width == 2 && image.Height == 4 && image.OriginalWidth == 4, "PSD EXIF auto orientation");
            Check(image.BgraPixels[2] is >= 128 and <= 131 && image.BgraPixels[2] == image.BgraPixels[1], "PSD Adobe RGB ICC conversion");
        }
        var oversize = Build(); BinaryPrimitives.WriteUInt32BigEndian(oversize.AsSpan(14), 8193); BinaryPrimitives.WriteUInt32BigEndian(oversize.AsSpan(18), 8192);
        await Reject(oversize, "256 MiB", "PSD rejects display budget before allocating pixels");
        var limitPath = Path.Combine(output, "oversize.psd"); File.WriteAllBytes(limitPath, oversize);
        try { await decoder.EstimatePixelBytesAsync(limitPath, default); throw new Exception("Size probe accepted oversized PSD"); }
        catch (NotSupportedException e) { Check(e.Message.Contains("256 MiB"), "PSD size probe enforces same budget"); }
        var psb = Build(); psb[5] = 2; await Reject(psb, "PSB", "PSB fails with reason");
        var mode = Build(); mode[25] = 4; await Reject(mode, "カラーモード", "PSD CMYK fails with reason");
        var depth32 = Build(); depth32[23] = 32; await Reject(depth32, "32bit", "PSD 32bit fails with reason");
        var zip = Build(); zip[39] = 2; await Reject(zip, "圧縮方式", "PSD ZIP fails with reason");
        var zipPrediction = Build(); zipPrediction[39] = 3; await Reject(zipPrediction, "圧縮方式", "PSD ZIP prediction fails with reason");
        var reserved = Build(); reserved[6] = 1; await Reject(reserved, "予約領域", "PSD rejects invalid reserved header");
        await Reject(Build()[..^1], "途中", "PSD raw requires exact channel bytes");
        var rleUnderflow = Build(rle:true); rleUnderflow[52] = 128; rleUnderflow[53] = 128; await Reject(rleUnderflow, "不足", "PSD RLE rejects underfilled rows");
        var rleOverflow = Build(rle:true); rleOverflow[52] = 250; await Reject(rleOverflow, "画像幅", "PSD RLE rejects overfilled rows");
        var rleBoundary = Build(rle:true); rleBoundary[41] = 1; await Reject(rleBoundary, "セクション", "PSD RLE honors compressed row byte counts");
        var truncatedLength = Build(resources:metadata.ToArray()); BinaryPrimitives.WriteUInt32BigEndian(truncatedLength.AsSpan(42), uint.MaxValue);
        await Reject(truncatedLength, "セクション", "PSD invalid resource length rejected");
        using var versionInfo = new MemoryStream(); using (var writer = new BinaryWriter(versionInfo, Encoding.ASCII, true)) Resource(writer,1057,[0,0,0,1,0]);
        await Reject(Build(resources:versionInfo.ToArray()), "合成画像", "PSD absent compatibility merged image rejected");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await decoder.DecodeAsync(Path.Combine(output,"alpha.psd"),cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check(true,"PSD pre-cancellation"); }
        Check((await decoder.DecodeAsync(Path.Combine(output,"alpha.psd"),default)).Width == 4, "PSD decoder usable after errors/cancellation");
        var archivePath = Path.Combine(output,"psd.zip");
        using (var archive = new ZipArchive(File.Create(archivePath), ZipArchiveMode.Create)) {
            archive.CreateEntryFromFile(Path.Combine(output,"rgb-16-rle.psd"),"chapter/1.psd");
            archive.CreateEntryFromFile(Path.Combine(output,"alpha.psd"),"chapter/2.psd");
        }
        using (var archive = await ArchiveSession.OpenAsync(archivePath,default)) {
            var entry = new ArchiveLocation(archivePath,"chapter/1.psd",false);
            var image = await archive.DecodeAsync(entry,default);
            Check(image.Width == 4 && image.BgraPixels[18] == 128,"PSD archive entry uses bounded PSD adapter");
        }
        Check(MagickImageDecoder.Supports("test.PSD"), "PSD extension supported case insensitively");
        // Independent native writer produces a structurally complete PSD, not our synthetic section fixture.
        var nativePath = Path.Combine(output,"native.psd");
        using (var image = new MagickImage(new MagickColor("#FF000080"),4,2)) {
            image.Settings.Compression = CompressionMethod.RLE; image.Depth = 8; image.Write(nativePath,MagickFormat.Psd);
        }
        var native = await decoder.DecodeAsync(nativePath,default);
        Check(native.BgraPixels[2] >= 250 && native.BgraPixels[0] <= 2 && native.BgraPixels[3] is >= 127 and <= 129,"PSD independently written merged alpha and layer sections");
        var native16Path = Path.Combine(output,"native16.psd");
        using (var image = new MagickImage(new MagickColor("#FF000080"),4,2)) {
            image.Settings.Compression = CompressionMethod.RLE; image.Depth = 16; image.Write(native16Path,MagickFormat.Psd);
        }
        var native16 = await decoder.DecodeAsync(native16Path,default);
        Check(native16.BgraPixels[2] >= 250 && native16.BgraPixels[0] <= 2 && native16.BgraPixels[3] is >= 127 and <= 129,"PSD independently written 16bit merged transparency");
        var literal = Build(depth:16,rle:true,literal:true);
        using (var input = new MemoryStream(literal)) {
            var image = await decoder.DecodeStreamAsync(input,default,0);
            Check(image.BgraPixels[2] == 255 && image.BgraPixels[6] == 254 && image.BgraPixels[18] == 128,"PSD RLE literal packets and 16bit high-byte conversion");
        }
        var cachePath = Path.Combine(output,"cache-input.psd"); File.WriteAllBytes(cachePath,Build());
        var disk = Path.Combine(output,"cache"); var cache = new DecodedImageCache(decoder,disk); cache.Configure(new(0,1,0));
        await cache.GetAsync(cachePath,default);
        var restart = new DecodedImageCache(decoder,disk); restart.Configure(new(0,1,0));
        Check((await restart.GetAsync(cachePath,default)).BgraPixels[2] == 255 && restart.DiskHits == 1,"PSD decoded disk cache survives a new reader");
        using (var image = new MagickImage(MagickColors.CornflowerBlue,640,480)) {
            image.Draw(new Drawables().FillColor(MagickColors.Gold).Rectangle(60,60,290,420).FillColor(MagickColors.White).Circle(450,240,550,240));
            image.Settings.Compression = CompressionMethod.RLE; image.Depth = 8; image.Write(Path.Combine(output,"display.psd"),MagickFormat.Psd);
        }
        return count;
    }

    private static byte[] Build(int depth=8, bool rle=false, bool alpha=false, bool selection=false, bool additional16=false, byte[]? resources=null, bool neutral=false, bool literal=false)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write("8BPS"u8); U16(writer,1); writer.Write(new byte[6]); U16(writer,alpha || selection ? 4 : 3);
        U32(writer,2); U32(writer,4); U16(writer,depth); U16(writer,3); U32(writer,0);
        U32(writer,resources?.Length ?? 0); if(resources is not null) writer.Write(resources);
        using var layers = new MemoryStream(); using (var layerWriter = new BinaryWriter(layers,Encoding.ASCII,true)) {
            if(alpha || selection) {
                U32(layerWriter,additional16 ? 0 : 2); if(!additional16) U16(layerWriter,alpha ? 65535 : 1);
                U32(layerWriter,0);
                if(additional16) { layerWriter.Write("8BIMLr16"u8); U32(layerWriter,2); U16(layerWriter,65535); U16(layerWriter,0); }
            }
        }
        U32(writer,(int)layers.Length); writer.Write(layers.ToArray()); U16(writer,rle ? 1 : 0);
        var channels = alpha || selection ? 4 : 3; var rows = new List<byte[]>();
        for(var c=0;c<channels;c++) for(var y=0;y<2;y++) {
            byte value = c == 3 ? (byte)128 : neutral ? (byte)128 : c == 0 ? (y == 0 ? (byte)255 : (byte)128) : c == 1 ? (byte)32 : (y == 0 ? (byte)0 : (byte)255);
            if(alpha && c < 3) value = (byte)((value * 128 + 255 * 127 + 127) / 255);
            var row = new byte[4 * depth/8]; for(var x=0;x<4;x++) { row[x*depth/8]=(byte)Math.Max(0,value-(literal ? x : 0)); if(depth==16) row[x*2+1]=literal ? (byte)(19+x) : value; }
            if(rle) rows.Add(literal ? [(byte)(row.Length-1),..row] : [(byte)(257-row.Length),row[0]]); else rows.Add(row);
        }
        if(rle) foreach(var row in rows) U16(writer,row.Length);
        foreach(var row in rows) writer.Write(row);
        return output.ToArray();
    }
    private static void Resource(BinaryWriter writer,int id,byte[] bytes)
    {
        writer.Write("8BIM"u8); U16(writer,id); U16(writer,0); U32(writer,bytes.Length); writer.Write(bytes); if(bytes.Length%2!=0)writer.Write((byte)0);
    }
    private static void U16(BinaryWriter writer,int value) { Span<byte> data=stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(data,(ushort)value); writer.Write(data); }
    private static void U32(BinaryWriter writer,int value) { Span<byte> data=stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(data,(uint)value); writer.Write(data); }
    private sealed class ShortReadStream(byte[] data) : Stream
    {
        private int offset;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int start,int count) => Read(buffer.AsSpan(start,count));
        public override int Read(Span<byte> buffer) { var count=Math.Min(1,Math.Min(buffer.Length,data.Length-offset)); data.AsSpan(offset,count).CopyTo(buffer); offset+=count; return count; }
        public override void Flush() {} public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException(); public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
