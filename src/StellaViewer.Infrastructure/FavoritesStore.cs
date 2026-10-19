using System.Text.Json;
using System.Text.Json.Serialization;

namespace StellaViewer.Infrastructure;

public enum FavoriteKind { Image, Archive, Folder, Pdf }
public sealed record FavoriteEntry(string Path, FavoriteKind Kind)
{
    [JsonIgnore] public string Name { get { var name=System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));return name.Length==0?Path:name; } }
    [JsonIgnore] public string KindLabel => Kind switch { FavoriteKind.Folder=>"フォルダ",FavoriteKind.Archive=>"書庫",FavoriteKind.Pdf=>"PDF",_=>"画像" };
}
public enum FavoriteAvailability { Available, Missing, NetworkBlocked, Unavailable }
public sealed record FavoriteItem(FavoriteEntry Entry, FavoriteAvailability Availability)
{
    public string Path=>Entry.Path;
    public string Name=>Entry.Name;
    public FavoriteKind Kind=>Entry.Kind;
    public string Information=>Entry.KindLabel+"  "+Path;
    public string StateLabel=>Availability switch { FavoriteAvailability.Available=>"利用可能",FavoriteAvailability.Missing=>"見つかりません",FavoriteAvailability.NetworkBlocked=>"ネットワーク閲覧が無効",_=>"利用できません" };
}

/// <summary>Path-keyed operations are merged into the latest shared document, never a UI snapshot.</summary>
public sealed class FavoritesStore(string? filePath=null,bool persist=true)
{
    private sealed record Document(int Version, FavoriteEntry[] Entries, bool ManualOrder=false);
    private FavoriteEntry[] memory=[];
    private readonly object memorySync=new();
    private bool manualOrder;
    private string FilePath=>filePath??System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StellaViewer","favorites.json");
    public static string NormalizePath(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||BrowsePath.IsVirtual(path)||!System.IO.Path.IsPathFullyQualified(path))throw new ArgumentException("お気に入りには物理ファイルまたはフォルダの絶対パスを指定してください。");
        return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    }
    public Task<FavoriteEntry[]> LoadAsync(CancellationToken token=default)=>Task.Run(()=>Locked(()=>Read(),token),token);
    public Task<FavoriteEntry[]> SetAsync(string path,FavoriteKind kind,bool registered,CancellationToken token=default)
    {
        path=NormalizePath(path);
        if(!Enum.IsDefined(kind))throw new ArgumentException("お気に入りの種類が不正です。");
        return Task.Run(()=>Locked(()=>{
            var next=Read().ToList();var index=next.FindIndex(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,path));
            if(registered){if(index<0)next.Add(new(path,kind));else next[index]=new(path,kind);}
            else if(index>=0)next.RemoveAt(index);
            token.ThrowIfCancellationRequested();Write(next.ToArray());return next.ToArray();
        },token),token);
    }
    private T Locked<T>(Func<T> action,CancellationToken token)
    {
        if(!persist){lock(memorySync){token.ThrowIfCancellationRequested();return action();}}
        using var mutex=new Mutex(false,@"Local\StellaViewer.Favorites");var held=false;
        try{
            try{held=mutex.WaitOne(TimeSpan.FromSeconds(5));}catch(AbandonedMutexException){held=true;}
            if(!held)throw new IOException("お気に入りの保存待ちがタイムアウトしました。");
            token.ThrowIfCancellationRequested();return action();
        }finally{if(held)mutex.ReleaseMutex();}
    }
    private FavoriteEntry[] Read()
    {
        if(!persist)return Ordered(memory);
        var document=RecoverableSettings.Read<Document>(FilePath,()=>new(1, []),ValidDocument);
        if(document is null||document.Version!=1||document.Entries is null)throw new InvalidDataException("お気に入りの保存形式に対応していません。");
        var result=new Dictionary<string,FavoriteEntry>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in document.Entries){
            if(entry is null||!Enum.IsDefined(entry.Kind))throw new InvalidDataException("お気に入りの登録内容が不正です。");
            var path=NormalizePath(entry.Path);result[path]=entry with{Path=path};
        }
        manualOrder=document.ManualOrder;
        return Ordered(result.Values.ToArray());
    }
    private FavoriteEntry[] Ordered(FavoriteEntry[] entries)=>manualOrder?entries.ToArray():entries.OrderBy(e=>new FileEntry(e.Path,e.Name,0,default),FolderBrowser.NaturalComparer).ToArray();
    public Task MoveAsync(string path,int direction,CancellationToken token=default)
    {
        path=NormalizePath(path);
        if(direction is not (-1 or 1))throw new ArgumentOutOfRangeException(nameof(direction));
        return Task.Run(()=>Locked(()=>{
            var entries=Read();
            var index=Array.FindIndex(entries,e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,path));
            if(index<0)throw new InvalidOperationException("この登録は別のウィンドウで削除されています。更新してください。");
            var target=index+direction;
            if(target<0||target>=entries.Length)return false;
            (entries[index],entries[target])=(entries[target],entries[index]);
            token.ThrowIfCancellationRequested();manualOrder=true;Write(entries);return true;
        },token),token);
    }
    private static bool ValidDocument(Document value)
    {
        if(value.Version!=1 || value.Entries is null)return false;
        try {
            foreach(var e in value.Entries){if(e is null || !Enum.IsDefined(e.Kind))return false;_ = NormalizePath(e.Path);}
            return true;
        } catch(Exception error) when(error is ArgumentException or NotSupportedException or InvalidDataException or OverflowException) {return false;}
    }
    private void Write(FavoriteEntry[] entries)
    {
        if(!persist){memory=entries;return;}
        RecoverableSettings.Write(FilePath,new Document(1,entries,manualOrder),ValidDocument);
    }
    public static Task<FavoriteItem[]> CheckAvailabilityAsync(IEnumerable<FavoriteEntry> entries,bool allowNetwork,CancellationToken token=default,Func<FavoriteEntry,bool>? exists=null)=>Task.Run(()=>{
        var result=new List<FavoriteItem>();
        foreach(var entry in entries){
            token.ThrowIfCancellationRequested();FavoriteAvailability state;
            try{
                NetworkAccess.Validate(entry.Path,allowNetwork);
                var found=exists?.Invoke(entry)??((File.GetAttributes(entry.Path)&FileAttributes.Directory)!=0)==(entry.Kind==FavoriteKind.Folder);
                state=found?FavoriteAvailability.Available:FavoriteAvailability.Missing;
            }catch(NotSupportedException){state=FavoriteAvailability.NetworkBlocked;}
            catch(FileNotFoundException){state=FavoriteAvailability.Missing;}
            catch(DirectoryNotFoundException){state=FavoriteAvailability.Missing;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){state=FavoriteAvailability.Unavailable;}
            result.Add(new(entry,state));
        }
        token.ThrowIfCancellationRequested();return result.ToArray();
    },token);
}
