using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using StellaViewer.Infrastructure;

internal static class ArchiveIsolationChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"archive-isolation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var checks=0;
        void Check(bool valid,string label){if(!valid)throw new Exception(label);checks++;Console.WriteLine("PASS: "+label);}
        var png=await File.ReadAllBytesAsync(Path.Combine(output,"sample.png"));
        var bulk=new byte[4*1024*1024];new Random(91).NextBytes(bulk);
        var textPng=TextPng(png);
        var zipPath=Path.Combine(root,"日本語 source.zip");
        using(var zip=new ZipArchive(File.Create(zipPath),ZipArchiveMode.Create)) {
            foreach(var (name,bytes) in new[]{("画像/page.png",png),("bulk.bin",bulk),("metadata.png",textPng),("bad.png","<svg/>"u8.ToArray())}) {
                using var target=zip.CreateEntry(name,CompressionLevel.Optimal).Open();target.Write(bytes);
            }
        }
        var sourceHash=SHA256.HashData(await File.ReadAllBytesAsync(zipPath));
        var localRejected=false;
        try{using var ignored=await ArchiveSession.OpenWorkerAsync(zipPath,zipPath,default);}catch(InvalidOperationException){localRejected=true;}
        Check(localRejected,"Archive parser cannot be invoked in viewer process");
        using var archive=await ArchiveSession.OpenAsync(zipPath,default);
        var pid=DecodeWorker.WorkerProcessId;
        Check(pid.HasValue&&pid!=Environment.ProcessId&&archive.ContainsFolder("画像"),"Catalog is parsed in bounded worker and synthetic folders cross IPC");
        var page=new ArchiveLocation(zipPath,"画像/page.png",false);
        var original=await new MagickImageDecoder().DecodeAsync(Path.Combine(output,"sample.png"),default);
        var decoded=await archive.DecodeAsync(page,default);
        Check(decoded.BgraPixels.SequenceEqual(original.BgraPixels),"Worker archive pixels match original file");
        Check((await archive.DecodeAsync(page,default,4)).Width==4,"Worker archive thumbnails retain size limits");
        Check(await archive.EstimatePixelBytesAsync(page,default,false)==8*6*4,"Archive pixel probe runs in worker");
        Check(!await archive.HasPngAnimationAsync(page,default),"Archive animation header detection runs in worker");
        var metadata=await archive.ReadPngMetadataAsync(new(zipPath,"metadata.png",false),default);
        var addedText=metadata.Entries.Where(e=>e.Key is "key0" or "key1" or "key2").ToArray();
        Check(addedText.Length==3&&addedText.Sum(e=>e.Text.Length)==3*800000,$"Archive PNG text larger than 1 MiB crosses bounded metadata protocol: {addedText.Length} fixture chunks; notice={metadata.Notice}");
        using(var copied=new MemoryStream()) {
            await archive.CopyEntryAsync(new(zipPath,"bulk.bin",false),copied,bulk.Length,default);
            Check(copied.ToArray().SequenceEqual(bulk),"Archive extraction streams across chunks without changing bytes");
        }
        Check(DecodeWorker.WorkerProcessId==pid,"Archive operations reuse worker and catalog");
        var unsupported=false;
        try{await archive.DecodeAsync(new(zipPath,"bad.png",false),default);}catch(NotSupportedException){unsupported=true;}
        Check(unsupported&&DecodeWorker.WorkerProcessId==pid,"Rejected archive thumbnail retains healthy worker catalog");
        Check((await archive.DecodeAsync(page,default)).Width==8,"Valid image remains available after rejected archive thumbnail");
        using(var copied=new MemoryStream()) {
            var rejected=false;try{await archive.CopyEntryAsync(new(zipPath,"bulk.bin",false),copied,bulk.Length-1,default);}catch(IOException){rejected=true;}
            Check(rejected&&copied.Length==0,"Caller extraction quota rejects oversized entry before writing");
        }
        using(var canceled=new CancellationTokenSource())using(var sink=new PausedWriter()) {
            var copy=archive.CopyEntryAsync(new(zipPath,"bulk.bin",false),sink,bulk.Length,canceled.Token);
            await sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));canceled.Cancel();
            var stopped=false;try{await copy;}catch(OperationCanceledException){stopped=true;}
            Check(stopped&&DecodeWorker.WorkerProcessId is null,"Canceled archive extraction kills worker and releases caller");
        }
        Check((await archive.DecodeAsync(page,default)).Width==8,"Existing archive view restarts worker after canceled extraction");
        var previousTimeout=DecodeWorker.RequestTimeout;
        try {
            DecodeWorker.RequestTimeout=TimeSpan.FromMilliseconds(350);
            using var sink=new PausedWriter();var timer=Stopwatch.StartNew();
            var stopped=false;try{await archive.CopyEntryAsync(new(zipPath,"bulk.bin",false),sink,bulk.Length,default);}catch(IOException){stopped=true;}
            Check(stopped&&timer.Elapsed<TimeSpan.FromSeconds(5)&&DecodeWorker.WorkerProcessId is null,"Archive timeout forcibly terminates parser/extractor process");
        }
        finally{DecodeWorker.RequestTimeout=previousTimeout;}
        Check((await archive.DecodeAsync(page,default)).Width==8,"Archive image can be opened after extraction timeout");
        using(var sink=new PausedWriter()) {
            var copy=archive.CopyEntryAsync(new(zipPath,"bulk.bin",false),sink,bulk.Length,default);
            await sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var worker=Process.GetProcessById(DecodeWorker.WorkerProcessId!.Value);worker.Kill(entireProcessTree:true);worker.WaitForExit(2000);sink.Release.TrySetResult();
            var failed=false;try{await copy;}catch(IOException){failed=true;}
            Check(failed&&DecodeWorker.WorkerProcessId is null,"Truncated worker output is an extraction failure");
        }
        Check((await archive.DecodeAsync(page,default)).Width==8,"Archive view survives worker crash");
        await using(var store=new ArchiveTemporaryStore(Path.Combine(root,"temporary"),8*1024*1024)) {
            var failed=false;
            try {
                using var lease=await store.CreateAsync(async (target,limit,token)=>{
                    using var faulty=new FailingWriter(target);await archive.CopyEntryAsync(new(zipPath,"bulk.bin",false),faulty,limit,token);
                },default);
            }catch(IOException){failed=true;}
            Check(failed&&store.UsedBytes==0&&!Directory.EnumerateFiles(store.DirectoryPath,"*.pdf").Any(),"Failed remote extraction releases and removes partial temporary file");
        }
        var largeCatalog=Path.Combine(root,"large-catalog.zip");
        using(var zip=new ZipArchive(File.Create(largeCatalog),ZipArchiveMode.Create))for(var i=0;i<7000;i++)zip.CreateEntry($"folder/image-{i:D5}.png");
        using(var catalog=await ArchiveSession.OpenAsync(largeCatalog,default))
            Check(catalog.Images("folder").Length==7000,"Large catalogs stream item-by-item beyond ordinary metadata packet limit");
        var finalHash=SHA256.HashData(await File.ReadAllBytesAsync(zipPath));
        Check(sourceHash.SequenceEqual(finalHash),"Archive isolation never modifies original source");
        return checks;
    }
    private static byte[] TextPng(byte[] original)
    {
        using var output=new MemoryStream();output.Write(original.AsSpan(0,original.Length-12));
        for(var i=0;i<3;i++) {
            var type="tEXt"u8.ToArray();var data=Encoding.Latin1.GetBytes($"key{i}\0"+new string('x',800000));
            var header=new byte[4];BinaryPrimitives.WriteInt32BigEndian(header,data.Length);output.Write(header);output.Write(type);output.Write(data);
            uint crc=0xffffffff;foreach(var b in type.Concat(data)){crc^=b;for(var bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}
            BinaryPrimitives.WriteUInt32BigEndian(header,~crc);output.Write(header);
        }
        output.Write(original.AsSpan(original.Length-12));return output.ToArray();
    }
    private sealed class PausedWriter:Stream
    {
        internal readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes,CancellationToken token=default){Started.TrySetResult();await Release.Task.WaitAsync(token);}
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){}public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
    }
    private sealed class FailingWriter(Stream target):Stream
    {
        private bool written;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes,CancellationToken token=default) {
            if(written)throw new IOException("Synthetic destination failure");written=true;await target.WriteAsync(bytes,token);
        }
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){}public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
    }
}
