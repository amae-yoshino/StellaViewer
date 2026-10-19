using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using ImageMagick;
using StellaViewer.Infrastructure;

internal static class ClipChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        output=Path.GetFullPath(output); Directory.CreateDirectory(output);
        var checks=0; var decoder=new MagickImageDecoder();
        void Check(bool value,string label) { if(!value)throw new Exception(label); checks++; Console.WriteLine("PASS: "+label); }
        async Task<string> Fixture(string name,string png,int w,int h,string kind="normal") {
            var path=Path.Combine(output,name+".clip");
            var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"StellaViewer.ClipFixtures.exe")) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
            foreach(var arg in new[]{path,png,w.ToString(),h.ToString(),kind})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;var errors=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
            if(process.ExitCode!=0)throw new Exception(await errors);return path;
        }
        async Task Reject(byte[] bytes,string fragment,string label) {
            using var input=new MemoryStream(bytes);
            try {await decoder.DecodeStreamAsync(input,default,0);}
            catch(Exception error) when(error is InvalidDataException or NotSupportedException) {Check(error.Message.Contains(fragment),label+": "+error.Message);return;}
            throw new Exception(label+" was accepted");
        }
        var pngPath=Path.Combine(output,"preview.png");
        using(var image=new MagickImage(new MagickColor("#FF000080"),8,6))image.Write(pngPath);
        var same=await Fixture("1-full",pngPath,8,6); var smaller=await Fixture("2-small",pngPath,1600,1200);
        foreach(var path in new[]{same,smaller}) {
            var hash=SHA256.HashData(File.ReadAllBytes(path));var image=await decoder.DecodeAsync(path,default);
            Check(image.Width==8&&image.Height==6&&image.BgraPixels[2]==255&&image.BgraPixels[3] is >=127 and <=129,"CLIP stored PNG preserves dimensions, BGRA and straight alpha");
            Check(image.OriginalWidth==(path==same?8:1600)&&image.OriginalHeight==(path==same?6:1200),"CLIP original pixels come from root mipmap, not millimeter Canvas fields");
            Check(await decoder.EstimatePixelBytesAsync(path,default)==8*6*4,"CLIP prefetch estimate uses stored display dimensions");
            var thumbnail=await decoder.DecodeAsync(path,default,4);
            Check(thumbnail.Width==4&&thumbnail.Height==3&&thumbnail.OriginalWidth==image.OriginalWidth,"CLIP thumbnail retains original canvas dimensions");
            Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"CLIP read leaves original source bytes unchanged");
        }
        var metadataPath=Path.Combine(output,"profile.png");
        using(var image=new MagickImage(new MagickColor("#808080"),8,6)){image.SetProfile(ColorProfiles.AdobeRGB1998);image.Write(metadataPath);}
        var profiled=await decoder.DecodeAsync(await Fixture("profile",metadataPath,8,6),default);
        Check(profiled.BgraPixels[2] is >=128 and <=131&&profiled.BgraPixels[2]==profiled.BgraPixels[1],"CLIP stored PNG uses embedded ICC conversion");
        var original=File.ReadAllBytes(same);
        await Reject(original[..^1],"INVALID_TRUNCATED","CLIP truncated footer rejected");
        var header=(byte[])original.Clone();header[23]=25;await Reject(header,"未対応","CLIP unknown header rejected");
        var version=(byte[])original.Clone();version[47]=1;await Reject(version,"未対応","CLIP unknown format version rejected");
        var sqlLength=(byte[])original.Clone();BinaryPrimitives.WriteUInt64BigEndian(sqlLength.AsSpan(88),ulong.MaxValue);await Reject(sqlLength,"INVALID_CHUNK_LENGTH","CLIP rejects section overflow before allocation");
        var badSql=(byte[])original.Clone();badSql[96]=0;await Reject(badSql,"INVALID_DATABASE_SIGNATURE","CLIP corrupt SQLite signature rejected");
        var unknown=(byte[])original.Clone();"UNKNOWN!"u8.CopyTo(unknown.AsSpan(80));await Reject(unknown,"未対応","CLIP unknown chunk fails explicitly");
        var footer=(byte[])original.Clone();footer[^16]=0;await Reject(footer,"未対応","CLIP unknown footer rejected");
        var tail=original.Concat(new byte[]{1}).ToArray();await Reject(tail,"INVALID_FILE_LENGTH","CLIP rejects bytes after declared file end");
        var missing=await Fixture("missing",pngPath,8,6,"missing");await Reject(File.ReadAllBytes(missing),"保存済み画像がありません","CLIP missing preview does not recompose layers");
        var multiple=await Fixture("multiple",pngPath,8,6,"multiple");await Reject(File.ReadAllBytes(multiple),"未対応","CLIP multiple canvases rejected explicitly");
        var multipleCanvas=await Fixture("multiple-canvas",pngPath,8,6,"multiple-canvas");await Reject(File.ReadAllBytes(multipleCanvas),"未対応","CLIP multiple Canvas rows rejected before choosing an image");
        var mismatch=await Fixture("mismatch",pngPath,8,6,"mismatch");await Reject(File.ReadAllBytes(mismatch),"INVALID_PREVIEW_DIMENSIONS","CLIP PNG and database dimensions must agree");
        var dimensions=await Fixture("dimensionless",pngPath,8,6,"dimensionless");await Reject(File.ReadAllBytes(dimensions),"原寸情報","CLIP unverified source dimensions fail with reason");
        var queryLimit=await Fixture("query-limit",pngPath,8,6,"query-limit");await Reject(File.ReadAllBytes(queryLimit),"処理上限","CLIP bounds recursive database processing");
        var overPng=File.ReadAllBytes(pngPath);BinaryPrimitives.WriteUInt32BigEndian(overPng.AsSpan(16),8193);BinaryPrimitives.WriteUInt32BigEndian(overPng.AsSpan(20),8192);
        var overPath=Path.Combine(output,"over-limit.png");File.WriteAllBytes(overPath,overPng);
        var overClip=await Fixture("over-limit",overPath,8193,8192);await Reject(File.ReadAllBytes(overClip),"256 MiB","CLIP validates PNG pixel limit before parent pixel allocation");
        var hugeMetadata=new byte[96];original.AsSpan(0,96).CopyTo(hugeMetadata);
        BinaryPrimitives.WriteUInt64BigEndian(hugeMetadata.AsSpan(8),256UL*1024*1024+113);
        BinaryPrimitives.WriteUInt64BigEndian(hugeMetadata.AsSpan(88),256UL*1024*1024+1);
        await Reject(hugeMetadata,"メタデータ","CLIP rejects metadata limit before reading or allocating database");
        using(var canceled=new CancellationTokenSource()) {
            canceled.Cancel();try{await decoder.DecodeAsync(same,canceled.Token);throw new Exception("Cancellation ignored");}
            catch(OperationCanceledException){Check(true,"CLIP pre-cancellation");}
        }
        using(var cancel=new CancellationTokenSource()) {
            using var blocked=new BlockedClipStream(cancel.Token);var task=decoder.DecodeStreamAsync(blocked,cancel.Token,0);
            await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cancel.Cancel();
            try{await task.WaitAsync(TimeSpan.FromSeconds(5));throw new Exception("CLIP cancellation ignored");}
            catch(OperationCanceledException){Check(true,"CLIP cancels active helper and closes its pipes");}
        }
        Check((await decoder.DecodeAsync(same,default)).Width==8,"CLIP decoder remains usable after failure and active cancellation");
        var zipPath=Path.Combine(output,"images.zip");
        using(var zip=new ZipArchive(File.Create(zipPath),ZipArchiveMode.Create)) {
            zip.CreateEntryFromFile(same,"chapter/1.clip");zip.CreateEntryFromFile(smaller,"chapter/2.clip");
        }
        using(var archive=await ArchiveSession.OpenAsync(zipPath,default)) {
            var image=await archive.DecodeAsync(new(zipPath,"chapter/2.clip",false),default);
            Check(image.Width==8&&image.OriginalWidth==1600,"CLIP archive stream uses stored preview extractor without temporary input files");
        }
        var cache=new DecodedImageCache(decoder,Path.Combine(output,"cache"));cache.Configure(new(0,1,0));await cache.GetAsync(smaller,default);
        var restart=new DecodedImageCache(decoder,Path.Combine(output,"cache"));restart.Configure(new(0,1,0));
        var cached=await restart.GetAsync(smaller,default);
        Check(restart.DiskHits==1&&cached.Width==8&&cached.OriginalWidth==1600,"CLIP disk cache preserves preview and source dimensions");
        Check(MagickImageDecoder.Supports("日本語.CLIP"),"CLIP supported case insensitively");
        using(var input=new ShortReadStream(original)) {
            Check((await decoder.DecodeStreamAsync(input,default,0)).Width==8&&input.CanRead,"CLIP accepts nonseekable short reads and leaves caller stream open");
        }
        // Include a skipped external block, as real files contain many opaque layer/vector chunks.
        var withExternal=new byte[original.Length+75];original.AsSpan(0,80).CopyTo(withExternal);
        "CHNKExta"u8.CopyTo(withExternal.AsSpan(80));BinaryPrimitives.WriteUInt64BigEndian(withExternal.AsSpan(88),59);
        BinaryPrimitives.WriteUInt64BigEndian(withExternal.AsSpan(96),40);BinaryPrimitives.WriteUInt64BigEndian(withExternal.AsSpan(144),3);
        original.AsSpan(80).CopyTo(withExternal.AsSpan(155));BinaryPrimitives.WriteUInt64BigEndian(withExternal.AsSpan(8),(ulong)withExternal.Length);
        BinaryPrimitives.WriteUInt64BigEndian(withExternal.AsSpan(48),155);
        using(var input=new MemoryStream(withExternal))Check((await decoder.DecodeStreamAsync(input,default,0)).Width==8,"CLIP skips external pixels without layer decoding");
        var display=Path.Combine(output,"display.png");using(var image=new MagickImage(MagickColors.MediumSeaGreen,640,480))image.Write(display);
        await Fixture("display",display,1600,1200);
        var navigation=Path.Combine(output,"navigation");Directory.CreateDirectory(navigation);
        File.Copy(overClip,Path.Combine(navigation,"1-limit.clip"),true);File.Copy(same,Path.Combine(navigation,"2-valid.clip"),true);
        return checks;
    }
    private sealed class ShortReadStream(byte[] bytes):Stream {
        private int offset;
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int start,int count)=>Read(buffer.AsSpan(start,count));
        public override int Read(Span<byte> buffer){var count=Math.Min(3,Math.Min(buffer.Length,bytes.Length-offset));bytes.AsSpan(offset,count).CopyTo(buffer);offset+=count;return count;}
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int start,int count)=>throw new NotSupportedException();
    }
    private sealed class BlockedClipStream(CancellationToken token):Stream {
        private bool prefix;
        internal TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int start,int count){if(!prefix){"CSFC"u8.CopyTo(buffer.AsSpan(start));prefix=true;return 4;}Entered.TrySetResult();token.WaitHandle.WaitOne();token.ThrowIfCancellationRequested();return 0;}
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int start,int count)=>throw new NotSupportedException();
    }
}
