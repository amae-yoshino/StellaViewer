using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace StellaViewer.Infrastructure;

public sealed record RecentLocation(string Location,DateTimeOffset OpenedAt)
{
    [JsonIgnore] public string DisplayPath=>BrowsePath.DisplayPath(Location);
    [JsonIgnore] public string Name=>PdfLocation.Parse(Location) is {} pdf?ArchiveLocation.Parse(pdf.DocumentPath)?.Name??Path.GetFileName(pdf.DocumentPath)
        :ArchiveLocation.Parse(Location) is {} archive?archive.Name:new FavoriteEntry(Location,FavoriteKind.Folder).Name;
    [JsonIgnore] public string Kind=>PdfLocation.IsIdentity(Location)?"PDF":ArchiveLocation.Parse(Location) is {} archive?archive.EntryPath.Length==0?"書庫":"書庫内フォルダ":"フォルダ";
    [JsonIgnore] public string Information=>$"{Kind}  {OpenedAt.LocalDateTime:yyyy/MM/dd HH:mm}";
}

/// <summary>Recent browsing places only. Physical locations passed here are known folder paths, never image files.</summary>
public sealed class RecentLocationStore(string? filePath=null,bool persist=true)
{
    public const int MaximumEntries=200;
    private sealed record Document(int Version,RecentLocation[] Entries);
    private RecentLocation[] memory=[];
    private readonly object memorySync=new();
    private string StorePath=>filePath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StellaViewer","recent-locations.json");
    public static string NormalizePlace(string place)
    {
        if(PdfLocation.Parse(place) is {} pdf)return (pdf with{Page=null}).Identity;
        if(ArchiveLocation.Parse(place) is {} archive){
            if(!archive.IsFolder)throw new ArgumentException("履歴には書庫内の画像ファイルを記録できません。");
            return archive.Identity;
        }
        return DirectoryShortcutStore.NormalizePath(place);
    }
    public static string Key(string place)
    {
        place=NormalizePlace(place);
        var canonical=PdfLocation.Parse(place) is {} pdf?new PdfLocation(SourceKey(pdf.DocumentPath)).Identity
            :ArchiveLocation.Parse(place) is {} archive?(archive with{ArchivePath=SourceKey(archive.ArchivePath)}).Identity:place.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    private static string SourceKey(string source)=>ArchiveLocation.Parse(source) is {} archive
        ?(archive with{ArchivePath=SourceKey(archive.ArchivePath)}).Identity:Path.GetFullPath(source).ToUpperInvariant();
    public Task<RecentLocation[]> LoadAsync(CancellationToken token=default)=>Task.Run(()=>Locked(Read,token),token);
    public Task RecordAsync(string place,CancellationToken token=default, Func<bool>? recordingAllowed=null)
    {
        place=NormalizePlace(place);var key=Key(place);
        return Task.Run(()=>Locked(()=>{
            if (recordingAllowed is not null && !recordingAllowed()) return false;
            var previous=Read();var next=new[]{new RecentLocation(place,DateTimeOffset.UtcNow)}.Concat(previous.Where(e=>Key(e.Location)!=key)).Take(MaximumEntries).ToArray();
            token.ThrowIfCancellationRequested();Write(next);return true;
        },token),token);
    }
    public Task RemoveAsync(string? place = null, CancellationToken token = default) => Task.Run(() => Locked(() => {
        var next = place is null ? [] : Read().Where(item => Key(item.Location) != Key(place)).ToArray();
        token.ThrowIfCancellationRequested(); Write(next); return true;
    }, token), token);
    private T Locked<T>(Func<T> operation,CancellationToken token)
    {
        if(!persist){lock(memorySync){token.ThrowIfCancellationRequested();return operation();}}
        using var mutex=new Mutex(false,@"Local\StellaViewer.RecentLocations");var held=false;
        try{
            try{held=mutex.WaitOne(TimeSpan.FromSeconds(5));}catch(AbandonedMutexException){held=true;}
            if(!held)throw new IOException("履歴の使用待ちがタイムアウトしました。");
            token.ThrowIfCancellationRequested();return operation();
        }finally{if(held)mutex.ReleaseMutex();}
    }
    private RecentLocation[] Read()
    {
        if(!persist)return memory.ToArray();
        var document=RecoverableSettings.Read<Document>(StorePath,()=>new(1, []),ValidDocument);
        if(document is null||document.Version!=1||document.Entries is null)throw new InvalidDataException("履歴の保存形式に対応していません。");
        var seen=new HashSet<string>(StringComparer.Ordinal);var rows=new List<RecentLocation>();
        foreach(var entry in document.Entries){
            if(entry is null)throw new InvalidDataException("履歴の内容が不正です。");
            var place=NormalizePlace(entry.Location);
            if(seen.Add(Key(place))&&rows.Count<MaximumEntries)rows.Add(entry with{Location=place});
        }
        return rows.ToArray();
    }
    private static bool ValidDocument(Document value)
    {
        if(value.Version!=1 || value.Entries is null)return false;
        try {
            foreach(var e in value.Entries){if(e is null)return false;_ = NormalizePlace(e.Location);}
            return true;
        } catch(Exception error) when(error is ArgumentException or NotSupportedException or InvalidDataException or OverflowException) {return false;}
    }
    private void Write(RecentLocation[] rows)
    {
        if(!persist){memory=rows;return;}
        RecoverableSettings.Write(StorePath,new Document(1,rows),ValidDocument);
        if(rows.Length==0)RecoverableSettings.Write(StorePath,new Document(1,rows),ValidDocument);
    }
}
