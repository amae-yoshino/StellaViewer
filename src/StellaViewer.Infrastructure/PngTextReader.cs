using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace StellaViewer.Infrastructure;

public sealed record PngTextEntry(string Key,string Text);
public sealed record PngTextResult(IReadOnlyList<PngTextEntry> Entries,string? Notice=null);

// Metadata only: image chunks are skipped without decoding pixels or allocating their length.
public static class PngTextReader
{
    public const int MaximumChunkBytes=1024*1024,MaximumTextBytes=4*1024*1024,MaximumEntries=64;
    public const long MaximumStreamScanBytes=64L*1024*1024;
    private static readonly UTF8Encoding Utf8=new(false,true);
    private static readonly uint[] CrcTable=MakeCrcTable();
    public static Task<PngTextResult> ReadFileAsync(string path,bool allowNetwork=false,CancellationToken token=default,long? expectedLength=null,DateTime? expectedModified=null)=>Task.Run(()=>{
        NetworkAccess.Validate(path,allowNetwork);
        var info=new FileInfo(path);var length=info.Length;var modified=info.LastWriteTimeUtc;
        if(expectedLength is long expected&&expected!=length||expectedModified is DateTime date&&date!=default&&date.ToUniversalTime()!=modified)throw new IOException("元ファイルが変更されています。再読み込みしてから情報を確認してください。");
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        var result=Read(input,token);info.Refresh();if(!info.Exists||info.Length!=length||info.LastWriteTimeUtc!=modified)throw new IOException("読み取り中に元ファイルが変更されました。再読み込みしてください。");return result;
    },token);
    public static PngTextResult Read(Stream input,CancellationToken token=default)
    {
        var entries=new List<PngTextEntry>();var warnings=new HashSet<string>();long scanned=0;var totalText=0;
        void ReadExactly(Span<byte> buffer){token.ThrowIfCancellationRequested();input.ReadExactly(buffer);scanned+=buffer.Length;}
        void Skip(long length){
            token.ThrowIfCancellationRequested();
            if(input.CanSeek){if(length>input.Length-input.Position)throw new EndOfStreamException();input.Seek(length,SeekOrigin.Current);return;}
            if(length>MaximumStreamScanBytes-scanned)throw new ScanLimitException();
            Span<byte> buffer=stackalloc byte[8192];while(length>0){var count=(int)Math.Min(length,buffer.Length);ReadExactly(buffer[..count]);length-=count;}
        }
        try{
            Span<byte> signature=stackalloc byte[8];var count=input.ReadAtLeast(signature,8,false);scanned+=count;
            if(count!=8||!signature.SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return new(entries);
            var header=new byte[8];var crcBytes=new byte[4];
            for(var chunks=0;chunks<100000;chunks++){
                token.ThrowIfCancellationRequested();if(!input.CanSeek&&scanned>MaximumStreamScanBytes-12)throw new ScanLimitException();
                ReadExactly(header);var length=BinaryPrimitives.ReadUInt32BigEndian(header);if(length>int.MaxValue)throw new InvalidDataException("PNGのチャンク長が不正です。");
                if(!input.CanSeek&&(long)length+4>MaximumStreamScanBytes-scanned)throw new ScanLimitException();
                var type=Encoding.ASCII.GetString(header,4,4);
                if(type=="IEND"){if(length!=0)throw new InvalidDataException("IENDが不正です。");ReadExactly(crcBytes);return new(entries,Join());}
                if(type is not("tEXt" or "zTXt" or "iTXt")){Skip((long)length+4);continue;}
                if(length>MaximumChunkBytes||entries.Count>=MaximumEntries){Skip((long)length+4);warnings.Add("大きすぎる、または件数上限を超えるテキスト項目を省略しました。");continue;}
                var data=new byte[(int)length];ReadExactly(data);ReadExactly(crcBytes);
                if(Crc(header.AsSpan(4,4),data)!=BinaryPrimitives.ReadUInt32BigEndian(crcBytes)){warnings.Add("CRCが一致しないテキスト項目を省略しました。");continue;}
                try{
                    var entry=Decode(type,data,MaximumTextBytes-totalText,token);
                    var textBytes=Encoding.UTF8.GetByteCount(entry.Text);
                    if(textBytes>MaximumTextBytes-totalText)throw new InvalidDataException("テキスト情報の上限4 MiBを超えています。");totalText+=textBytes;
                    entries.Add(entry);
                }catch(Exception error)when(error is InvalidDataException or DecoderFallbackException or ArgumentException){warnings.Add("読み取れないテキスト項目を省略しました："+error.Message);}
            }
            warnings.Add("PNGのチャンク数が探索上限を超えています。");
        }
        catch(ScanLimitException){warnings.Add("書庫内PNGの情報探索が64 MiBの上限に達しました。");}
        catch(Exception error)when(error is EndOfStreamException or InvalidDataException){warnings.Add("PNGの情報が途中で切れているか、不正です。");}
        return new(entries,Join());
        string? Join()=>warnings.Count==0?null:string.Join(Environment.NewLine,warnings);
    }
    private static PngTextEntry Decode(string type,byte[] data,int remaining,CancellationToken token)
    {
        var separator=Array.IndexOf(data,(byte)0);if(separator is <1 or >79)throw new InvalidDataException("キーワードが不正です。");
        var key=Encoding.Latin1.GetString(data,0,separator);var offset=separator+1;var compressed=false;var utf8=false;
        if(type=="zTXt"){
            if(offset>=data.Length||data[offset++]!=0)throw new InvalidDataException("未対応の圧縮方式です。");compressed=true;
        }else if(type=="iTXt"){
            if(data.Length-offset<2||data[offset]>1||data[offset+1]!=0)throw new InvalidDataException("iTXtの圧縮情報が不正です。");
            compressed=data[offset]==1;offset+=2;
            for(var i=0;i<2;i++){var end=Array.IndexOf(data,(byte)0,offset);if(end<0)throw new InvalidDataException("iTXtのヘッダーが不正です。");offset=end+1;}
            utf8=true;
        }
        byte[] text;
        if(compressed){
            using var source=new MemoryStream(data,offset,data.Length-offset,false);using var inflater=new ZLibStream(source,CompressionMode.Decompress);using var output=new MemoryStream();
            var buffer=new byte[8192];int count;while((count=inflater.Read(buffer))>0){token.ThrowIfCancellationRequested();if(count>remaining-output.Length)throw new InvalidDataException("展開したテキスト情報が上限4 MiBを超えています。");output.Write(buffer,0,count);}text=output.ToArray();
        }else{if(data.Length-offset>remaining)throw new InvalidDataException("テキスト情報が上限4 MiBを超えています。");text=data[offset..];}
        var value=utf8?Utf8.GetString(text):Encoding.Latin1.GetString(text);
        if(value.Contains('\0'))throw new InvalidDataException("テキストにNULL文字が含まれています。");
        return new(key,value);
    }
    private sealed class ScanLimitException:Exception { }
    private static uint[] MakeCrcTable(){var table=new uint[256];for(uint i=0;i<256;i++){var value=i;for(var bit=0;bit<8;bit++)value=(value&1)!=0?0xedb88320^(value>>1):value>>1;table[i]=value;}return table;}
    private static uint Crc(ReadOnlySpan<byte> type,ReadOnlySpan<byte> data){var crc=uint.MaxValue;foreach(var b in type)crc=CrcTable[(crc^b)&255]^(crc>>8);foreach(var b in data)crc=CrcTable[(crc^b)&255]^(crc>>8);return ~crc;}
}
