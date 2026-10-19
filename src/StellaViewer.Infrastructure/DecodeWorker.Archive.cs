using System.Text;
using StellaViewer.Core;

namespace StellaViewer.Infrastructure;

public static partial class DecodeWorker
{
    private const int TextMessageLimit = 32 * 1024 * 1024;
    private static ArchiveSession? workerArchive;
    private static string? workerArchivePath, workerFormatPath;
    private static long workerArchiveLength;
    private static DateTime workerArchiveModified;

    internal static async Task<ArchiveItem[]> ArchiveCatalogAsync(string physical, string format, CancellationToken token)
        => (await SendAsync(new("archive-catalog", physical, FormatPath: format), token).ConfigureAwait(false)).Catalog!;
    internal static async Task<DecodedImage> ArchiveImageAsync(string physical, string format, string member, uint thumbnail, CancellationToken token)
        => (await SendAsync(new("archive-image", physical, thumbnail, Member: member, FormatPath: format), token).ConfigureAwait(false)).Images.Single();
    internal static async Task<long> ArchiveProbeAsync(string physical, string format, string member, CancellationToken token)
        => (await SendAsync(new("archive-probe", physical, Member: member, FormatPath: format), token).ConfigureAwait(false)).Reply.Number;
    internal static async Task<bool> ArchiveAnimationHeaderAsync(string physical, string format, string member, CancellationToken token)
        => (await SendAsync(new("archive-animation-header", physical, Member: member, FormatPath: format), token).ConfigureAwait(false)).Reply.Number != 0;
    internal static async Task<PngTextResult> ArchiveTextAsync(string physical, string format, string member, CancellationToken token)
        => (await SendAsync(new("archive-text", physical, Member: member, FormatPath: format), token).ConfigureAwait(false)).Text!;
    internal static async Task ArchiveCopyAsync(string physical, string format, string member, Stream destination, long limit, CancellationToken token)
        => _ = await SendAsync(new("archive-copy", physical, Member: member, FormatPath: format, ByteLimit: limit), token, destination: destination).ConfigureAwait(false);

    private static async Task<Result?> ReceiveArchiveAsync(Request request, Reply reply, Stream stream, Stream? destination, CancellationToken token)
    {
        if(request.Operation=="archive-catalog") {
            if(reply.Number<0 || reply.Number>ArchiveReadLimits.MaximumItems)throw new InvalidDataException("書庫の一覧件数が不正です。");
            var entries=new ArchiveItem[(int)reply.Number];long characters=0;
            var paths=new HashSet<string>(StringComparer.Ordinal);
            for(var i=0;i<entries.Length;i++) {
                var item=await ReadJsonAsync<ArchiveItem>(stream,token).ConfigureAwait(false);
                if(item.Path is null || item.Name is null)throw new InvalidDataException("書庫の一覧項目が不正です。");
                ArchiveReadLimits.CheckPath(item.Path);
                if(item.Path.Length==0 || ArchiveLocation.NormalizeEntry(item.Path)!=item.Path || item.Name!=item.Path.Split('/')[^1]
                    || item.Length<0 || item.Index< -1 || item.Index>=ArchiveReadLimits.MaximumItems || item.Index== -1 && !item.IsDirectory
                    || !paths.Add(item.Path) || (characters+=item.Path.Length)>ArchiveReadLimits.MaximumCatalogCharacters)
                    throw new InvalidDataException("書庫の一覧内容が不正です。");
                entries[i]=item;
            }
            var directories=entries.Where(e=>e.IsDirectory).Select(e=>e.Path).ToHashSet(StringComparer.Ordinal);
            foreach(var entry in entries) {
                var parent=ArchiveLocation.ParentEntry(entry.Path);
                if(parent.Length>0 && !directories.Contains(parent))throw new InvalidDataException("書庫の親フォルダが不正です。");
            }
            return new(reply,[],entries);
        }
        if(request.Operation=="archive-text") {
            var text=await ReadJsonAsync<PngTextResult>(stream,token,TextMessageLimit).ConfigureAwait(false);
            if(text.Entries is null || text.Entries.Count>PngTextReader.MaximumEntries || text.Notice?.Length>65536)
                throw new InvalidDataException("書庫のテキスト情報が不正です。");
            long bytes=0;
            foreach(var entry in text.Entries) {
                if(entry is null || entry.Key is null || entry.Key.Length>79 || entry.Text is null
                    || (bytes+=Encoding.UTF8.GetByteCount(entry.Text))>PngTextReader.MaximumTextBytes)
                    throw new InvalidDataException("書庫のテキスト情報が上限を超えています。");
            }
            return new(reply,[],Text:text);
        }
        if(request.Operation=="archive-copy") {
            if(destination is null || reply.Number<0 || reply.Number>Math.Min(request.ByteLimit,ArchiveTemporaryStore.DefaultLimit))
                throw new InvalidDataException("書庫内項目の展開サイズが不正です。");
            var buffer=new byte[65536];long copied=0;
            while(true) {
                var count=await ReadIntAsync(stream,token).ConfigureAwait(false);
                if(count==0)break;
                if(count== -1) {
                    var failure=await ReadJsonAsync<Reply>(stream,token).ConfigureAwait(false);
                    throw new IOException(failure.Error??"書庫内項目を展開できませんでした。");
                }
                if(count<0 || count>buffer.Length || count>reply.Number-copied)
                    throw new InvalidDataException("書庫内項目の展開データが不正です。");
                await stream.ReadExactlyAsync(buffer.AsMemory(0,count),token).ConfigureAwait(false);
                await destination.WriteAsync(buffer.AsMemory(0,count),token).ConfigureAwait(false);copied+=count;
            }
            if(copied!=reply.Number)throw new InvalidDataException("書庫内項目の展開サイズが一致しません。");
            return new(reply,[]);
        }
        return null;
    }

    private static Reply ArchiveFailure(Exception error)
    {
        var message=error.Message.Length>4096?error.Message[..4096]:error.Message;
        return new(error is NotSupportedException?1:error is InvalidDataException?2:3,message);
    }
    private static async Task<ArchiveSession> WorkerArchiveAsync(Request request)
    {
        var path=System.IO.Path.GetFullPath(request.Path!);
        var info=new FileInfo(path);var length=info.Length;var modified=info.LastWriteTimeUtc;
        if(workerArchive is not null && workerArchivePath==path && workerFormatPath==request.FormatPath
            && workerArchiveLength==length && workerArchiveModified==modified) {
            workerArchive.ValidateUnchanged();return workerArchive;
        }
        workerArchive?.Dispose();workerArchive=null;
        workerArchive=await ArchiveSession.OpenWorkerAsync(path,request.FormatPath!,default).ConfigureAwait(false);
        workerArchivePath=path;workerFormatPath=request.FormatPath;workerArchiveLength=length;workerArchiveModified=modified;
        return workerArchive;
    }
    private static async Task ServeArchiveAsync(Request request,Stream output)
    {
        var started=false;
        try {
            var archive=await WorkerArchiveAsync(request).ConfigureAwait(false);
            var location=new ArchiveLocation(archive.SourcePath,request.Member??"",false);
            if(request.Operation is "archive-image" or "archive-probe" && archive.FindFile(location.EntryPath)?.Length>InputLimit)
                throw new NotSupportedException("書庫内画像の読み込み容量が上限512 MiBを超えました。");
            if(request.Operation=="archive-catalog") {
                var entries=archive.WorkerCatalog();
                await WriteJsonAsync(output,new Reply(Number:entries.Length),default).ConfigureAwait(false);started=true;
                foreach(var entry in entries)await WriteJsonAsync(output,entry,default).ConfigureAwait(false);
            }
            else if(request.Operation=="archive-image") {
                var image=await archive.DecodeAsync(location,default,request.Thumbnail,true).ConfigureAwait(false);
                await WriteJsonAsync(output,new Reply(Frames:[Describe(image)]),default).ConfigureAwait(false);started=true;
                await output.WriteAsync(image.BgraPixels).ConfigureAwait(false);
            }
            else if(request.Operation=="archive-probe") {
                var size=await archive.EstimatePixelBytesAsync(location,default,true).ConfigureAwait(false);
                await WriteJsonAsync(output,new Reply(Number:size),default).ConfigureAwait(false);
            }
            else if(request.Operation=="archive-animation-header") {
                var animated=await archive.HasPngAnimationAsync(location,default,true).ConfigureAwait(false);
                await WriteJsonAsync(output,new Reply(Number:animated?1:0),default).ConfigureAwait(false);
            }
            else if(request.Operation=="archive-text") {
                var text=await archive.ReadPngMetadataAsync(location,default,true).ConfigureAwait(false);
                await WriteJsonAsync(output,new Reply(),default).ConfigureAwait(false);started=true;
                await WriteJsonAsync(output,text,default,TextMessageLimit).ConfigureAwait(false);
            }
            else if(request.Operation=="archive-copy") {
                var item=archive.FindFile(location.EntryPath)??throw new FileNotFoundException("書庫内の項目が見つかりません。");
                var limit=Math.Min(request.ByteLimit,ArchiveTemporaryStore.DefaultLimit);
                if(limit<0 || item.Length>limit)throw new IOException("一時展開の容量上限を超えました。");
                await WriteJsonAsync(output,new Reply(Number:item.Length),default).ConfigureAwait(false);started=true;
                try {
                    using var sink=new ChunkWriter(output);
                    await archive.CopyEntryAsync(location,sink,limit,default,true).ConfigureAwait(false);
                    await WriteIntAsync(output,0,default).ConfigureAwait(false);
                }
                catch(Exception error) {
                    await WriteIntAsync(output,-1,default).ConfigureAwait(false);
                    await WriteJsonAsync(output,ArchiveFailure(error),default).ConfigureAwait(false);
                }
            }
            else throw new InvalidDataException("書庫の読み込み要求が不正です。");
            await output.FlushAsync().ConfigureAwait(false);
        }
        catch(Exception error) {
            if(started)throw; // Never send another header into a partially written payload.
            await WriteJsonAsync(output,ArchiveFailure(error),default).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }
    }
    private sealed class ChunkWriter(Stream output):Stream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default) {
            while(!buffer.IsEmpty) {
                var count=Math.Min(buffer.Length,65536);
                await WriteIntAsync(output,count,token).ConfigureAwait(false);
                await output.WriteAsync(buffer[..count],token).ConfigureAwait(false);buffer=buffer[count..];
            }
        }
        public override void Write(byte[] buffer,int offset,int count)=>WriteAsync(buffer.AsMemory(offset,count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead=>false;
        public override bool CanSeek=>false;
        public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();
        public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush()=>output.Flush();
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
    }
}
