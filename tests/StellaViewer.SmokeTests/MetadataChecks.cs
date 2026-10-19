using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;
using StellaViewer.Infrastructure;
internal static class MetadataChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"metadata");Directory.CreateDirectory(root);var count=0;
        void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
        byte[] png;using(var image=new MagickImage(MagickColors.CornflowerBlue,240,160)){image.Strip();image.SetArtifact("png:exclude-chunks","date,time");png=image.ToByteArray(MagickFormat.Png);}
        var parameters="blue sky,\nJapanese landscape\nNegative prompt: low quality, blurry\nSteps: 24, Sampler: DPM++ 2M, CFG scale: 7, Seed: 18446744073709551615, Model: landscape, Hashes: {\"model\": \"abc,def\"}";
        byte[] Make(params byte[][] chunks)=>[..png[..^12],..chunks.SelectMany(chunk=>chunk),..png[^12..]];
        PngTextResult Read(byte[] data){using var stream=new MemoryStream(data);return PngTextReader.Read(stream);}
        string Field(AiMetadataReport report,string name)=>report.Fields.Single(field=>field.Name==name).Value;
        Check(Read(png).Entries.Count==0&&Read("not PNG"u8.ToArray()).Entries.Count==0,"Ordinary PNG and non-PNG have no AI metadata");
        var plain=Make(Text("parameters",parameters));var basic=AiMetadata.Parse(Read(plain));
        Check(Field(basic,"プロンプト")=="blue sky,\nJapanese landscape"&&Field(basic,"ネガティブプロンプト")=="low quality, blurry","AUTOMATIC1111 multiline positive and negative prompts are separated");
        Check(Field(basic,"Steps")=="24"&&Field(basic,"Sampler")=="DPM++ 2M"&&Field(basic,"CFG")=="7"&&Field(basic,"Seed")=="18446744073709551615"&&Field(basic,"モデル")=="landscape","Generation parameters preserve exact seed and model text");
        Check(Field(basic,"Hashes").Contains("abc,def"),"Quoted JSON commas do not split generation parameters");
        Check(Read(Make(Text("parameters",parameters,true))).Entries.Single().Text==parameters,"Compressed zTXt metadata is decoded");
        const string japanese="青い空、山\nNegative prompt: ぼけ\nSteps: 20, Sampler: Euler, CFG scale: 6, Seed: 42";
        var international=Make(International("parameters",japanese,false));
        Check(Read(international).Entries.Single().Text==japanese&&Read(Make(International("parameters",japanese,true))).Entries.Single().Text==japanese,"Plain and compressed iTXt preserve Unicode prompts");
        Check(Read(Make(Text("parameters","first"),Text("parameters","second"))).Entries.Count==2,"Duplicate original text chunks remain available without overwriting");
        var bad=Text("parameters",parameters);bad[^1]^=1;var invalid=Read(Make(bad));Check(invalid.Entries.Count==0&&invalid.Notice!.Contains("CRC"),"Corrupt text CRC is skipped and reported");
        Check(Read(Make(Chunk("zTXt",[..Encoding.Latin1.GetBytes("parameters"),0,0,1,2,3]))).Notice is not null,"Broken compressed metadata is isolated from image display");
        Check(Read(Make(Chunk("tEXt",[0,1,2]))).Notice is not null,"Invalid PNG text keyword is reported");
        Check(Read(plain[..^15]).Notice is not null,"Truncated PNG metadata produces a notice");
        Check(Read(Make(Text("oversize",new string('x',PngTextReader.MaximumChunkBytes)))).Entries.Count==0,"Oversize individual text chunk is skipped");
        var bomb=Read(Make(Text("bomb",new string('x',PngTextReader.MaximumTextBytes+1),true)));Check(bomb.Entries.Count==0&&bomb.Notice is not null,"Compressed text expansion is bounded");
        var many=Read(Make(Enumerable.Range(0,65).Select(i=>Text("key"+i,"value")).ToArray()));Check(many.Entries.Count==64&&many.Notice is not null,"Text item count is bounded with notice");
        var graph="""{"1":{"class_type":"CheckpointLoaderSimple","inputs":{"ckpt_name":"model.safetensors"}},"2":{"class_type":"CLIPTextEncode","inputs":{"text":"positive text"}},"3":{"class_type":"CLIPTextEncode","inputs":{"text":"negative text"}},"4":{"class_type":"KSampler","inputs":{"model":["1",0],"positive":["2",0],"negative":["3",0],"steps":30,"cfg":7.5,"seed":987654321,"sampler_name":"euler"}}}""";
        var comfy=AiMetadata.Parse(Read(Make(International("prompt",graph,false))));
        Check(Field(comfy,"ノード 4 / プロンプト")=="positive text"&&Field(comfy,"ノード 4 / ネガティブプロンプト")=="negative text"&&Field(comfy,"ノード 4 / モデル")=="model.safetensors","ComfyUI extracts standard sampler connections without mixing positive and negative nodes");
        Check(Field(comfy,"ノード 4 / Steps")=="30"&&Field(comfy,"ノード 4 / CFG")=="7.5"&&Field(comfy,"ノード 4 / Seed")=="987654321","ComfyUI sampler fields are read as exact scalar values");
        var custom=AiMetadata.Parse(Read(Make(Text("prompt",graph.Replace("CLIPTextEncode","UnknownCustomNode")))));Check(!custom.Fields.Any(field=>field.Name.EndsWith("プロンプト"))&&custom.DisplayText().Contains("UnknownCustomNode"),"Unknown ComfyUI nodes are shown raw instead of guessing their prompts");
        var cycle="""{"1":{"class_type":"LoraLoader","inputs":{"model":["1",0]}},"4":{"class_type":"KSampler","inputs":{"model":["1",0]}}}""";
        Check(!AiMetadata.Parse(Read(Make(Text("prompt",cycle)))).Fields.Any(field=>field.Name.EndsWith("モデル")),"Cyclic ComfyUI connections terminate without inventing model information");
        var novel=AiMetadata.Parse(Read(Make(Text("Software","NovelAI"),Text("Source","nai-model"),International("Description","花の絵",false),Text("Comment","""{"uc":"bad","steps":28,"scale":5,"seed":123,"sampler":"k_euler"}"""))));
        Check(Field(novel,"プロンプト")=="花の絵"&&Field(novel,"ネガティブプロンプト")=="bad"&&Field(novel,"モデル／Source")=="nai-model"&&Field(novel,"Steps")=="28","NovelAI text and Comment JSON are interpreted");
        var malformed=AiMetadata.Parse(Read(Make(Text("prompt","{broken JSON"))));Check(malformed.Fields.Count==0&&malformed.DisplayText().Contains("{broken JSON"),"Malformed tool JSON remains visible as raw text");
        Check(AiMetadata.Parse(Read(Make(Text("Software","ordinary editor")))).DisplayText().Contains("メタデータなし"),"Non-AI text is not classified as AI generation information");
        Check(new AiMetadataReport([],new([new("raw",new string('x',100000))])).DisplayText().Length<65700,"Sidebar text rendering is bounded and truncation is explicit");
        using(var stream=new NonSeek(new MemoryStream(international),3))Check(PngTextReader.Read(stream).Entries.Single().Text==japanese,"Non-seekable short reads find text after image data");
        var header=new byte[8];BinaryPrimitives.WriteUInt32BigEndian(header,(uint)PngTextReader.MaximumStreamScanBytes);"IDAT"u8.CopyTo(header.AsSpan(4));
        using(var stream=new NonSeek(new MemoryStream([..png[..8],..header]),3))Check(PngTextReader.Read(stream).Notice!.Contains("64 MiB"),"Non-seekable scan budget rejects huge image chunk before reading its payload");
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();try{using var stream=new MemoryStream(plain);PngTextReader.Read(stream,canceled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Check(true,"Metadata reading honors cancellation");}}
        var path=Path.Combine(root,"parameters.png");File.WriteAllBytes(path,international);var hash=SHA256.HashData(international);var fileResult=await PngTextReader.ReadFileAsync(path);
        Check(fileResult.Entries.Count==1&&SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash),"Metadata inspection leaves original file bytes unchanged");
        try{await PngTextReader.ReadFileAsync(path,expectedLength:1);throw new Exception("Changed source accepted");}catch(IOException){Check(true,"Changed source is not presented as metadata for the displayed old image");}
        try{await PngTextReader.ReadFileAsync(@"\\stella-metadata.invalid\share\image.png");throw new Exception("Network accepted");}catch(NotSupportedException){Check(true,"Disabled network metadata is rejected before opening source");}
        File.WriteAllBytes(Path.Combine(root,"plain.png"),png);File.WriteAllBytes(Path.Combine(root,"comfy.png"),Make(International("prompt",graph,false)));File.WriteAllBytes(Path.Combine(root,"novel.png"),Make(Text("Software","NovelAI"),Text("Comment","""{"prompt":"flower","uc":"bad","seed":123}""")));
        var book=Path.Combine(root,"metadata.zip");using(var outputFile=File.Create(book))using(var zip=new ZipArchive(outputFile,ZipArchiveMode.Create))using(var entry=zip.CreateEntry("folder/parameters.png").Open())entry.Write(international);
        using(var archive=await ArchiveSession.OpenAsync(book,default)){var result=await archive.ReadPngMetadataAsync(new(book,"folder/parameters.png",false),default);Check(result.Entries.Single().Text==japanese,"Archive metadata is read directly from entry stream");}
        var nested=Path.Combine(root,"nested.zip");using(var outputFile=File.Create(nested))using(var zip=new ZipArchive(outputFile,ZipArchiveMode.Create))using(var entry=zip.CreateEntry("inner.zip").Open())entry.Write(File.ReadAllBytes(book));
        return count;
    }
    private static byte[] Text(string key,string value,bool compressed=false)
    {
        var bytes=Encoding.Latin1.GetBytes(value);if(!compressed)return Chunk("tEXt",[..Encoding.Latin1.GetBytes(key),0,..bytes]);
        using var output=new MemoryStream();using(var zip=new ZLibStream(output,CompressionLevel.SmallestSize,true))zip.Write(bytes);return Chunk("zTXt",[..Encoding.Latin1.GetBytes(key),0,0,..output.ToArray()]);
    }
    private static byte[] International(string key,string value,bool compressed){var bytes=Encoding.UTF8.GetBytes(value);if(compressed){using var output=new MemoryStream();using(var zip=new ZLibStream(output,CompressionLevel.SmallestSize,true))zip.Write(bytes);bytes=output.ToArray();}return Chunk("iTXt",[..Encoding.Latin1.GetBytes(key),0,(byte)(compressed?1:0),0,0,0,..bytes]);}
    private static byte[] Chunk(string type,byte[] data){var chunk=new byte[data.Length+12];BinaryPrimitives.WriteUInt32BigEndian(chunk,(uint)data.Length);Encoding.ASCII.GetBytes(type).CopyTo(chunk,4);data.CopyTo(chunk,8);uint crc=uint.MaxValue;foreach(var b in chunk.AsSpan(4,data.Length+4)){crc^=b;for(var i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length-4),~crc);return chunk;}
    private sealed class NonSeek(Stream input,int maximum):Stream
    {
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>input.Read(buffer,offset,Math.Min(maximum,count));public override int Read(Span<byte> buffer)=>input.Read(buffer[..Math.Min(buffer.Length,maximum)]);public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();protected override void Dispose(bool disposing){if(disposing)input.Dispose();base.Dispose(disposing);}
    }
}
